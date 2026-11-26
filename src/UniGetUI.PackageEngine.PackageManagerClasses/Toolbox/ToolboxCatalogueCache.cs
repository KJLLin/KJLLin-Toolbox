using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UniGetUI.Core.Logging;

namespace UniGetUI.PackageEngine.Toolbox
{
    /// <summary>
    /// Remembers the name and version of every curated package.
    ///
    /// Browsing the store means asking `winget search` once per curated id, and those calls
    /// are serialised behind the CLI lock: fourteen of them take 25-40 seconds on a cold
    /// machine. The cache lets the second launch paint the list immediately, while anything
    /// that is missing or older than <see cref="MaxAge"/> is refilled in the background.
    /// </summary>
    public static class ToolboxCatalogueCache
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);
        private const string CacheFileName = "catalogue-cache.json";
        private static readonly object FileLock = new();

        public static string FilePath => Path.Join(
            ToolboxCloudConfig.DataDirectory,
            CacheFileName);

        /// <summary>
        /// Returns whatever was cached, even when it is stale: a slightly old version number
        /// is far better than a blank window. Staleness is reported so the caller can queue
        /// a refresh.
        /// </summary>
        public static Dictionary<string, string>? TryLoadAny(out bool stale)
        {
            stale = true;
            try
            {
                if (!File.Exists(FilePath))
                {
                    Logger.Info("[TOOLBOX] catalogue cache absent (first run)");
                    return null;
                }

                stale = DateTime.UtcNow - File.GetLastWriteTimeUtc(FilePath) > MaxAge;
                string json = File.ReadAllText(FilePath);
                Dictionary<string, string>? entries = JsonSerializer.Deserialize(
                    json,
                    ToolboxJsonContext.Default.DictionaryStringString);

                if (entries is not { Count: > 0 })
                {
                    Logger.Info("[TOOLBOX] catalogue cache empty");
                    return null;
                }

                Logger.Info(
                    $"[TOOLBOX] catalogue cache hit entries={entries.Count} stale={stale}");
                return entries;
            }
            catch (Exception ex)
            {
                Logger.Warn("[TOOLBOX] catalogue cache unreadable");
                Logger.Warn(ex);
                return null;
            }
        }

        public static void Save(Dictionary<string, string> entries)
        {
            if (entries.Count == 0)
            {
                return;
            }

            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(ToolboxCloudConfig.DataDirectory);
                    File.WriteAllText(
                        FilePath,
                        JsonSerializer.Serialize(
                            entries,
                            ToolboxJsonContext.Default.DictionaryStringString));
                    Logger.Info($"[TOOLBOX] catalogue cache written entries={entries.Count}");
                }
                catch (Exception ex)
                {
                    Logger.Warn("[TOOLBOX] catalogue cache not writable");
                    Logger.Warn(ex);
                }
            }
        }

        /// <summary>"7-Zip|24.09" — the pipe keeps it one flat string per package id.</summary>
        public static string Pack(string name, string version)
        {
            return $"{name?.Replace("|", "/")}|{version}";
        }

        public static bool TryUnpack(string packed, out string name, out string version)
        {
            name = "";
            version = "";
            int separator = packed.IndexOf('|');
            if (separator <= 0)
            {
                return false;
            }

            name = packed[..separator];
            version = packed[(separator + 1)..];
            return !string.IsNullOrWhiteSpace(name);
        }
    }
}
