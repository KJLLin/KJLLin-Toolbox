#!/usr/bin/env python3
"""默认首页/更新 = 自有 bucket + 精选第三方应用；源列表始终全部可见，自有源排前。"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SCOOP = ROOT / "src/UniGetUI.PackageEngine.Managers.Scoop/Scoop.cs"
HELPER = ROOT / "src/UniGetUI.PackageEngine.Managers.Scoop/Helpers/ScoopSourceHelper.cs"

CURATED = """
        /// <summary>第三方 bucket 里预批准的应用：不开「更多源」时，它们也出现在首页与更新里。</summary>
        internal static readonly string[] CuratedApps =
        [
            "7zip",
            "vscode",
            "everything",
            "obsidian",
            "powertoys",
            "notepadplusplus",
            "potplayer",
            "snipaste",
            "git",
            "ripgrep",
            "quicklook",
            "bandizip",
            "googlechrome",
            "firefox",
            "steam",
        ];

        internal bool IsCurated(string? name) =>
            name is not null && CuratedApps.Contains(name, StringComparer.OrdinalIgnoreCase);
"""

A1 = """            return false;
        }

        private (string Bucket, string Version)? FindBucketManifestOnDisk(string id)
"""
B1 = (
    """            return false;
        }
""" + CURATED + """
        private (string Bucket, string Version)? FindBucketManifestOnDisk(string id)
"""
)

A2 = """                if (skipSource)
                {
                    continue;
                }

                string[] elements = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (elements.Length < 2)
                {
                    continue;
                }
"""
B2 = """                string[] elements = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (elements.Length < 2)
                {
                    continue;
                }

                if (!ShowThirdPartySources && !IsOwnSource(source.Name) && !IsCurated(elements[0]))
                {
                    continue;
                }
"""

A3 = """            if (ShowThirdPartySources)
            {
                // Own catalogs win"""
B3 = """            if (packages.Count > 0)
            {
                // Own catalogs win"""

A4 = "                    if (!ShowThirdPartySources && !IsOwnSource(installedPackage.Source.Name))\n"
B4 = (
    "                    if (!ShowThirdPartySources && !IsOwnSource(installedPackage.Source.Name) "
    "&& !IsCurated(id))\n"
)

A5 = """                if (scoop is not null && !scoop.ShowThirdPartySources && !scoop.IsOwnSource(name))
                {
                    continue;
                }

"""

A6 = """            if (scoop is not null && scoop.ShowThirdPartySources)
            {
                return sources.OrderBy(item => scoop.IsOwnSource(item.Name) ? 0 : 1).ToList();
            }
"""
B6 = """            if (scoop is not null)
            {
                return sources.OrderBy(item => scoop.IsOwnSource(item.Name) ? 0 : 1).ToList();
            }
"""

report = []


def patch_scoop(text):
    if "CuratedApps" in text:
        report.append("  · Scoop.cs 已有精选白名单，跳过")
        return text
    missing = [name for name, block in (("A1", A1), ("A2", A2), ("A3", A3), ("A4", A4)) if block not in text]
    if missing:
        report.append("  ! Scoop.cs 锚点缺失：" + ",".join(missing))
        return text
    text = text.replace(A1, B1, 1)
    text = text.replace(A2, B2, 1)
    text = text.replace(A3, B3, 1)
    text = text.replace(A4, B4, 1)
    report.append("  ✓ Scoop.cs 加入精选白名单与过滤")
    return text


def patch_helper(text):
    if A5 in text:
        text = text.replace(A5, "", 1)
        report.append("  ✓ ScoopSourceHelper.cs 源列表不再隐藏第三方 bucket")
    else:
        report.append("  · ScoopSourceHelper.cs 第三方过滤已不存在")
    if A6 in text:
        text = text.replace(A6, B6, 1)
        report.append("  ✓ ScoopSourceHelper.cs 自有源始终排最前")
    return text


def main():
    write = "--apply" in sys.argv
    for path, fn in ((SCOOP, patch_scoop), (HELPER, patch_helper)):
        if not path.exists():
            report.append("  ! 缺少 %s" % path.relative_to(ROOT))
            continue
        original = path.read_text("utf-8")
        updated = fn(original)
        if updated != original:
            if write:
                path.write_text(updated, "utf-8")
            else:
                report.append("  ~ %s（未写入）" % path.relative_to(ROOT))
    print("\n".join(report) if report else "nothing to do")
    return 0


if __name__ == "__main__":
    sys.exit(main())
