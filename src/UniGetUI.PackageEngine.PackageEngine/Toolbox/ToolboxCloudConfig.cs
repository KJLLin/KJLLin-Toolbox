using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniGetUI.Core.Logging;

namespace UniGetUI.PackageEngine.Toolbox
{
    /// <summary>
    /// One entry of the curated catalogue that Toolbox installs through a local WinGet
    /// manifest instead of the public winget source. The metadata lives in the cloud
    /// config, so a version bump only needs a config push, not a client release.
    /// </summary>
    public sealed class ToolboxOwnedAppConfig
    {
        public string Key { get; set; } = "";
        public string? Publisher { get; set; }
        public string? Identifier { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? InstallerUrl { get; set; }
        public string? Sha256 { get; set; }
        public string? CommandAlias { get; set; }
        public string? ShortcutName { get; set; }
        public bool AutoInstall { get; set; }
        public bool AutoUpdate { get; set; } = true;
    }

    /// <summary>
    /// The cloud side of Toolbox: the curated catalogue, the mirror order and the
    /// self-hosted applications are all server-controlled. Clients cache the last good
    /// copy and refresh every few hours, so a config push reaches installed devices
    /// without shipping a new build.
    /// </summary>
    public sealed class ToolboxConfig
    {
        public int Schema { get; set; } = 1;
        public string Engine { get; set; } = "winget";
        public string[] Curated { get; set; } = [];
        public string[] Mirrors { get; set; } = [];
        public bool Shortcuts { get; set; } = true;
        public string Announcement { get; set; } = "";
        public ToolboxOwnedAppConfig[] OwnedApps { get; set; } = [];
    }

    public static class ToolboxCloudConfig
    {
        private static readonly string[] Sources =
        [
            "https://gitee.com/kjllin/kjllin-toolbox-bucket/raw/main/toolbox-config.json",
            "https://ghfast.top/https://raw.githubusercontent.com/KJLLin/KJLLin-Toolbox-Bucket/main/toolbox-config.json",
            "https://raw.githubusercontent.com/KJLLin/KJLLin-Toolbox-Bucket/main/toolbox-config.json",
        ];

        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static ToolboxConfig? _current;
        private static DateTime _loadedAt = DateTime.MinValue;

        public static string DataDirectory => Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KJLLin Toolbox");

        public static string CacheFile => Path.Join(DataDirectory, "cloud-config.json");

        /// <summary>Built-in fallback, used only before the first successful fetch.</summary>
        public static ToolboxConfig Defaults { get; } = new()
        {
            Engine = "winget",
            Shortcuts = true,
            Curated =
            [
                "7zip.7zip",
                "Microsoft.VisualStudioCode",
                "voidtools.Everything",
                "Obsidian.Obsidian",
                "Microsoft.PowerToys",
                "Notepad++.Notepad++",
                "Daum.PotPlayer",
                "Git.Git",
                "BurntSushi.ripgrep.MSVC",
                "QL-Win.QuickLook",
                "Bandisoft.Bandizip",
                "Google.Chrome",
                "Mozilla.Firefox",
                "Valve.Steam",
            ],
            Mirrors =
            [
                "https://gitee.com/kjllin/kjllin-toolbox-bucket/raw/main/",
                "https://ghfast.top/https://raw.githubusercontent.com/KJLLin/KJLLin-Toolbox-Bucket/main/",
            ],
        };

        public static ToolboxConfig? Current => _current;

        public static async Task<ToolboxConfig> GetAsync(bool force = false, CancellationToken ct = default)
        {
            if (!force && _current is not null && DateTime.UtcNow - _loadedAt < RefreshInterval)
            {
                return _current;
            }

            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!force && _current is not null && DateTime.UtcNow - _loadedAt < RefreshInterval)
                {
                    return _current;
                }

                ToolboxConfig? cached = ReadCache();
                if (cached is not null)
                {
                    _current ??= cached;
                }

                ToolboxConfig? fetched = await FetchAsync(ct).ConfigureAwait(false);
                if (fetched is not null)
                {
                    _current = fetched;
                    _loadedAt = DateTime.UtcNow;
                    WriteCache(fetched);
                }
                else if (_current is null)
                {
                    _loadedAt = DateTime.UtcNow;
                }

                return _current ?? Defaults;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static async Task<ToolboxConfig?> FetchAsync(CancellationToken ct)
        {
            foreach (string source in Sources)
            {
                try
                {
                    string payload = await Http.GetStringAsync(source, ct).ConfigureAwait(false);
                    ToolboxConfig? config = JsonSerializer.Deserialize<ToolboxConfig>(payload, JsonOptions);
                    if (config is null || config.Schema < 1)
                    {
                        Logger.Warn($"Toolbox cloud config at {source} is not usable");
                        continue;
                    }

                    Logger.Info($"Toolbox cloud config loaded from {source}");
                    return config;
                }
                catch (Exception e)
                {
                    Logger.Warn($"Toolbox cloud config fetch failed for {source}: {e.Message}");
                }
            }

            return null;
        }

        private static ToolboxConfig? ReadCache()
        {
            try
            {
                if (!File.Exists(CacheFile))
                {
                    return null;
                }

                ToolboxConfig? cached = JsonSerializer.Deserialize<ToolboxConfig>(
                    File.ReadAllText(CacheFile), JsonOptions);
                return cached is { Schema: >= 1 } ? cached : null;
            }
            catch (Exception e)
            {
                Logger.Warn($"Toolbox cloud config cache is unreadable: {e.Message}");
                return null;
            }
        }

        private static void WriteCache(ToolboxConfig config)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(CacheFile, JsonSerializer.Serialize(config, JsonOptions));
            }
            catch (Exception e)
            {
                Logger.Warn($"Toolbox cloud config cache could not be written: {e.Message}");
            }
        }
    }
}
