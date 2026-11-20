"""把安装器接到 Scoop，并把客户端启动页固定成首页（Discover）。

品牌改造由 branding/apply_brand.py 负责；这个脚本只管两件与“装完就能用”相关的事：

1. 安装器（UniGetUI.iss）装完后先确认 Scoop 存在、并注册自有 bucket ——
   客户端只是 Scoop 的图形壳，本机没有 scoop 命令时源永远是空的。
2. 启动页固定成 Discover —— 首页直接列出源里的软件，不再优先跳更新页。

用法（在仓库根目录）：
    python branding/patch_installer.py --check
    python branding/patch_installer.py --apply

幂等：第二次运行只会报告“已存在”，不会重复写入。
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONFIG = ROOT / "branding" / "toolbox.json"
ISS = ROOT / "UniGetUI.iss"
VM = ROOT / "src" / "UniGetUI.Avalonia" / "ViewModels" / "MainWindowViewModel.cs"

BS = chr(92)   # backslash
NL = chr(10)   # newline
Q = chr(34)    # double quote

OLD_STARTUP = (
    "_ => UpgradablePackagesLoader.Instance is { } l && l.Count() > 0 "
    "? PageType.Updates : PageType.Discover,"
)
NEW_STARTUP = "_ => PageType.Discover,"

report = []


def load_config():
    return json.loads(CONFIG.read_text("utf-8"))


def bucket_arg(cfg):
    return ";".join("%s=%s" % (s["name"], s["url"]) for s in cfg.get("sources", []))


def insert_after_section(text, section, line):
    """把一行插到某个 [Section] 标题的下一行，保持文件原有的换行风格。"""
    nl = (chr(13) + NL) if (chr(13) + NL) in text else NL
    marker = "[" + section + "]" + nl
    if marker not in text:
        return None
    return text.replace(marker, marker + line + nl, 1)


def patch_iss(text, cfg):
    if not cfg.get("bootstrap_scoop"):
        return text
    if "EnsureScoop.ps1" in text:
        report.append("installer: Scoop bootstrap already present")
        return text

    source_line = (
        "Source: " + Q + "InstallerExtras" + BS + "EnsureScoop.ps1" + Q
        + "; DestDir: " + Q + "{tmp}" + Q
        + "; Flags: deleteafterinstall ignoreversion"
    )
    run_line = (
        "Filename: " + Q + "powershell.exe" + Q
        + "; Parameters: " + Q
        + "-ExecutionPolicy Bypass -File -NonInteractive " + Q + Q
        + "{tmp}" + BS + "EnsureScoop.ps1" + Q + Q
        + " -BucketUrls '" + bucket_arg(cfg) + "'"
        + Q
        + "; StatusMsg: " + Q + "Preparing Scoop and the KJLLin Toolbox sources..." + Q
        + "; WorkingDir: {app}; Flags: runhidden runasoriginaluser"
        + "; Check: not CmdLineParamExists('/NoScoop')"
    )

    text_after_files = insert_after_section(text, "Files", source_line)
    if text_after_files is None:
        report.append("installer: [Files] section not found, nothing injected")
        return text
    text_after_run = insert_after_section(text_after_files, "Run", run_line)
    if text_after_run is None:
        report.append("installer: [Run] section not found, nothing injected")
        return text

    report.append("installer: Scoop bootstrap injected (%s)" % bucket_arg(cfg))
    return text_after_run


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
