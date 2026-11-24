using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniGetUI.Core.Logging;

namespace UniGetUI.PackageEngine.Toolbox
{
    /// <summary>
    /// Installs a Toolbox-owned application (HPCL and friends) through a local WinGet
    /// manifest.
    ///
    /// Why a local manifest: these apps are not published to the winget community
    /// repository, and we do not want a public REST source. `winget install --manifest`
    /// needs the LocalManifestFiles policy, which only an administrator can enable, so
    /// the first install asks for one UAC elevation and records that it happened.
    ///
    /// This lives in the engine assembly rather than the UI so both the operation
    /// parameter builder and the background updater can reach it.
    /// </summary>
    public static class WinGetLocalManifest
    {
        private const string ManifestVersion = "1.6.0";
        private const string PolicyMarkerFileName = "localmanifest-policy.ok";
        private const string CommonInstallSwitches =
            "--accept-package-agreements --accept-source-agreements --silent --disable-interactivity";

        // winget 1.4-1.10 accept "--manifest", the newer CLI only registers the short form.
        // Both are tried before falling back to the pinned download.
        private const string InstallArgumentsLong = "--manifest \"{0}\" " + CommonInstallSwitches;
        private const string InstallArgumentsShort = "-m \"{0}\" " + CommonInstallSwitches;

        private static readonly HttpClient DownloadClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10),
        };

        private static readonly object PolicyLock = new();

        private static string PolicyMarkerPath => Path.Join(
            ToolboxCloudConfig.DataDirectory,
            PolicyMarkerFileName);

        /// <summary>The cloud-declared application that owns this package id, if any.</summary>
        public static ToolboxOwnedAppConfig? FindOwned(string? packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return null;
            }

            ToolboxConfig? config = ToolboxCloudConfig.TryGetCurrent();
            if (config is null)
            {
                return null;
            }

            return config.OwnedApps.FirstOrDefault(app =>
                string.Equals(app.Identifier, packageId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(app.Key, packageId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Writes the manifest for this package id and returns its directory, or null when
        /// the id is not a Toolbox-owned app or its metadata is incomplete. Synchronous on
        /// purpose: it runs while building a command line.
        /// </summary>
        public static string? TryWriteManifestFor(string? packageId)
        {
            ToolboxOwnedAppConfig? app = FindOwned(packageId);
            if (app is null || !HasCompleteMetadata(app))
            {
                return null;
            }

            try
            {
                string directory = ManifestDirectory(app);
                WriteManifest(app, directory);
                return directory;
            }
            catch (Exception e)
            {
                Logger.Warn($"Could not write the manifest for {app.Key}: {e.Message}");
                return null;
            }
        }

        public static bool HasCompleteMetadata(ToolboxOwnedAppConfig app)
        {
            return !string.IsNullOrWhiteSpace(app.Identifier)
                && !string.IsNullOrWhiteSpace(app.Version)
                && !string.IsNullOrWhiteSpace(app.InstallerUrl)
                && !string.IsNullOrWhiteSpace(app.Sha256);
        }

        private static string StateFilePath => Path.Join(
            ToolboxCloudConfig.DataDirectory,
            "owned-apps.json");

        /// <summary>Versions the client itself recorded after a successful install.</summary>
        public static Dictionary<string, string> ReadInstalledVersions()
        {
            try
            {
                if (!File.Exists(StateFilePath))
                {
                    return [];
                }

                return System.Text.Json.JsonSerializer.Deserialize(
                    File.ReadAllText(StateFilePath),
                    ToolboxJsonContext.Default.DictionaryStringString) ?? [];
            }
            catch (Exception e)
            {
                Logger.Warn($"Owned app state could not be read: {e.Message}");
                return [];
            }
        }

        /// <summary>Numeric-aware comparison that tolerates strings like 3.16.SNAPSHOT.</summary>
        public static int CompareVersions(string left, string right)
        {
            string[] leftParts = left.Split('.', '-');
            string[] rightParts = right.Split('.', '-');
            int length = Math.Max(leftParts.Length, rightParts.Length);
            for (int i = 0; i < length; i++)
            {
                string leftPart = i < leftParts.Length ? leftParts[i] : "0";
                string rightPart = i < rightParts.Length ? rightParts[i] : "0";

                bool leftIsNumber = int.TryParse(leftPart, out int leftNumber);
                bool rightIsNumber = int.TryParse(rightPart, out int rightNumber);

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

        /// <summary>
        /// Enables <c>LocalManifestFiles</c> once, remembering that it was done so later
        /// installs do not show a UAC prompt again. Returns false when the user declines.
        /// </summary>
        public static bool EnsureLocalManifestPolicy()
        {
            lock (PolicyLock)
            {
                if (File.Exists(PolicyMarkerPath))
                {
                    return true;
                }

                try
                {
                    using Process process = new()
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "winget.exe",
                            Arguments = "settings --enable LocalManifestFiles",
                            Verb = "runas",
                            UseShellExecute = true,
                        },
                    };

                    process.Start();
                    process.WaitForExit(120000);
                    if (process.ExitCode != 0)
                    {
                        Logger.Warn($"LocalManifestFiles was not enabled (exit {process.ExitCode})");
                        return false;
                    }

                    Directory.CreateDirectory(ToolboxCloudConfig.DataDirectory);
                    File.WriteAllText(PolicyMarkerPath, DateTime.UtcNow.ToString("O"));
                    Logger.Info("LocalManifestFiles enabled for local manifest installs");
                    return true;
                }
                catch (Exception e)
                {
                    // A declined UAC prompt lands here; it is a normal, non-fatal outcome.
                    Logger.Warn($"LocalManifestFiles elevation was refused or failed: {e.Message}");
                    return false;
                }
            }
        }

        public static async Task<bool> InstallAsync(
            ToolboxOwnedAppConfig app,
            CancellationToken ct = default)
        {
            if (!HasCompleteMetadata(app))
            {
                Logger.Warn($"Owned app {app.Key} has incomplete manifest metadata, skipping");
                return false;
            }

            string manifestDirectory = ManifestDirectory(app);
            try
            {
                WriteManifest(app, manifestDirectory);
            }
            catch (Exception e)
            {
                Logger.Warn($"Could not write the manifest for {app.Key}: {e.Message}");
                return false;
            }

            bool installed = await RunInstallAsync(manifestDirectory, InstallArgumentsShort, ct)
                .ConfigureAwait(false);
            if (installed)
            {
                return true;
            }

            Logger.Info($"Local manifest install of {app.Key} failed, enabling LocalManifestFiles once");
            if (EnsureLocalManifestPolicy())
            {
                installed = await RunInstallAsync(manifestDirectory, InstallArgumentsLong, ct)
                    .ConfigureAwait(false);
                if (installed)
                {
                    return true;
                }
            }

            Logger.Info($"winget did not accept the local manifest for {app.Key}; "
                + "using the pinned download in portable layout instead");
            return await InstallPortableDirectAsync(app, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Portable-layout install used when winget refuses the local manifest.
        ///
        /// The download is pinned by the cloud config (URL + sha256), so this keeps the same
        /// integrity guarantee winget would give, and drops the binary where winget's
        /// portable installer would put it so the shortcut builder finds it unchanged.
        /// </summary>
        private static async Task<bool> InstallPortableDirectAsync(
            ToolboxOwnedAppConfig app,
            CancellationToken ct)
        {
            try
            {
                string url = ToolboxCloudConfig.ResolveInstallerUrl(app.InstallerUrl);
                string packagesRoot = Path.Join(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Packages");
                string destinationDirectory = Path.Join(
                    packagesRoot,
                    $"{app.Identifier}_{app.Version}");
                string executableName =
                    (app.CommandAlias ?? app.ShortcutName ?? app.Key ?? "app") + ".exe";
                string destination = Path.Join(destinationDirectory, executableName);

                byte[] payload = await DownloadClient.GetByteArrayAsync(url, ct).ConfigureAwait(false);

                string expected = (app.Sha256 ?? "").Trim().ToUpperInvariant();
                string actual = Convert.ToHexString(SHA256.HashData(payload));
                if (expected.Length != actual.Length || expected != actual)
                {
                    Logger.Warn($"sha256 mismatch for {app.Key}: expected {expected}, got {actual}");
                    return false;
                }

                Directory.CreateDirectory(destinationDirectory);
                await File.WriteAllBytesAsync(destination, payload, ct).ConfigureAwait(false);
                Logger.Info($"Installed {app.Key} {app.Version} into {destinationDirectory}");
                return true;
            }
            catch (Exception e)
            {
                Logger.Warn($"Portable install of {app.Key} failed: {e.Message}");
                return false;
            }
        }

        private static string ManifestDirectory(ToolboxOwnedAppConfig app) => Path.Join(
            ToolboxCloudConfig.DataDirectory,
            "manifests",
            app.Key,
            app.Version ?? "0");

        private static void WriteManifest(ToolboxOwnedAppConfig app, string directory)
        {
            Directory.CreateDirectory(directory);

            string publisher = app.Publisher ?? "KJLLin";
            string identifier = app.Identifier!;
            string version = app.Version!;
            string display = app.DisplayName ?? identifier;

            File.WriteAllText(
                Path.Join(directory, $"{identifier}.yaml"),
                string.Join("\n", new[]
                {
                    $"PackageIdentifier: {identifier}",
                    $"PackageVersion: {version}",
                    "DefaultLocale: en-US",
                    "ManifestType: version",
                    $"ManifestVersion: {ManifestVersion}",
                    "",
                }),
                new UTF8Encoding(false));

            List<string> installer = new()
            {
                $"PackageIdentifier: {identifier}",
                $"PackageVersion: {version}",
                "InstallerType: portable",
                "Installers:",
                "  - Architecture: x64",
                $"    InstallerUrl: {ToolboxCloudConfig.ResolveInstallerUrl(app.InstallerUrl)}",
                $"    InstallerSha256: {app.Sha256!.ToUpperInvariant()}",
            };
            if (!string.IsNullOrWhiteSpace(app.CommandAlias))
            {
                installer.Add($"    Commands: [\"{app.CommandAlias}\"]");
            }
            installer.Add("ManifestType: installer");
            installer.Add($"ManifestVersion: {ManifestVersion}");
            installer.Add("");
            File.WriteAllText(
                Path.Join(directory, $"{identifier}.installer.yaml"),
                string.Join("\n", installer),
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Join(directory, $"{identifier}.locale.en-US.yaml"),
                string.Join("\n", new[]
                {
                    $"PackageIdentifier: {identifier}",
                    $"PackageVersion: {version}",
                    "PackageLocale: en-US",
                    $"Publisher: {publisher}",
                    $"PackageName: {display}",
                    "License: MIT",
                    $"ShortDescription: {app.Description ?? display}",
                    "ManifestType: defaultLocale",
                    $"ManifestVersion: {ManifestVersion}",
                    "",
                }),
                new UTF8Encoding(false));

            Logger.Info($"Manifest written to {directory}");
        }

        private static async Task<bool> RunInstallAsync(
            string manifestDirectory,
            string argumentsTemplate,
            CancellationToken ct)
        {
            string arguments = string.Format(argumentsTemplate, manifestDirectory);
            try
            {
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "winget.exe",
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    },
                };

                process.Start();
                string stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                string stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                if (process.ExitCode == 0)
                {
                    Logger.Info($"winget accepted the manifest at {manifestDirectory}");
                    return true;
                }

                Logger.Warn($"winget manifest install exited {process.ExitCode}: {Trim(stdout)} {Trim(stderr)}");
                return false;
            }
            catch (Exception e)
            {
                Logger.Warn($"winget manifest install crashed: {e.Message}");
                return false;
            }
        }

        private static string Trim(string value)
        {
            string flat = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length > 400 ? flat[..400] : flat;
        }
    }
}
