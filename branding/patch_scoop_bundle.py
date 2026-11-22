#!/usr/bin/env python3
"""Scoop is no longer part of the client, so this step only cleans up after it.

The installer used to bundle scoop-install.ps1 next to EnsureScoop.ps1. Both are gone
from UniGetUI.iss now; this script removes any leftover line so an older branch that
still carries them converges to the WinGet-only layout.
"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
ISS = ROOT / "UniGetUI.iss"

MARKERS = ("scoop-install.ps1", "EnsureScoop.ps1")
NL = chr(10)

report = []


def main():
    write = "--apply" in sys.argv
    if not ISS.exists():
        print("missing UniGetUI.iss")
        return 1

    text = ISS.read_text("utf-8")
    nl = (chr(13) + NL) if (chr(13) + NL) in text else NL
    kept = []
    removed = 0
    for line in text.split(nl):
        if any(marker in line for marker in MARKERS):
            removed += 1
            continue
        kept.append(line)

    if not removed:
        print("  · UniGetUI.iss 已无 Scoop 引导行")
        return 0

    updated = nl.join(kept)
    if write:
        ISS.write_text(updated, "utf-8")
        print("  ✓ 移除 %d 行 Scoop 引导" % removed)
    else:
        print("  ~ 可移除 %d 行 Scoop 引导" % removed)
    return 0


if __name__ == "__main__":
    sys.exit(main())
