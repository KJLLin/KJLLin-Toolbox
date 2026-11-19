#!/usr/bin/env python3
"""把 fork 后的 UniGetUI 改造成自己的应用分发客户端（品牌 + 自有源 + 安装器身份）。

用法：
    python branding/apply_brand.py --check    # 只报告将要改什么
    python branding/apply_brand.py --apply    # 真正写入
    python branding/apply_brand.py --verify   # 检查是否还有残留

兼容 Python 3.7+，Windows / macOS / Linux 均可运行。
"""
from __future__ import annotations

import json
import re
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
CONFIG_PATH = HERE / "toolbox.json"

# 改造范围：只动产品身份与源列表，不动业务逻辑
ASSEMBLY = ROOT / "src" / "SharedAssemblyInfo.cs"
MANIFEST = ROOT / "src" / "UniGetUI.Avalonia" / "app.manifest"
INSTALLER = ROOT / "UniGetUI.iss"
SCOOP = ROOT / "src" / "UniGetUI.PackageEngine.Managers.Scoop" / "Scoop.cs"
LANG_DIR = ROOT / "src" / "Languages"
ICON_ICO = ROOT / "src" / "SharedAssets" / "icon.ico"
ICON_PNG = ROOT / "media" / "icon.png"
BRAND_ICO = HERE / "assets" / "icon.ico"
BRAND_PNG = HERE / "assets" / "icon.png"

report = []


def load_config():
    if not CONFIG_PATH.exists():
        print("缺少 branding/toolbox.json")
        sys.exit(2)
    return json.loads(CONFIG_PATH.read_text("utf-8"))


def sub_once(text, pattern, repl, what):
    new, count = re.subn(pattern, repl, text)
    if count:
        report.append("  · %s → %d 处" % (what, count))
    return new


def transform_assembly(text, cfg):
    product = cfg["product_name"]
    for attr in ("AssemblyProduct", "AssemblyDescription", "AssemblyTitle", "AssemblyDefaultAlias"):
        text = sub_once(
            text, r'%s\("[^"]*"\)' % attr, '%s("%s")' % (attr, product), "程序集 %s" % attr
        )
    return text


def transform_manifest(text, cfg):
    # 只改 assemblyIdentity 的 name，兼容任意历史值（改过名也能再次生效）
    return sub_once(
        text,
        r'(<assemblyIdentity[^>]*?name=")([^"]*)(")',
        lambda m: m.group(1) + cfg["assembly_identity"] + m.group(3),
        "应用清单身份",
    )


def transform_installer(text, cfg):
    text = sub_once(text, r'#define MyAppName "[^"]*"', '#define MyAppName "%s"' % cfg["product_name"], "安装器名称")
    text = sub_once(text, r'#define MyAppPublisher "[^"]*"', '#define MyAppPublisher "%s"' % cfg["publisher"], "发行方")
    text = sub_once(text, r'#define MyAppPublisherURL "[^"]*"', '#define MyAppPublisherURL "%s"' % cfg["website"], "发行方网址")
    text = sub_once(text, r"AppId=\{\{[^}]*\}", "AppId={{%s}" % cfg["app_id_guid"], "安装器 AppId")
    return text


def _source_entries(cfg, indent):
    """按 Scoop.cs 里原有的写法生成 ManagerSource 列表项。"""
    out = ""
    for src in cfg.get("sources", []):
        out += (
            '%snew ManagerSource(\n'
            '%s    this,\n'
            '%s    "%s",\n'
            '%s    new Uri("%s")\n'
            '%s),\n'
        ) % (indent, indent, indent, src["name"], indent, src["url"], indent)
    return out


KNOWN_RE = re.compile(r"KnownSources\s*=\s*\n(\s*)\[\n(.*?)\n(\s*)\],", re.S)
DEFAULT_RE = re.compile(
    r'(DefaultSource\s*=\s*new ManagerSource\(\n\s*this,\n\s*)"([^"]*)"(,\n\s*new Uri\(")([^"]*)"'
)


def transform_scoop(text, cfg):
    """only_own_sources=true 时，把 KnownSources 裁剪成只剩自有源。"""
    sources = cfg.get("sources") or []
    if not sources:
        report.append("  ! 没有配置私有源，跳过 Scoop")
        return text

    if not cfg.get("only_own_sources"):
        if any('"%s"' % s["name"] in text for s in sources):
            report.append("  · Scoop 私有源已存在，跳过")
            return text
        anchor = '''                    new ManagerSource(
                        this,
                        "games",
                        new Uri("https://github.com/Calinou/scoop-games")
                    ),
                ],'''
        if anchor not in text:
            report.append("  ! 找不到 Scoop KnownSources 锚点，未注入私有源")
            return text
        block = _source_entries(cfg, "                    ")
        text = text.replace(anchor, '''                    new ManagerSource(
                        this,
                        "games",
                        new Uri("https://github.com/Calinou/scoop-games")
                    ),
%s                ],''' % block)
        report.append("  · Scoop 私有源注入 %d 个" % len(sources))
        return text

    match = KNOWN_RE.search(text)
    if not match:
        report.append("  ! 找不到 KnownSources 数组，未裁剪公共源")
        return text

    indent = match.group(1)
    body = _source_entries(cfg, indent + "    ")
    text = (
        text[: match.start()]
        + "KnownSources =\n%s[\n%s%s]," % (indent, body, match.group(3))
        + text[match.end():]
    )
    removed = match.group(2).count("new ManagerSource")
    report.append("  · Scoop 源列表：%d → %d（只留自有源）" % (removed, len(sources)))

    first = sources[0]
    default = DEFAULT_RE.search(text)
    if default:
        text = (
            text[: default.start()]
            + '%s"%s"%s%s"' % (default.group(1), first["name"], default.group(3), first["url"])
            + text[default.end():]
        )
        report.append('  · 默认源 → "%s"' % first["name"])
    return text


# 语言包的一行形如   "English source key": "translated value",
# key 是代码里 Translate() 查找用的原文，绝不改写；只改 value。
_LINE = re.compile(r'^(\s*"(?:[^"\\]|\\.)*"\s*:\s*)"((?:[^"\\]|\\.)*)"(,?)\s*$')


def _replace_value(value, cfg):
    product = cfg["product_name"]
    originals = set(cfg.get("replace_aliases", [])) | {"UniGetUI"} | {product}
    for old in sorted(originals, key=len, reverse=True):
        if old and old != product and old in value:
            value = value.replace(old, product)
    for old_url, new_url in cfg.get("url_replacements", {}).items():
        value = value.replace(old_url, new_url)
    return value


def transform_languages(text, cfg):
    lines = []
    for line in text.split("\n"):
        match = _LINE.match(line)
        if not match:
            lines.append(line)
            continue
        key_part, value, comma = match.group(1), match.group(2), match.group(3)
        lines.append('%s"%s"%s' % (key_part, _replace_value(value, cfg), comma))
    return "\n".join(lines)


def apply_file(path, fn, cfg, apply_changes):
    if not path.exists():
        return
    original = path.read_text("utf-8")
    updated = fn(original, cfg)
    if updated != original and apply_changes:
        path.write_text(updated, "utf-8")
        report.append("  ✓ %s" % path.relative_to(ROOT))
    elif updated != original:
        report.append("  ~ %s（未写入）" % path.relative_to(ROOT))


def main():
    mode = "--apply" if "--apply" in sys.argv else "--check"
    cfg = load_config()

    if "--verify" in sys.argv:
        leftovers = []
        for path in (ROOT / "src").rglob("*"):
            if path.suffix in (".cs", ".json", ".axaml", ".manifest", ".csproj") and path.is_file():
                try:
                    text = path.read_text("utf-8", errors="ignore")
                except OSError:
                    continue
                if "UniGetUI" in text:
                    leftovers.append(str(path.relative_to(ROOT)))
        print("残留文件：%d 个" % len(leftovers))
        for item in leftovers[:20]:
            print("  -", item)
        return 0

    print("品牌：%s" % cfg["product_name"])
    print("自有源：%s" % ", ".join(s["name"] for s in cfg.get("sources", [])))
    print("模式：%s\n" % mode)

    apply_file(ASSEMBLY, transform_assembly, cfg, mode == "--apply")
    apply_file(MANIFEST, transform_manifest, cfg, mode == "--apply")
    apply_file(INSTALLER, transform_installer, cfg, mode == "--apply")
    apply_file(SCOOP, transform_scoop, cfg, mode == "--apply")

    for path in sorted(LANG_DIR.glob("lang_*.json")):
        apply_file(path, transform_languages, cfg, mode == "--apply")

    if BRAND_ICO.exists() and BRAND_ICO != ICON_ICO:
        if mode == "--apply":
            shutil.copy2(BRAND_ICO, ICON_ICO)
        report.append("  %s 图标 icon.ico" % ("✓" if mode == "--apply" else "~"))
    else:
        report.append("  ! 未提供 branding/assets/icon.ico，跳过图标替换")
    if BRAND_PNG.exists() and BRAND_PNG != ICON_PNG:
        if mode == "--apply":
            shutil.copy2(BRAND_PNG, ICON_PNG)
        report.append("  %s 图标 icon.png" % ("✓" if mode == "--apply" else "~"))

    print("\n".join(report) if report else "没有需要修改的内容")
    print("\n完成。下一步：dotnet build src/UniGetUI.Avalonia/UniGetUI.Avalonia.csproj")
    return 0


if __name__ == "__main__":
    sys.exit(main())
