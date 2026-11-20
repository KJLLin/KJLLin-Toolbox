using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Win32;
using UniGetUI.Core.Logging;

namespace UniGetUI.PackageEngine.Managers.ScoopManager
{
    /// <summary>列出从自有 Scoop bucket 装的应用，让它们在 Windows「应用和功能」里可卸载。</summary>
    internal static class ScoopUninstallEntries
    {
        private const string Root = @"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
        private const string Prefix = "KJLLin.Toolbox.";
        private static readonly string[] Own = ["toolbox", "toolbox-beta"];

        [ModuleInitializer]
        internal static void Bootstrap()
        {
            if (!OperatingSystem.IsWindows())
                return;

            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                while (true)
                {
                    try
                    {
                        Sync();
                    }
                    catch (Exception e)
                    {
                        Logger.Warn(e);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(60));
                }
            });
        }

        internal static void Sync()
        {
            string? apps = AppsRoot();
            if (apps is null)
                return;

            HashSet<string> keep = new(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in Directory.EnumerateDirectories(apps))
            {
                string name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                try
                {
                    string current = Path.Join(dir, "current");
                    if (!Directory.Exists(current))
                        continue;

                    string? bucket = Str(Path.Join(current, "install.json"), "bucket");
                    if (bucket is null || !Own.Contains(bucket, StringComparer.OrdinalIgnoreCase))
                        continue;

                    string manifest = Path.Join(current, "manifest.json");
                    string? version = Str(manifest, "version");
                    string? home = Str(manifest, "homepage");
                    string title = Shortcut(manifest) ?? Str(manifest, "description") ?? name;
                    string? bin = Bin(manifest);
                    string icon = current;
                    if (bin is not null && File.Exists(Path.Join(current, bin)))
                        icon = Path.Join(current, bin);

                    RegistryKey? key = Registry.CurrentUser.CreateSubKey($@"{Root}\\{Prefix}{name}", true);
                    if (key is null)
                        continue;

                    using (key)
                    {
                        key.SetValue("DisplayName", title);
                        key.SetValue("DisplayVersion", version ?? "0.0.0");
                        key.SetValue("Publisher", "KJLLin");
                        key.SetValue("InstallLocation", current);
                        key.SetValue("DisplayIcon", icon);
                        key.SetValue("UninstallString", Cmd(name, apps));
                        key.SetValue("QuietUninstallString", Cmd(name, apps));
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                        if (home is not null)
                            key.SetValue("URLInfoAbout", home);
                    }

                    keep.Add(Prefix + name);
                }
                catch (Exception e)
                {
                    Logger.Warn(e);
                }
            }

            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(Root);
            if (root is null)
                return;

            foreach (string sub in root.GetSubKeyNames())
            {
                if (!sub.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || keep.Contains(sub))
                    continue;

                try
                {
                    root.DeleteSubKeyTree(sub, false);
                }
                catch (Exception e)
                {
                    Logger.Warn(e);
                }
            }
        }

        private static string Cmd(string app, string apps)
        {
            string scoop = Path.Join(Path.GetDirectoryName(apps) ?? apps, "shims", "scoop.cmd");
            string reg = "HKCU:" + "\\" + Root + "\\" + Prefix + app;
            return "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"& '"
                + scoop
                + "' uninstall '"
                + app
                + "'; if ($LASTEXITCODE -eq 0) { Remove-Item -LiteralPath '"
                + reg
                + "' -Recurse -Force }\"";
        }

        private static string? AppsRoot()
        {
            List<string> roots = [];
            string? env = Environment.GetEnvironmentVariable("SCOOP");
            if (!string.IsNullOrWhiteSpace(env))
                roots.Add(env);

            roots.Add(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop"));

            foreach (string root in roots)
            {
                string apps = Path.Join(root, "apps");
                if (Directory.Exists(apps))
                    return apps;
            }

            return null;
        }

        private static JsonDocument? Json(string path)
        {
            try
            {
                return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string? Str(string path, string field)
        {
            using JsonDocument? doc = Json(path);
            if (doc is null || !doc.RootElement.TryGetProperty(field, out JsonElement e))
                return null;

            string? text = e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static string? Shortcut(string path)
        {
            using JsonDocument? doc = Json(path);
            if (doc is null || !doc.RootElement.TryGetProperty("shortcuts", out JsonElement list))
                return null;

            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
                return null;

            JsonElement first = list[0];
            if (first.ValueKind != JsonValueKind.Array || first.GetArrayLength() < 2)
                return null;

            JsonElement name = first[1];
            string? text = name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static string? Bin(string path)
        {
            using JsonDocument? doc = Json(path);
            if (doc is null || !doc.RootElement.TryGetProperty("bin", out JsonElement bin))
                return null;

            if (bin.ValueKind == JsonValueKind.String)
                return bin.GetString()?.Trim();

            if (bin.ValueKind != JsonValueKind.Array)
                return null;

            foreach (JsonElement item in bin.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    return item.GetString()?.Trim();
            }

            return null;
        }
    }
}
