using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UniGetUI.Core.Logging;

namespace UniGetUI.PackageEngine.Managers.ScoopManager
{
    /// <summary>
    /// 精选第三方应用名单，来源是 KJLLin-Toolbox-Bucket 仓库根目录的 curated.json。
    /// 启动时读本地缓存，后台拉远端刷新（每 6 小时一次），拉不到就用代码里内置的名单。
    /// 若存在本地覆盖文件 curated.override.json，则以它为准并停止拉取远端。
    /// 名单里的 "winget" 映射表示该应用在 winget 可用时改由 winget 安装（走原生卸载器），
    /// 这时 Scoop 侧不再展示它，避免同一个应用在商店里出现两条。
    /// </summary>
    internal static class CuratedCatalog
    {
        // 国内优先：jsDelivr 的 GitHub 通道，失败再走加速代理，最后直连。
        private static readonly string[] Mirrors =
        [
            "https://gitee.com/kjllin/kjllin-toolbox-bucket/raw/main/curated.json",
            "https://cdn.jsdelivr.net/gh/KJLLin/KJLLin-Toolbox-Bucket@main/curated.json",
            "https://ghfast.top/https://raw.githubusercontent.com/KJLLin/KJLLin-Toolbox-Bucket/main/curated.json",
            "https://raw.githubusercontent.com/KJLLin/KJLLin-Toolbox-Bucket/main/curated.json",
        ];

        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

        /// <summary>远端拉不到时用的内置 winget 对照表（只列 winget 社区源确认存在的包）。</summary>
        private static readonly Dictionary<string, string> BuiltinWinGetIds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["7zip"] = "7zip.7zip",
            ["vscode"] = "Microsoft.VisualStudioCode",
            ["everything"] = "voidtools.Everything",
            ["obsidian"] = "Obsidian.Obsidian",
            ["powertoys"] = "Microsoft.PowerToys",
            ["notepadplusplus"] = "Notepad++.Notepad++",
            ["potplayer"] = "Daum.PotPlayer",
            ["git"] = "Git.Git",
            ["ripgrep"] = "BurntSushi.ripgrep.MSVC",
            ["quicklook"] = "QL-Win.QuickLook",
            ["bandizip"] = "Bandisoft.Bandizip",
            ["googlechrome"] = "Google.Chrome",
            ["firefox"] = "Mozilla.Firefox",
            ["steam"] = "Valve.Steam",
        };

        private static volatile string[] _names = [];
        private static volatile Dictionary<string, string> _wingetIds = BuiltinWinGetIds;

        [ModuleInitializer]
        internal static void Bootstrap()
        {
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                LoadCache();
                LoadOverride();
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

        /// <summary>该应用对应的 winget 包标识；没有对应包则返回 null。</summary>
        internal static string? WinGetId(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _wingetIds.TryGetValue(name, out string? id) ? id : null;
        }

        /// <summary>
        /// 是否应当交给 winget 处理：有对应 winget 包，且这台机器的 winget 真的可用。
        /// 只有在两个条件都成立时才让 Scoop 侧隐藏该应用。
        /// </summary>
        internal static bool IsDelegatedToWinGet(string? name)
        {
            return WinGetId(name) is not null && WinGetProbe.IsAvailable;
        }

        private static async Task RefreshAsync()
        {
            if (LoadOverride())
            {
                // 本地覆盖文件存在时以它为准，不再拉远端
                return;
            }

            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };

            foreach (string mirror in Mirrors)
            {
                try
                {
                    string body = await client.GetStringAsync(mirror);
                    (string[] names, Dictionary<string, string> winget) = Parse(body);
                    if (names.Length == 0)
                        continue;

                    _names = names;
                    _wingetIds = winget.Count > 0 ? winget : BuiltinWinGetIds;
                    SaveCache(body);
                    return;
                }
                catch (Exception e)
                {
                    Logger.Warn($"[Toolbox] curated list mirror failed ({mirror}): {e.Message}");
                }
            }
        }

        private static (string[] Names, Dictionary<string, string> WinGet) Parse(string json)
        {
            Dictionary<string, string> winget = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("winget", out JsonElement map)
                    && map.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty entry in map.EnumerateObject())
                    {
                        string? id = entry.Value.ValueKind == JsonValueKind.String
                            ? entry.Value.GetString()?.Trim()
                            : null;
                        if (!string.IsNullOrEmpty(entry.Name) && !string.IsNullOrEmpty(id))
                            winget[entry.Name.Trim()] = id;
                    }
                }

                if (!doc.RootElement.TryGetProperty("apps", out JsonElement apps))
                    return ([], winget);
                if (apps.ValueKind != JsonValueKind.Array)
                    return ([], winget);

                List<string> names = [];
                foreach (JsonElement item in apps.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        continue;

                    string? text = item.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(text))
                        names.Add(text);
                }

                return (names.ToArray(), winget);
            }
            catch (Exception e)
            {
                Logger.Warn(e);
                return ([], winget);
            }
        }

        /// <summary>
        /// 本地覆盖名单（%LOCALAPPDATA%\\KJLLin Toolbox\\curated.override.json），
        /// 给测试和开发用：存在即生效，优先于远端与缓存。返回是否生效。
        /// </summary>
        private static bool LoadOverride()
        {
            try
            {
                string path = OverridePath();
                if (!File.Exists(path))
                    return false;

                (string[] names, Dictionary<string, string> winget) = Parse(File.ReadAllText(path));
                if (names.Length == 0)
                    return false;

                _names = names;
                _wingetIds = winget.Count > 0 ? winget : BuiltinWinGetIds;
                return true;
            }
            catch (Exception e)
            {
                Logger.Warn(e);
                return false;
            }
        }

        private static void LoadCache()
        {
            string path = CachePath();
            if (!File.Exists(path))
                return;

            (string[] names, Dictionary<string, string> winget) = Parse(File.ReadAllText(path));
            if (names.Length > 0)
                _names = names;
            if (winget.Count > 0)
                _wingetIds = winget;
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

        private static string OverridePath()
        {
            return Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KJLLin Toolbox",
                "curated.override.json"
            );
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
