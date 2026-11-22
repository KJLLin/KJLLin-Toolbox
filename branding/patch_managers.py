#!/usr/bin/env python3
"""Single engine: WinGet only.

KJLLin Toolbox used to run Scoop as the primary engine and WinGet as a delegate. The
Scoop engine is gone now, so this script pins the manager list to WinGet and drops the
Scoop singleton that used to live next to it.

Idempotent: every known shape of the candidates line collapses to the WinGet-only one.
"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
PE = ROOT / "src/UniGetUI.PackageEngine.PackageEngine/PEInterface.cs"

TARGET = "            candidates.InsertRange(0, [WinGet]);"
SHAPES = [
    "            candidates.InsertRange(0, [WinGet, WinInstaller, Scoop, Chocolatey]);",
    "            candidates.InsertRange(0, [Scoop]);",
    "            candidates.InsertRange(0, [Scoop, WinGet]);",
    "            candidates.InsertRange(0, [WinGet, Scoop]);",
]
SCOOP_FIELD = "        public static readonly Scoop? Scoop = Create(() => new Scoop());\n"
SCOOP_USING = "using UniGetUI.PackageEngine.Managers.ScoopManager;\n"
LEGACY_LIST = "            List<IPackageManager?> candidates = [Npm, Bun, Pip, Cargo, Vcpkg, DotNet, PowerShell7];"
LEGACY_ADD = "            candidates.Add(PowerShell);\n"

report = []


def patch(text):
    if TARGET in text:
        report.append("  · PEInterface.cs 已是 WinGet 单引擎，跳过")
    else:
        replaced = False
        for shape in SHAPES:
            if shape in text:
                text = text.replace(shape, TARGET, 1)
                report.append("  ✓ PEInterface.cs -> WinGet 单引擎")
                replaced = True
                break
        if not replaced:
            report.append("  ! PEInterface.cs 锚点缺失，未改动")

    if LEGACY_LIST in text:
        text = text.replace(LEGACY_LIST, "            List<IPackageManager?> candidates = [];", 1)
        report.append("  ✓ 移除 npm/pip/cargo/dotnet/powershell7 候选")

    if LEGACY_ADD in text:
        text = text.replace(LEGACY_ADD, "", 1)
        report.append("  ✓ 移除 PowerShell 候选")

    if SCOOP_FIELD in text:
        text = text.replace(SCOOP_FIELD, "", 1)
        report.append("  ✓ 移除 Scoop 引擎实例")

    if SCOOP_USING in text:
        text = text.replace(SCOOP_USING, "", 1)
        report.append("  ✓ 移除 Scoop 命名空间引用")

    return text


def main():
    write = "--apply" in sys.argv
    if not PE.exists():
        print("missing PEInterface.cs")
        return 1
    original = PE.read_text("utf-8")
    updated = patch(original)
    if updated != original and write:
        PE.write_text(updated, "utf-8")
    print("\n".join(report) if report else "nothing to do")
    return 0


if __name__ == "__main__":
    sys.exit(main())
