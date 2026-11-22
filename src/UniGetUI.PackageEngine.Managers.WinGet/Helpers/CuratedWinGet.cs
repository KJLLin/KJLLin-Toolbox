using System;
using System.Collections.Generic;

namespace UniGetUI.PackageEngine.Managers.WingetManager
{
    /// <summary>
    /// The curated catalogue shipped with KJLLin Toolbox.
    ///
    /// Toolbox is a store, not a mirror of the whole winget community source: browsing
    /// exposes only these identifiers, which keeps the list readable and keeps untested
    /// publishers out of the UI. Searching is filtered against the same set.
    ///
    /// The list can be replaced at runtime by the cloud config
    /// (<c>ToolboxCloudConfig.Curated</c>), so adding an app does not need a new build.
    /// </summary>
    internal static class CuratedWinGet
    {
        internal static readonly string[] DefaultIds =
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
        ];

        private static volatile string[] _ids = DefaultIds;
        private static volatile HashSet<string> _lookup = new(DefaultIds, StringComparer.OrdinalIgnoreCase);

        internal static IEnumerable<string> Ordered => _ids;

        internal static bool Contains(string? id)
        {
            return id is not null && _lookup.Contains(id);
        }

        /// <summary>Applies a server-side catalogue. Empty input keeps the built-in list.</summary>
        internal static void ApplyOverride(string[]? ids)
        {
            if (ids is null || ids.Length == 0)
            {
                return;
            }

            _ids = ids;
            _lookup = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        }
    }
}
