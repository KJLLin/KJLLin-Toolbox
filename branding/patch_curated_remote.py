#!/usr/bin/env python3
"""把精选名单从代码内置改成远端 curated.json（失败回退内置）。"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SCOOP = ROOT / "src/UniGetUI.PackageEngine.Managers.Scoop/Scoop.cs"

OLD = """        internal bool IsCurated(string? name) =>
            name is not null && CuratedApps.Contains(name, StringComparer.OrdinalIgnoreCase);
"""
NEW = """        internal bool IsCurated(string? name) => CuratedCatalog.Contains(name);
"""

report = []


def main():
    write = "--apply" in sys.argv
    if not SCOOP.exists():
        print("missing Scoop.cs")
        return 1
    text = SCOOP.read_text("utf-8")
    if NEW in text:
        report.append("  · Scoop.cs 已接远端名单，跳过")
    elif OLD not in text:
        report.append("  ! Scoop.cs 锚点缺失，未接入远端名单")
    else:
        text = text.replace(OLD, NEW, 1)
        report.append("  ✓ Scoop.cs IsCurated 改为读取远端名单")
        if write:
            SCOOP.write_text(text, "utf-8")
        else:
            report.append("  ~ Scoop.cs（未写入）")
    print("\n".join(report) if report else "nothing to do")
    return 0


if __name__ == "__main__":
    sys.exit(main())
