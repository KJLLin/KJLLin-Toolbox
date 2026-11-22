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
    /// </summary>
    internal static class CuratedWinGet
    {
        internal static readonly string[] Ids =
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

        private static readonly HashSet<string> Lookup = new(Ids, StringComparer.OrdinalIgnoreCase);

        internal static bool Contains(string? id)
        {
            return id is not null && Lookup.Contains(id);
        }

        internal static IEnumerable<string> Ordered => Ids;
    }
}
