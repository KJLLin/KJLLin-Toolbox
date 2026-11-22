"""WinGet-only installer wiring + fixed start page.

Branding itself lives in apply_brand.py. This script only touches UniGetUI.iss and the
start page, and it is fully idempotent:

1. WinGet bootstrap: the installer must deploy EnsureWinGet.ps1 and run it before
   anything else, so a machine that ships without App Installer still ends up with a
   working winget. The script downloads nothing when winget is already present.
2. Any leftover Scoop bootstrap lines are removed (the client is WinGet-only now).
3. The start page stays pinned to Discover.

Usage (from the repo root):
    python branding/patch_installer.py --check
    python branding/patch_installer.py --apply
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONFIG = ROOT / "branding" / "toolbox.json"
ISS = ROOT / "UniGetUI.iss"
VM = ROOT / "src" / "UniGetUI.Avalonia" / "ViewModels" / "MainWindowViewModel.cs"

BS = chr(92)  # backslash
NL = chr(10)  # newline
Q = chr(34)  # double quote

WINGET_SOURCE = (
    "Source: " + Q + "InstallerExtras" + BS + "EnsureWinGet.ps1" + Q
    + "; DestDir: " + Q + "{tmp}" + Q
    + "; Flags: deleteafterinstall ignoreversion"
)
WINGET_RUN = (
    "Filename: " + Q + "powershell.exe" + Q
    + "; Parameters: " + Q
    + "-ExecutionPolicy Bypass -File -NonInteractive " + Q + Q
    + "{tmp}" + BS + "EnsureWinGet.ps1" + Q + Q
    + Q
    + "; StatusMsg: " + Q + "Checking Windows Package Manager (winget)..." + Q
    + "; WorkingDir: {app}; Flags: runhidden runasoriginaluser"
    + "; Check: not CmdLineParamExists('/NoWinGetBootstrap')"
)

SCOOP_MARKERS = ("EnsureScoop.ps1", "scoop-install.ps1")

OLD_STARTUP = (
    "_ => UpgradablePackagesLoader.Instance is { } l && l.Count() > 0 "
    "? PageType.Updates : PageType.Discover,"
)
NEW_STARTUP = "_ => PageType.Discover,"

report = []


def load_config():
    return json.loads(CONFIG.read_text("utf-8"))


def insert_after_section(text, section, line):
    """Insert a line right below a [Section] header, keeping the original line endings."""
    nl = (chr(13) + NL) if (chr(13) + NL) in text else NL
    marker = "[" + section + "]" + nl
    if marker not in text:
        return None
    return text.replace(marker, marker + line + nl, 1)


def strip_scoop_bootstrap(text):
    nl = (chr(13) + NL) if (chr(13) + NL) in text else NL
    kept = []
    removed = 0
    for line in text.split(nl):
        if any(marker in line for marker in SCOOP_MARKERS):
            removed += 1
            continue
        kept.append(line)
    if removed:
        report.append("installer: removed %d leftover Scoop line(s)" % removed)
    return nl.join(kept)


def patch_iss(text, cfg):
    text = strip_scoop_bootstrap(text)

    if "EnsureWinGet.ps1" not in text:
        text_with_source = insert_after_section(text, "Files", WINGET_SOURCE)
        if text_with_source is None:
            report.append("installer: [Files] section not found, WinGet bootstrap not injected")
            return text
        text = text_with_source
        report.append("installer: EnsureWinGet.ps1 added to [Files]")

    if "NoWinGetBootstrap" in text:
        report.append("installer: WinGet bootstrap already wired")
    else:
        text_with_run = insert_after_section(text, "Run", WINGET_RUN)
        if text_with_run is None:
            report.append("installer: [Run] section not found, WinGet bootstrap not scheduled")
        else:
            text = text_with_run
            report.append("installer: EnsureWinGet.ps1 scheduled in [Run]")

    return text


def patch_start_page(text, cfg):
    if cfg.get("startup_page") != "discover":
        return text
    if OLD_STARTUP in text:
        report.append("startup: default page pinned to Discover")
        return text.replace(OLD_STARTUP, NEW_STARTUP, 1)
    if NEW_STARTUP in text:
        report.append("startup: already pinned to Discover")
    else:
        report.append("startup: pattern not found, left untouched")
    return text


def apply_file(path, fn, cfg, write):
    if not path.exists():
        report.append("missing: %s" % path.relative_to(ROOT))
        return
    original = path.read_text("utf-8")
    updated = fn(original, cfg)
    if updated == original:
        return
    if write:
        path.write_text(updated, "utf-8")
        report.append("written: %s" % path.relative_to(ROOT))
    else:
        report.append("would change: %s" % path.relative_to(ROOT))


def main():
    cfg = load_config()
    write = "--apply" in sys.argv
    apply_file(ISS, patch_iss, cfg, write)
    apply_file(VM, patch_start_page, cfg, write)
    print(NL.join(report) if report else "nothing to do")
    return 0


if __name__ == "__main__":
    sys.exit(main())
