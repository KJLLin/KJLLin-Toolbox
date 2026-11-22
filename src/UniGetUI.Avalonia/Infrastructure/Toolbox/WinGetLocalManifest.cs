using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniGetUI.Core.Logging;
using UniGetUI.PackageEngine.Toolbox;

namespace UniGetUI.Avalonia.Infrastructure.Toolbox
{
    /// <summary>
    /// Installs a Toolbox-owned application (HPCL and friends) through a local WinGet
    /// manifest.
    ///
    /// Why a local manifest: these apps are not published to the winget community
    /// repository, and we do not want a public REST source. `winget install --manifest`
    /// needs the LocalManifestFiles policy, which only an administrator can enable, so
    /// the first install asks for one UAC elevation, retries once, and gives up quietly
    /// (never blocking the rest of the client) if the user declines.
    /// </summary>
    internal static class WinGetLocalManifest
    {
        private const string ManifestVersion = "1.6.0";
        private const string InstallArguments =
            "--manifest \"{0}\" --accept-package-agreements --accept-source-agreements --silent --disable-interactivity";

        internal static async Task<bool> InstallAsync(ToolboxOwnedAppConfig app, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(app.Identifier)
                || string.IsNullOrWhiteSpace(app.Version)
                || string.IsNullOrWhiteSpace(app.InstallerUrl)
                || string.IsNullOrWhiteSpace(app.Sha256))
            {
                Logger.Warn($"Owned app {app.Key} has incomplete manifest metadata, skipping");
                return false;
            }

            string manifestDirectory = Path.Join(
                ToolboxCloudConfig.DataDirectory, "manifests", app.Key, app.Version ?? "0");
            try
            {
                WriteManifest(app, manifestDirectory);
            }
            catch (Exception e)
            {
                Logger.Warn($"Could not write the manifest for {app.Key}: {e.Message}");
                return false;
            }

            bool installed = await RunInstallAsync(manifestDirectory, ct).ConfigureAwait(false);
            if (installed)
            {
                return true;
            }

            Logger.Info($"Local manifest install of {app.Key} failed, enabling LocalManifestFiles once");
            if (!await EnableLocalManifestFilesAsync(ct).ConfigureAwait(false))
            {
                Logger.Warn($"LocalManifestFiles was not enabled, {app.Key} stays uninstalled");
                return false;
            }

            return await RunInstallAsync(manifestDirectory, ct).ConfigureAwait(false);
        }

        private static void WriteManifest(ToolboxOwnedAppConfig app, string directory)
        {
            Directory.CreateDirectory(directory);

            string publisher = app.Publisher ?? "KJLLin";
            string identifier = app.Identifier!;
            string version = app.Version!;
            string display = app.DisplayName ?? identifier;

            File.WriteAllText(Path.Join(directory, $"{identifier}.yaml"), string.Join("\n", new[]
            {
                $"PackageIdentifier: {identifier}",
                $"PackageVersion: {version}",
                "DefaultLocale: en-US",
                "ManifestType: version",
                $"ManifestVersion: {ManifestVersion}",
                "",
            }), new UTF8Encoding(false));

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

            File.WriteAllText(Path.Join(directory, $"{identifier}.locale.en-US.yaml"), string.Join("\n", new[]
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
            }), new UTF8Encoding(false));

            Logger.Info($"Manifest written to {directory}");
        }

        private static async Task<bool> RunInstallAsync(string manifestDirectory, CancellationToken ct)
        {
            string arguments = string.Format(InstallArguments, manifestDirectory);
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

        /// <summary>One elevation, only when the local-manifest policy actually blocks us.</summary>
        private static async Task<bool> EnableLocalManifestFilesAsync(CancellationToken ct)
        {
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
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                return process.ExitCode == 0;
            }
            catch (Exception e)
            {
                // A declined UAC prompt lands here; it is a normal, non-fatal outcome.
                Logger.Warn($"LocalManifestFiles elevation was refused or failed: {e.Message}");
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
