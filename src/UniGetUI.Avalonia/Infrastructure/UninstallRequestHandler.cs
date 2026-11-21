using System.Diagnostics;
using Avalonia.Threading;
using Microsoft.Win32;
using UniGetUI.Avalonia.Views;
using UniGetUI.Avalonia.Views.DialogPages;
using UniGetUI.Core.Logging;
using UniGetUI.Core.Tools;

namespace UniGetUI.Avalonia.Infrastructure;

/// <summary>
/// Handles the --scoop-uninstall &lt;app&gt; argument Windows passes when the user picks
/// Uninstall for a package that KJLLin Toolbox registered in Apps &amp; Features.
/// </summary>
internal static class UninstallRequestHandler
{
    private const string Flag = "--scoop-uninstall";
    private const string KeyPrefix = @"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\KJLLin.Toolbox.";

    internal static async Task HandleAsync(MainWindow mainWindow, string[]? args)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string? app = ParseApp(args);
        if (app is null)
            return;

        // Windows may start us cold, so wait until the window is really on screen before
        // owning a modal dialog.
        for (int i = 0; i < 40 && !mainWindow.IsVisible; i++)
            await Task.Delay(250);

        string display = ReadDisplayName(app);
        if (!await AskAsync(mainWindow, display))
            return;

        await Task.Run(() => RunUninstall(app));
    }

    private static Task<bool> AskAsync(MainWindow mainWindow, string display)
    {
        TaskCompletionSource<bool> result = new();

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                ImmersiveConfirmationDialog dialog = new(
                    "卸载 " + display,
                    "KJLLin Toolbox 会调用 Scoop 卸载「" + display + "」，并清理开始菜单快捷方式。继续吗？",
                    "卸载",
                    "取消"
                )
                {
                    RequireChoice = true,
                };

                await dialog.ShowDialog(mainWindow);
                result.TrySetResult(dialog.Result == true);
            }
            catch (Exception e)
            {
                Logger.Warn(e);
                result.TrySetResult(false);
            }
        });

        return result.Task;
    }

    private static void RunUninstall(string app)
    {
        string scoop = ScoopShim();
        if (scoop.Length == 0)
        {
            Logger.Warn($"scoop.cmd was not found, so {app} cannot be uninstalled");
            return;
        }

        try
        {
            ProcessStartInfo info = new()
            {
                FileName = "powershell.exe",
                Arguments =
                    "-NoProfile -ExecutionPolicy Bypass -Command \"& '" + scoop + "' uninstall '" + app + "'\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using Process? process = Process.Start(info);
            process?.WaitForExit(120000);

            if (process is not null && process.ExitCode == 0)
                RemoveEntry(app);
        }
        catch (Exception e)
        {
            Logger.Warn(e);
        }
    }

    private static string ScoopShim()
    {
        string? root = Environment.GetEnvironmentVariable("SCOOP");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "scoop"
            );
        }

        string shim = Path.Join(root, "shims", "scoop.cmd");
        return File.Exists(shim) ? shim : string.Empty;
    }

    private static string ReadDisplayName(string app)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPrefix + app);
            if (key?.GetValue("DisplayName") is string name && !string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch (Exception e)
        {
            Logger.Warn(e);
        }

        return CoreTools.FormatAsName(app);
    }

    private static void RemoveEntry(string app)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPrefix + app, false);
        }
        catch (Exception e)
        {
            Logger.Warn(e);
        }
    }

    private static string? ParseApp(string[]? args)
    {
        if (args is null)
            return null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(Flag + "=", StringComparison.OrdinalIgnoreCase))
            {
                string value = arg[(Flag.Length + 1)..].Trim();
                if (value.Length > 0)
                    return value;
            }

            if (
                arg.Equals(Flag, StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && !string.IsNullOrWhiteSpace(args[i + 1])
            )
            {
                return args[i + 1].Trim();
            }
        }

        return null;
    }
}
