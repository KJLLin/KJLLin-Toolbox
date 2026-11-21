using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UniGetUI.Core.Logging;

namespace UniGetUI.PackageEngine.Managers.ScoopManager
{
    /// <summary>
    /// 精选第三方应用名单，来源是 KJLLin-Toolbox 仓库根目录的 curated.json。
    /// 启动时读本地缓存，后台拉远端刷新（每 6 小时一次），拉不到就用代码里内置的名单。
    /// </summary>
    internal static class CuratedCatalog
    {
        private const string RemoteUrl =
            "https://raw.githubusercontent.com/KJLLin/KJLLin-Toolbox/main/curated.json";

        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

        private static volatile string[] _names = [];

        [ModuleInitializer]
        internal static void Bootstrap()
        {
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                LoadCache();
            }
            catch (Exception e)
            {
                Logger.Warn(e);
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(StartupDelay);
                while (true)
                {
                    try
                    {
                        await RefreshAsync();
                    }
                    catch (Exception e)
                    {
                        Logger.Warn(e);
                    }

                    await Task.Delay(RefreshInterval);
                }
            });
        }

        /// <summary>应用是否在精选名单里（未拉到远端名单时回退到代码内置的那份）。</summary>
        internal static bool Contains(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string[] names = _names.Length > 0 ? _names : Scoop.CuratedApps;
            foreach (string candidate in names)
            {
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static async Task RefreshAsync()
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
            string body = await client.GetStringAsync(RemoteUrl);
            string[] names = Parse(body);
            if (names.Length == 0)
                return;

            _names = names;
            SaveCache(body);
        }

        private static string[] Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("apps", out JsonElement apps))
                    return [];
                if (apps.ValueKind != JsonValueKind.Array)
                    return [];

                List<string> names = [];
                foreach (JsonElement item in apps.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        continue;

                    string? text = item.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(text))
                        names.Add(text);
                }

                return names.ToArray();
            }
            catch (Exception e)
            {
                Logger.Warn(e);
                return [];
            }
        }

        private static void LoadCache()
        {
            string path = CachePath();
            if (!File.Exists(path))
                return;

            string[] names = Parse(File.ReadAllText(path));
            if (names.Length > 0)
                _names = names;
        }

        private static void SaveCache(string body)
        {
            try
            {
                string path = CachePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(path, body);
            }
            catch (Exception e)
            {
                Logger.Warn(e);
            }
        }

        private static string CachePath()
        {
            return Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KJLLin Toolbox",
                "curated.json"
            );
        }
    }
}
