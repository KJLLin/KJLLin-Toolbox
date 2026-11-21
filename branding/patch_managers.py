#!/usr/bin/env python3
"""双引擎：Scoop（主）+ WinGet（可选），其余管理器保持删除状态。"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
PE = ROOT / "src/UniGetUI.PackageEngine.PackageEngine/PEInterface.cs"

ORIGINAL = "            candidates.InsertRange(0, [WinGet, WinInstaller, Scoop, Chocolatey]);"
SINGLE = "            candidates.InsertRange(0, [Scoop]);"
DUAL = "            candidates.InsertRange(0, [Scoop, WinGet]);"
LEGACY_LIST = "            List<IPackageManager?> candidates = [Npm, Bun, Pip, Cargo, Vcpkg, DotNet, PowerShell7];"
LEGACY_ADD = "            candidates.Add(PowerShell);\n"

report = []


def patch(text):
    if DUAL in text:
        report.append("  · PEInterface.cs 已是双引擎，跳过")
        return text
    if SINGLE in text:
        text = text.replace(SINGLE, DUAL, 1)
        report.append("  ✓ PEInterface.cs 单引擎 -> Scoop + WinGet")
    elif ORIGINAL in text:
        text = text.replace(ORIGINAL, DUAL, 1)
        report.append("  ✓ PEInterface.cs 上游形态 -> Scoop + WinGet")
    else:
        report.append("  ! PEInterface.cs 锚点缺失，未改动")
        return text
    if LEGACY_LIST in text:
        text = text.replace(LEGACY_LIST, "            List<IPackageManager?> candidates = [];", 1)
        report.append("  ✓ 移除 npm/pip/cargo/dotnet/powershell7 候选")
    if LEGACY_ADD in text:
        text = text.replace(LEGACY_ADD, "", 1)
        report.append("  ✓ 移除 PowerShell 候选")
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
