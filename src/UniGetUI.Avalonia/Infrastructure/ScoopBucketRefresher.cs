using System.Diagnostics;
using System.IO;
using UniGetUI.Core.Logging;

namespace UniGetUI.Avalonia.Infrastructure;

/// <summary>
/// 刷新 Scoop bucket 索引（scoop update），让精选应用的版本信息和上游保持一致。
///
/// 启动后跑一次（成功过就 6 小时内不再跑），设置页的「刷新源」按钮可以随时强制刷。
/// 不刷新索引时 scoop status 只能看到旧的 bucket 清单，精选应用就不会提示新版本。
/// </summary>
internal static class ScoopBucketRefresher
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromHours(6);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>启动后调用，不阻塞界面。</summary>
    internal static void StartInBackground() => _ = RunAsync(false);

    /// <summary>同步刷新；force 时忽略 6 小时间隔。</summary>
    internal static async Task<bool> RunAsync(bool force)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (!force && DateTime.UtcNow - LastRun() < MinInterval)
            return false;

        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(1)))
            return false;

        try
        {
            string? scoop = ScoopPath();
            if (scoop is null)
                return false;

            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        "-NoProfile -ExecutionPolicy Bypass -NonInteractive -Command \"& '"
                        + scoop.Replace("'", "''")
                        + "' update\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                SaveLastRun();
                Logger.Info("[Toolbox] Scoop bucket index refreshed");
                return true;
            }

            Logger.Warn($"[Toolbox] scoop update failed ({process.ExitCode}): {output} {error}");
            return false;
        }
        catch (Exception e)
        {
            Logger.Warn(e);
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string? ScoopPath()
    {
        List<string> candidates = [];

        string? env = Environment.GetEnvironmentVariable("SCOOP");
        if (!string.IsNullOrWhiteSpace(env))
            candidates.Add(Path.Join(env, "shims", "scoop.cmd"));

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Join(profile, "scoop", "shims", "scoop.cmd"));

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string StampPath()
    {
        return Path.Join(PathDirectory(), "last-bucket-refresh.txt");
    }

    private static string PathDirectory()
    {
        return Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KJLLin Toolbox"
        );
    }

    private static DateTime LastRun()
    {
        try
        {
            string path = StampPath();
            if (!File.Exists(path))
                return DateTime.MinValue;

            string text = File.ReadAllText(path).Trim();
            return DateTime.TryParse(text, out DateTime parsed)
                ? parsed.ToUniversalTime()
                : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static void SaveLastRun()
    {
        try
        {
            Directory.CreateDirectory(PathDirectory());
            File.WriteAllText(StampPath(), DateTime.UtcNow.ToString("o"));
        }
        catch (Exception e)
        {
            Logger.Warn(e);
        }
    }
}
