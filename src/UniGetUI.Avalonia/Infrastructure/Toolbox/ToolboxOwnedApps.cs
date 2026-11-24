using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniGetUI.Core.Logging;
using UniGetUI.PackageEngine.Toolbox;

namespace UniGetUI.Avalonia.Infrastructure.Toolbox
{
    /// <summary>
    /// Keeps Toolbox-owned applications in sync with the cloud config.
    ///
    /// The cloud config is the single source of truth: it names the app, its version,
    /// the installer URL and the pinned hash. This class installs missing apps when
    /// AutoInstall is on, upgrades them when AutoUpdate is on, and repairs the start
    /// menu shortcut after every successful run. Nothing here blocks client startup.
    /// </summary>
    internal static class ToolboxOwnedApps
    {
        private const string StateFileName = "owned-apps.json";
        private const string CommandInstall = "--toolbox-own-install";
        private const string CommandList = "--toolbox-own-list";

        private static readonly SemaphoreSlim Gate = new(1, 1);

        internal static string DataDirectory => ToolboxCloudConfig.DataDirectory;

        private static string StateFile => Path.Join(DataDirectory, StateFileName);

        /// <summary>Runs a few seconds after startup so the UI is already responsive.</summary>
        internal static void StartInBackground()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(25)).ConfigureAwait(false);
                    await SyncAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Warn($"Toolbox owned-app sync failed: {e.Message}");
                }
            });
        }

        internal static async Task SyncAsync(CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ToolboxConfig config = await ToolboxCloudConfig.GetAsync(ct: ct).ConfigureAwait(false);
                if (config.OwnedApps.Length == 0)
                {
                    Logger.Info("Cloud config lists no owned apps");
                    return;
                }

                Dictionary<string, string> state = ReadState();
                foreach (ToolboxOwnedAppConfig app in config.OwnedApps)
                {
                    if (string.IsNullOrWhiteSpace(app.Key))
                    {
                        continue;
                    }

                    string? installedVersion = state.GetValueOrDefault(app.Key);
                    bool executablePresent = StartMenuShortcuts.FindExecutable(app) is not null;

                    if (installedVersion is null || !executablePresent)
                    {
                        if (app.AutoInstall)
                        {
                            Logger.Info($"Owned app {app.Key} is missing, installing {app.Version}");
                            if (await InstallCoreAsync(app, ct).ConfigureAwait(false))
                            {
                                state[app.Key] = app.Version ?? "";
                            }
                        }
                        else
                        {
                            Logger.Info($"Owned app {app.Key} {app.Version} is available but not installed");
                        }
                    }
                    else if (app.AutoUpdate && IsNewer(app.Version, installedVersion))
                    {
                        Logger.Info($"Owned app {app.Key} {installedVersion} -> {app.Version}");
                        if (await InstallCoreAsync(app, ct).ConfigureAwait(false))
                        {
                            state[app.Key] = app.Version ?? "";
                        }
                    }
                    else
                    {
                        Logger.Info($"Owned app {app.Key} is current at {installedVersion}");
                    }

                    if (state.ContainsKey(app.Key))
                    {
                        StartMenuShortcuts.Ensure(app, config.Shortcuts, config.DesktopShortcuts);
                    }
                }

                WriteState(state);
            }
            finally
            {
                Gate.Release();
            }
        }

        internal static async Task<bool> InstallByKeyAsync(string key, CancellationToken ct = default)
        {
            ToolboxConfig config = await ToolboxCloudConfig.GetAsync(force: true, ct: ct).ConfigureAwait(false);
            ToolboxOwnedAppConfig? app = config.OwnedApps
                .FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
            if (app is null)
            {
                Logger.Warn($"No owned app named {key} in the cloud config");
                return false;
            }

            bool ok = await InstallCoreAsync(app, ct).ConfigureAwait(false);
            if (ok)
            {
                Dictionary<string, string> state = ReadState();
                state[app.Key] = app.Version ?? "";
                WriteState(state);
                StartMenuShortcuts.Ensure(app, config.Shortcuts, config.DesktopShortcuts);
            }

            return ok;
        }

        internal static Task HandleCommandLineAsync(string[]? args)
        {
            if (args is null || args.Length == 0)
            {
                return Task.CompletedTask;
            }

            if (args.Contains(CommandList))
            {
                _ = Task.Run(async () =>
                {
                    ToolboxConfig config = await ToolboxCloudConfig.GetAsync(force: true).ConfigureAwait(false);
                    foreach (ToolboxOwnedAppConfig app in config.OwnedApps)
                    {
                        Logger.Info($"owned app: {app.Key} {app.Version} autoInstall={app.AutoInstall} autoUpdate={app.AutoUpdate}");
                    }
                });
                return Task.CompletedTask;
            }

            int index = Array.IndexOf(args, CommandInstall);
            if (index >= 0 && index + 1 < args.Length)
            {
                string key = args[index + 1];
                _ = Task.Run(() => InstallByKeyAsync(key));
            }

            return Task.CompletedTask;
        }

        private static async Task<bool> InstallCoreAsync(ToolboxOwnedAppConfig app, CancellationToken ct)
        {
            bool ok = await WinGetLocalManifest.InstallAsync(app, ct).ConfigureAwait(false);
            if (!ok)
            {
                Logger.Warn($"Owned app {app.Key} could not be installed from its local manifest");
            }

            return ok;
        }

        private static bool IsNewer(string? candidate, string? installed)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(installed))
            {
                return true;
            }

            return CompareVersions(candidate, installed) > 0;
        }

        /// <summary>Numeric-aware comparison that tolerates strings like 3.16.SNAPSHOT.</summary>
        internal static int CompareVersions(string left, string right)
        {
            string[] leftParts = left.Split('.', '-');
            string[] rightParts = right.Split('.', '-');
            int length = Math.Max(leftParts.Length, rightParts.Length);
            for (int i = 0; i < length; i++)
            {
                string leftPart = i < leftParts.Length ? leftParts[i] : "0";
                string rightPart = i < rightParts.Length ? rightParts[i] : "0";

                int leftNumber;
                int rightNumber;
                bool leftIsNumber = int.TryParse(leftPart, out leftNumber);
                bool rightIsNumber = int.TryParse(rightPart, out rightNumber);

                int comparison = leftIsNumber && rightIsNumber
                    ? leftNumber.CompareTo(rightNumber)
                    : string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private static Dictionary<string, string> ReadState()
        {
            try
            {
                if (!File.Exists(StateFile))
                {
                    return [];
                }

                return JsonSerializer.Deserialize(
                    File.ReadAllText(StateFile),
                    ToolboxJsonContext.Default.DictionaryStringString) ?? [];
            }
            catch (Exception e)
            {
                Logger.Warn($"Owned app state could not be read: {e.Message}");
                return [];
            }
        }

        private static void WriteState(Dictionary<string, string> state)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(
                    StateFile,
                    JsonSerializer.Serialize(
                        state, ToolboxJsonContext.Default.DictionaryStringString));
            }
            catch (Exception e)
            {
                Logger.Warn($"Owned app state could not be written: {e.Message}");
            }
        }
    }
}
