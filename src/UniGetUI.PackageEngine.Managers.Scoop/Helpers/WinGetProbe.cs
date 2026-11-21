using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UniGetUI.Core.Logging;

namespace UniGetUI.PackageEngine.Managers.ScoopManager
{
    /// <summary>
    /// 判断本机 winget 能不能真的用：既要有 winget.exe（App Installer），还要它能跑起来。
    /// 结果缓存，探测在后台线程完成，不阻塞首屏；探测失败一律当作不可用。
    /// </summary>
    internal static class WinGetProbe
    {
        private static int _once;
        private static volatile bool _available;

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Bootstrap()
        {
            if (!OperatingSystem.IsWindows())
                return;

            _ = Task.Run(Probe);
        }

        /// <summary>winget 是否可用；首次访问会触发一次后台探测。</summary>
        internal static bool IsAvailable
        {
            get
            {
                if (Interlocked.CompareExchange(ref _once, 1, 0) == 0)
                    _ = Task.Run(Probe);
                return _available;
            }
        }

        private static void Probe()
        {
            Interlocked.Exchange(ref _once, 1);
            try
            {
                string? exe = Locate();
                if (exe is null)
                {
                    Logger.Info("[Toolbox] winget not found, staying on Scoop only");
                    _available = false;
                    return;
                }

                using Process probe = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "--version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    },
                };

                probe.Start();
                if (!probe.WaitForExit(5000))
                {
                    try { probe.Kill(true); } catch { }
                    Logger.Info("[Toolbox] winget probe timed out, staying on Scoop only");
                    _available = false;
                    return;
                }

                string version = probe.StandardOutput.ReadToEnd().Trim();
                _available = probe.ExitCode == 0 && version.Length > 0;
                Logger.Info($"[Toolbox] winget probe: exit {probe.ExitCode}, version '{version}'");
            }
            catch (Exception e)
            {
                Logger.Warn($"[Toolbox] winget probe failed: {e.Message}");
                _available = false;
            }
        }

        private static string? Locate()
        {
            string local = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(local))
                return local;

            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                string candidate = Path.Join(dir.Trim(), "winget.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
