using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UniGetUI.Core.Logging;
using UniGetUI.PackageEngine.Toolbox;

namespace UniGetUI.Avalonia.Infrastructure.Toolbox
{
    /// <summary>
    /// Start menu and desktop shortcuts for Toolbox-owned apps.
    ///
    /// winget's portable installer only drops the binary under
    /// %LOCALAPPDATA%\Microsoft\WinGet\Packages and never creates a shortcut, so the
    /// client builds one itself. Shortcuts are made through WScript.Shell rather than
    /// COM interop types, which keeps them working under trim/AOT builds.
    /// </summary>
    internal static class StartMenuShortcuts
    {
        private static string ProgramsDirectory => Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs");

        private static string DesktopDirectory =>
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        internal static string? FindExecutable(ToolboxOwnedAppConfig app)
        {
            string packages = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");

            try
            {
                if (!Directory.Exists(packages) || string.IsNullOrWhiteSpace(app.Identifier))
                {
                    return null;
                }

                string alias = app.CommandAlias ?? "";
                string[] candidates = Directory
                    .EnumerateDirectories(packages, app.Identifier + "_*")
                    .SelectMany(directory => Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories))
                    .OrderByDescending(path => alias.Length > 0
                        && Path.GetFileNameWithoutExtension(path).Equals(alias, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(path => path.Length)
                    .ToArray();

                return candidates.FirstOrDefault();
            }
            catch (Exception e)
            {
                Logger.Warn($"Could not locate the executable for {app.Key}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates the shortcuts the cloud asked for. <paramref name="shortcutsEnabled"/>
        /// is the master switch for the start menu entry, <paramref name="desktopEnabled"/>
        /// adds a desktop copy on top.
        /// </summary>
        internal static bool Ensure(
            ToolboxOwnedAppConfig app,
            bool shortcutsEnabled,
            bool desktopEnabled = false)
        {
            if (!shortcutsEnabled || string.IsNullOrWhiteSpace(app.Key))
            {
                return false;
            }

            string? target = FindExecutable(app);
            if (target is null)
            {
                Logger.Warn($"No executable found for {app.Key}, shortcut skipped");
                return false;
            }

            string name = app.ShortcutName ?? app.DisplayName ?? app.Key;
            bool startMenu = CreateLink(Path.Join(ProgramsDirectory, name + ".lnk"), target, app, name);
            if (desktopEnabled)
            {
                CreateLink(Path.Join(DesktopDirectory, name + ".lnk"), target, app, name);
            }

            return startMenu;
        }

        private static bool CreateLink(
            string link,
            string target,
            ToolboxOwnedAppConfig app,
            string name)
        {
            if (File.Exists(link))
            {
                return true;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(link) ?? "");
                string script =
                    "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + Escape(link) + "');"
                    + "$s.TargetPath='" + Escape(target) + "';"
                    + "$s.WorkingDirectory='" + Escape(Path.GetDirectoryName(target) ?? "") + "';"
                    + "$s.IconLocation='" + Escape(target) + ",0';"
                    + "$s.Description='" + Escape(app.Description ?? name) + "';"
                    + "$s.Save()";

                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                process.Start();
                process.WaitForExit(30000);
                if (process.ExitCode == 0 && File.Exists(link))
                {
                    Logger.Info($"Shortcut created: {link}");
                    return true;
                }

                Logger.Warn($"Shortcut creation exited {process.ExitCode} for {app.Key}");
                return false;
            }
            catch (Exception e)
            {
                Logger.Warn($"Could not create a shortcut for {app.Key}: {e.Message}");
                return false;
            }
        }

        private static string Escape(string value) => value.Replace("'", "''");
    }
}
