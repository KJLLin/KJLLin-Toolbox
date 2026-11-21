#!/usr/bin/env python3
"""只保留 Scoop：去掉 npm/pip/dotnet/powershell/cargo/bun/vcpkg（以及 winget/choco）的管理器接入。"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
PE = ROOT / "src/UniGetUI.PackageEngine.PackageEngine/PEInterface.cs"

M1 = "            List<IPackageManager?> candidates = [Npm, Bun, Pip, Cargo, Vcpkg, DotNet, PowerShell7];"
M2 = "            candidates.InsertRange(0, [WinGet, WinInstaller, Scoop, Chocolatey]);"
M3 = "            candidates.Add(PowerShell);\n"

N1 = "            List<IPackageManager?> candidates = [];"
N2 = "            candidates.InsertRange(0, [Scoop]);"

report = []


def patch(text):
    if "candidates.InsertRange(0, [Scoop]);" in text:
        report.append("  · PEInterface.cs 已裁剪过，跳过")
        return text
    if M1 not in text or M2 not in text:
        report.append("  ! PEInterface.cs 锚点缺失，未裁剪")
        return text
    text = text.replace(M1, N1, 1)
    text = text.replace(M2, N2, 1)
    text = text.replace(M3, "", 1)
    report.append("  ✓ PEInterface.cs 只保留 Scoop")
    return text


def main():
    write = "--apply" in sys.argv
    if not PE.exists():
        print("missing PEInterface.cs")
        return 1
    original = PE.read_text("utf-8")
    updated = patch(original)
    if updated != original:
        if write:
            PE.write_text(updated, "utf-8")
        else:
            report.append("  ~ PEInterface.cs（未写入）")
    print("\n".join(report) if report else "nothing to do")
    return 0


if __name__ == "__main__":
    sys.exit(main())
