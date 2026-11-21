#!/usr/bin/env python3
"""把构建时下载的 Scoop 安装脚本一起塞进安装包（[Files] 里多一行）。"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
ISS = ROOT / "UniGetUI.iss"

BS = chr(92)
NL = chr(10)

LINE = (
    'Source: "InstallerExtras' + BS + 'scoop-install.ps1"; DestDir: "{tmp}"; '
    'Flags: deleteafterinstall ignoreversion'
)
ANCHOR_START = 'Source: "InstallerExtras' + BS + 'EnsureScoop.ps1"'

report = []


def main():
    write = "--apply" in sys.argv
    if not ISS.exists():
        print("missing UniGetUI.iss")
        return 1

    text = ISS.read_text("utf-8")
    if "scoop-install.ps1" in text:
        print("  · UniGetUI.iss 已包含 scoop-install.ps1")
        return 0

    nl = (chr(13) + NL) if (chr(13) + NL) in text else NL
    lines = text.split(nl)
    index = next((i for i, line in enumerate(lines) if line.startswith(ANCHOR_START)), None)
    if index is None:
        print("  ! UniGetUI.iss 找不到 EnsureScoop 那一行")
        return 0

    lines.insert(index + 1, LINE)
    updated = nl.join(lines)
    if write:
        ISS.write_text(updated, "utf-8")
        print("  ✓ UniGetUI.iss 加入 scoop-install.ps1")
    else:
        print("  ~ UniGetUI.iss 可写入")
    return 0


if __name__ == "__main__":
    sys.exit(main())
