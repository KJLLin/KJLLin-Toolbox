"""把 fork 后的 UniGetUI 改造成自己的商店（品牌 + 默认源 + 安装器身份）。

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
CONFIG_PATH = HERE / "polaris.json"

# 改造范围：只动产品身份，不动业务逻辑
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
        print("缺少 branding/polaris.json")
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
    return sub_once(
        text,
        r'name="[^"]*\.Avalonia\.Desktop"',
        'name="%s"' % cfg["assembly_identity"],
        "应用清单身份",
    )


def transform_installer(text, cfg):
    text = sub_once(text, r'#define MyAppName "[^"]*"', '#define MyAppName "%s"' % cfg["product_name"], "安装器名称")
    text = sub_once(text, r'#define MyAppPublisher "[^"]*"', '#define MyAppPublisher "%s"' % cfg["publisher"], "发行方")
    text = sub_once(text, r'#define MyAppPublisherURL "[^"]*"', '#define MyAppPublisherURL "%s"' % cfg["website"], "发行方网址")
    text = sub_once(text, r"AppId=\{\{[^}]*\}", "AppId={{%s}" % cfg["app_id_guid"], "安装器 AppId")
    return text


def transform_scoop(text, cfg):
    """把私有 bucket 注册为 KnownSource；已存在则跳过。"""
    names = [s["name"] for s in cfg.get("sources", [])]
    if any('"%s"' % n in text for n in names):
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

    block = ""
    for src in cfg.get("sources", []):
        block += (
            '                    new ManagerSource(\n'
            '                        this,\n'
            '                        "%s",\n'
            '                        new Uri("%s")\n'
            '                    ),\n' % (src["name"], src["url"])
        )
    new = text.replace(anchor, '''                    new ManagerSource(
                        this,
                        "games",
                        new Uri("https://github.com/Calinou/scoop-games")
                    ),
%s                ],''' % block)
    report.append("  · Scoop 私有源注入 %d 个" % len(cfg.get("sources", [])))
    return new


# 语言包的一行形如   "English source key": "translated value",
# key 是代码里 Translate() 查找用的原文，绝不改写；只改 value。
_LINE = re.compile(r'^(\s*"(?:[^"\\]|\\.)*"\s*:\s*)"((?:[^"\\]|\\.)*)"(,?)\s*$')


def _replace_value(value: str, cfg: dict) -> str:
    product = cfg["product_name"]
    originals = set(cfg.get("replace_aliases", [])) | {"UniGetUI"} | {product}
    for old in sorted(originals, key=len, reverse=True):
        if old and old != product and old in value:
            value = value.replace(old, product)
    for old_url, new_url in cfg.get("url_replacements", {}).items():
        value = value.replace(old_url, new_url)
    return value


def transform_languages(text: str, cfg: dict) -> str:
    lines = []
    for line in text.split("\n"):
        match = _LINE.match(line)
        if not match:
            lines.append(line)
            continue
        key_part, value, comma = match.group(1), match.group(2), match.group(3)
        new_value = _replace_value(value, cfg)
        lines.append('%s"%s"%s' % (key_part, new_value, comma))
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

    print("品牌：%s / %s" % (cfg["product_name"], cfg.get("product_name_zh", "")))
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
    if BRAND_PNG.exists() and BRAND_PNG != ICON_PNG:
        if mode == "--apply":
            shutil.copy2(BRAND_PNG, ICON_PNG)
        report.append("  %s 图标 icon.png" % ("✓" if mode == "--apply" else "~"))

    print("\n".join(report) if report else "没有需要修改的内容")
    print("\n完成。下一步：dotnet build src/UniGetUI.Avalonia/UniGetUI.Avalonia.csproj")
    return 0


if __name__ == "__main__":
    sys.exit(main())
