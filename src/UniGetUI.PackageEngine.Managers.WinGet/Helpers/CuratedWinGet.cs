using System;
using System.Collections.Generic;
using System.Linq;

namespace UniGetUI.PackageEngine.Managers.WingetManager
{
    /// <summary>
    /// The curated catalogue shipped with KJLLin Toolbox.
    ///
    /// Toolbox is a store, not a mirror of the whole winget community source: browsing
    /// exposes only these identifiers, which keeps the list readable and keeps untested
    /// publishers out of the UI. The browse order is the order of the list itself.
    ///
    /// Everything here is replaced at runtime by the cloud config, so adding an app,
    /// pulling a broken one or widening the search scope does not need a new build.
    /// </summary>
    public static class CuratedWinGet
    {
        public static readonly string[] DefaultIds =
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
        private static volatile HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
        private static volatile bool _searchAll;

        /// <summary>Catalogue order, minus anything the cloud pulled.</summary>
        public static IEnumerable<string> Ordered => _ids.Where(id => !_blocked.Contains(id));

        /// <summary>True when free-text search covers the whole source instead of the catalogue.</summary>
        public static bool SearchAll => _searchAll;

        public static bool IsBlocked(string? id)
        {
            return id is not null && _blocked.Contains(id);
        }

        public static bool Contains(string? id)
        {
            return id is not null && _lookup.Contains(id) && !_blocked.Contains(id);
        }

        /// <summary>
        /// Applies a server-side catalogue. A missing or empty identifier list keeps the
        /// current one, so a partial config cannot accidentally empty the store.
        /// </summary>
        public static void ApplyOverride(string[]? ids, string[]? blocked = null, bool? searchAll = null)
        {
            if (ids is { Length: > 0 })
            {
                _ids = ids;
                _lookup = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            }

            _blocked = new HashSet<string>(
                blocked ?? [],
                StringComparer.OrdinalIgnoreCase);

            if (searchAll is { } value)
            {
                _searchAll = value;
            }
        }
    }
}
