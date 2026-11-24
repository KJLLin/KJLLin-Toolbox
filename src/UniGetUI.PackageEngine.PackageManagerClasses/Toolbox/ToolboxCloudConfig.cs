using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// A banner the cloud can raise in the main window: release notes, maintenance
    /// windows, a broken mirror or a deprecation warning. <see cref="Id"/> makes it
    /// dismissible once per client; leave it empty to show the banner on every start.
    /// </summary>
    public sealed class ToolboxNoticeConfig
    {
        public string Id { get; set; } = "";
        public string Level { get; set; } = "info";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Url { get; set; } = "";
        public string UrlLabel { get; set; } = "";
        public bool Closable { get; set; } = true;
    }

    /// <summary>
    /// One group of the homepage. Sections are presentation only: they never change what
    /// can be installed, they just decide which part of the catalogue the user is looking
    /// at.
    /// </summary>
    public sealed class ToolboxSectionConfig
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>Symbol name under Assets/Symbols; falls back to a per-key default.</summary>
        public string? Icon { get; set; }
        public string[] Ids { get; set; } = [];
    }

    /// <summary>
    /// The cloud side of Toolbox: the curated catalogue, the mirror order, the client
    /// behaviour switches and the self-hosted applications are all server-controlled.
    /// Clients cache the last good copy and refresh every few hours, so a config push
    /// reaches installed devices without shipping a new build.
    /// </summary>
    public sealed class ToolboxConfig
    {
        public int Schema { get; set; } = 2;
        public string Engine { get; set; } = "winget";

        /// <summary>Identifiers shown while browsing. Order is significant: it is the browse order.</summary>
        public string[] Curated { get; set; } = [];

        /// <summary>Identifiers hidden from browsing and from search, even when listed in Curated.</summary>
        public string[] BlockedIds { get; set; } = [];

        /// <summary><c>curated</c> keeps free-text search inside the catalogue, <c>all</c> searches the whole source.</summary>
        public string SearchScope { get; set; } = "curated";

        /// <summary><c>store</c> keeps the updates page to the catalogue, <c>all</c> shows every winget update.</summary>
        public string UpdatesScope { get; set; } = "store";

        /// <summary><c>store</c> keeps the installed page to the catalogue, <c>all</c> (the default) shows every package so anything can still be uninstalled.</summary>
        public string InstalledScope { get; set; } = "all";

        /// <summary>Prefixes used for config and installer downloads, in order. An entry may proxy an absolute URL.</summary>
        public string[] Mirrors { get; set; } = [];

        /// <summary>Hours between config refreshes, clamped to 1..168.</summary>
        public int RefreshHours { get; set; } = 6;

        public bool Shortcuts { get; set; } = true;
        public bool DesktopShortcuts { get; set; }

        /// <summary>Plain-text banner, kept for old configs. <see cref="Notice"/> supersedes it.</summary>
        public string Announcement { get; set; } = "";

        public ToolboxNoticeConfig? Notice { get; set; }
        public ToolboxSectionConfig[] Sections { get; set; } = [];
        public ToolboxOwnedAppConfig[] OwnedApps { get; set; } = [];
    }

    public static class ToolboxCloudConfig
    {
        private const string ConfigFileName = "toolbox-config.json";
        private const string DismissedFileName = "dismissed-notices.json";

        /// <summary>Last-resort endpoints, used when no mirror from the cache resolves.</summary>
        private static readonly string[] FallbackSources =
        [
            "https://gitee.com/kjllin/kjllin-toolbox-bucket/raw/main/toolbox-config.json",
            "https://ghfast.top/https://raw.githubusercontent.com/KJLLin/KJLLin-Toolbox-Bucket/main/toolbox-config.json",
            "https://raw.githubusercontent.com/KJLLin/KJLLin-Toolbox-Bucket/main/toolbox-config.json",
        ];

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static ToolboxConfig? _current;
        private static DateTime _loadedAt = DateTime.MinValue;
        private static HashSet<string>? _dismissed;

        public static string DataDirectory => Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KJLLin Toolbox");

        public static string CacheFile => Path.Join(DataDirectory, "cloud-config.json");

        private static string DismissedFile => Path.Join(DataDirectory, DismissedFileName);

        /// <summary>Built-in fallback, used only before the first successful fetch.</summary>
        public static ToolboxConfig Defaults { get; } = new()
        {
            Engine = "winget",
            SearchScope = "curated",
            RefreshHours = 6,
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

        /// <summary>
        /// Synchronous snapshot for call paths that cannot await (command-line building,
        /// for instance). Never hits the network: it uses the in-memory copy, then the
        /// cache, and otherwise returns null.
        /// </summary>
        public static ToolboxConfig? TryGetCurrent()
        {
            if (_current is not null)
            {
                return _current;
            }

            _current = ReadCache();
            return _current;
        }

        /// <summary>The mirror list actually in force: the cloud one when present, else the built-in list.</summary>
        public static string[] ActiveMirrors
        {
            get
            {
                string[] mirrors = _current?.Mirrors ?? [];
                return mirrors.Length > 0 ? mirrors : Defaults.Mirrors;
            }
        }

        private static TimeSpan RefreshInterval
        {
            get
            {
                int hours = _current?.RefreshHours ?? Defaults.RefreshHours;
                if (hours < 1)
                {
                    hours = 1;
                }
                else if (hours > 168)
                {
                    hours = 168;
                }

                return TimeSpan.FromHours(hours);
            }
        }

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

        /// <summary>
        /// Expands the config file name through the mirror list, then appends the built-in
        /// endpoints. Duplicates are dropped so a mirror that mirrors the GitHub copy is
        /// not queried twice.
        /// </summary>
        private static string[] BuildSources()
        {
            List<string> sources = [];
            foreach (string mirror in ActiveMirrors)
            {
                string prefix = mirror.EndsWith('/') ? mirror : mirror + "/";
                sources.Add(prefix + ConfigFileName);
            }

            foreach (string fallback in FallbackSources)
            {
                if (!sources.Any(source => string.Equals(source, fallback, StringComparison.OrdinalIgnoreCase)))
                {
                    sources.Add(fallback);
                }
            }

            return sources.ToArray();
        }

        /// <summary>
        /// Rewrites an absolute download URL through the first mirror that can proxy it.
        /// A mirror entry shaped like
        /// <c>https://ghfast.top/https://raw.githubusercontent.com/...</c> proxies everything
        /// below github.com, which is how large installers avoid the throttled direct path.
        /// Mirrors that are plain hosts are ignored: their layout is unknown and guessing
        /// one would produce a broken URL. The hash is unaffected, since the bytes are the
        /// same file.
        /// </summary>
        public static string ResolveInstallerUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "";
            }

            if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            foreach (string mirror in ActiveMirrors)
            {
                int marker = mirror.IndexOf("https://", 1, StringComparison.OrdinalIgnoreCase);
                if (marker <= 0)
                {
                    continue;
                }

                string proxy = mirror[..marker];
                return proxy.EndsWith('/') ? proxy + url : proxy + "/" + url;
            }

            return url;
        }

        private static async Task<ToolboxConfig?> FetchAsync(CancellationToken ct)
        {
            foreach (string source in BuildSources())
            {
                try
                {
                    string payload = await Http.GetStringAsync(source, ct).ConfigureAwait(false);
                    ToolboxConfig? config = JsonSerializer.Deserialize(
                        payload, ToolboxJsonContext.Default.ToolboxConfig);
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

                ToolboxConfig? cached = JsonSerializer.Deserialize(
                    File.ReadAllText(CacheFile), ToolboxJsonContext.Default.ToolboxConfig);
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
                File.WriteAllText(
                    CacheFile,
                    JsonSerializer.Serialize(config, ToolboxJsonContext.Default.ToolboxConfig));
            }
            catch (Exception e)
            {
                Logger.Warn($"Toolbox cloud config cache could not be written: {e.Message}");
            }
        }

        /// <summary>Notices the user already closed, so one push is not shown twice.</summary>
        private static HashSet<string> Dismissed
        {
            get
            {
                if (_dismissed is not null)
                {
                    return _dismissed;
                }

                try
                {
                    if (File.Exists(DismissedFile))
                    {
                        string[]? ids = JsonSerializer.Deserialize(
                            File.ReadAllText(DismissedFile), ToolboxJsonContext.Default.StringArray);
                        _dismissed = new HashSet<string>(ids ?? [], StringComparer.OrdinalIgnoreCase);
                        return _dismissed;
                    }
                }
                catch (Exception e)
                {
                    Logger.Warn($"Notice history could not be read: {e.Message}");
                }

                _dismissed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return _dismissed;
            }
        }

        public static bool IsNoticeDismissed(string? id)
        {
            return !string.IsNullOrWhiteSpace(id) && Dismissed.Contains(id);
        }

        public static void DismissNotice(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || !Dismissed.Add(id))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(
                    DismissedFile,
                    JsonSerializer.Serialize(
                        Dismissed.ToArray(), ToolboxJsonContext.Default.StringArray),
                    System.Text.Encoding.UTF8);
            }
            catch (Exception e)
            {
                Logger.Warn($"Notice history could not be written: {e.Message}");
            }
        }
    }
}
