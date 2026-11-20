"""把 Scoop 源改成「默认只看自有源，可在设置里打开更多源，自有源优先」。

上游的源列表来自 `scoop bucket list`（所有 bucket），搜索来自 `scoop-search`（全库搜），
所以只裁剪 KnownSources 没有效果。这里在代码层加一道闸：

- 新增设置项 `ScoopShowAllSources`（默认关），设置页「Scoop sources」开关
- 关闭时：源列表只保留自有 bucket，搜索/首页只保留自有源，更新也只报自有源
- 打开时：全量显示，但自有源排前面，同一应用多个 bucket 都有时保留自有源那条

用法（仓库根目录）：
    python branding/patch_sources.py --check
    python branding/patch_sources.py --apply

幂等：第二次运行报告 already patched，不重复写入。
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCOOP = ROOT / "src/UniGetUI.PackageEngine.Managers.Scoop/Scoop.cs"
HELPER = ROOT / "src/UniGetUI.PackageEngine.Managers.Scoop/Helpers/ScoopSourceHelper.cs"
NAMES = ROOT / "src/UniGetUI.Core.Settings/SettingsEngine_Names.cs"
GENERAL = ROOT / "src/UniGetUI.Avalonia/Views/Pages/SettingsPages/General.axaml"

report = []


def sub(text, anchor, replacement, label):
    if anchor not in text:
        report.append("  ! %s：锚点未找到" % label)
        return text
    report.append("  · %s" % label)
    return text.replace(anchor, replacement, 1)


MEMBERS = (
    "        /// <summary>\n"
    "        /// Whether packages from Scoop buckets the client does not own may be listed too.\n"
    "        /// Off by default, so the store only shows its own catalogs; the user can turn it\n"
    '        /// on from Settings ("Scoop sources").\n'
    "        /// </summary>\n"
    "        internal bool ShowThirdPartySources => Settings.Get(Settings.K.ScoopShowAllSources);\n"
    "\n"
    "        internal bool IsOwnSource(string? name)\n"
    "        {\n"
    "            if (string.IsNullOrEmpty(name))\n"
    "                return false;\n"
    "            foreach (var source in Properties.KnownSources)\n"
    "            {\n"
    "                if (string.Equals(source.Name, name, StringComparison.OrdinalIgnoreCase))\n"
    "                    return true;\n"
    "            }\n"
    "            return false;\n"
    "        }\n"
    "\n"
)


def patch_scoop(text):
    if "ShowThirdPartySources" in text:
        report.append("  · Scoop.cs 已改过，跳过")
        return text

    text = sub(
        text,
        "        private (string Bucket, string Version)? FindBucketManifestOnDisk(string id)",
        MEMBERS + "        private (string Bucket, string Version)? FindBucketManifestOnDisk(string id)",
        "Scoop.cs 成员 ShowThirdPartySources / IsOwnSource",
    )
    text = sub(
        text,
        "            IManagerSource source = Properties.DefaultSource;\n",
        "            IManagerSource source = Properties.DefaultSource;\n            bool skipSource = false;\n",
        "Scoop.cs 搜索：skipSource 标记",
    )
    text = sub(
        text,
        "                    source = SourcesHelper.Factory.GetSourceOrDefault(sourceName);\n",
        "                    source = SourcesHelper.Factory.GetSourceOrDefault(sourceName);\n"
        "                    skipSource = !ShowThirdPartySources && !IsOwnSource(sourceName);\n",
        "Scoop.cs 搜索：按 bucket 判定跳过",
    )
    text = sub(
        text,
        "                string[] elements = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);\n",
        "                if (skipSource)\n"
        "                {\n"
        "                    continue;\n"
        "                }\n"
        "\n"
        "                string[] elements = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);\n",
        "Scoop.cs 搜索：跳过非自有 bucket 段",
    )

    start = text.find("internal IReadOnlyList<Package> ParseSearchOutput(")
    end = text.find("            return packages;\n", start) if start >= 0 else -1
    if start >= 0 and end >= 0:
        text = (
            text[:end]
            + "            if (ShowThirdPartySources)\n"
            "            {\n"
            "                // Own catalogs win: when the same app is served by several buckets,\n"
            "                // keep the entry that comes from one of our sources.\n"
            "                Dictionary<string, Package> preferred = new(StringComparer.OrdinalIgnoreCase);\n"
            "                foreach (Package package in packages\n"
            "                    .OrderBy(candidate => IsOwnSource(candidate.Source.Name) ? 0 : 1)\n"
            "                    .ThenBy(candidate => candidate.Id))\n"
            "                {\n"
            "                    if (!preferred.ContainsKey(package.Id))\n"
            "                        preferred[package.Id] = package;\n"
            "                }\n"
            "                return preferred.Values.ToList();\n"
            "            }\n"
            "\n"
            "            return packages;\n"
            + text[end + len("            return packages;\n"):]
        )
        report.append("  · Scoop.cs 搜索：自有源优先去重")
    else:
        report.append("  ! Scoop.cs 搜索：找不到返回值锚点")

    text = sub(
        text,
        "                    OverridenInstallationOptions options = new(installedPackage.OverridenOptions.Scope);\n",
        "                    if (!ShowThirdPartySources && !IsOwnSource(installedPackage.Source.Name))\n"
        "                        continue;\n"
        "                    OverridenInstallationOptions options = new(installedPackage.OverridenOptions.Scope);\n",
        "Scoop.cs 更新：过滤非自有源",
    )
    return text


def patch_helper(text):
    if "ShowThirdPartySources" in text:
        report.append("  · ScoopSourceHelper.cs 已改过，跳过")
        return text

    text = sub(
        text,
        "            List<ManagerSource> sources = [];\n",
        "            List<ManagerSource> sources = [];\n            var scoop = Manager as Scoop;\n",
        "ScoopSourceHelper 取得管理器",
    )
    text = sub(
        text,
        "                try\n                {\n                    Uri url = BuildSourceUrl(name, source);\n",
        "                if (scoop is not null && !scoop.ShowThirdPartySources && !scoop.IsOwnSource(name))\n"
        "                {\n"
        "                    continue;\n"
        "                }\n"
        "\n"
        "                try\n                {\n                    Uri url = BuildSourceUrl(name, source);\n",
        "ScoopSourceHelper 过滤非自有 bucket",
    )

    anchor = "            return sources;\n        }"
    if anchor in text:
        text = text.replace(
            anchor,
            "            if (scoop is not null && scoop.ShowThirdPartySources)\n"
            "            {\n"
            "                return sources.OrderBy(item => scoop.IsOwnSource(item.Name) ? 0 : 1).ToList();\n"
            "            }\n"
            "\n"
            "            return sources;\n        }",
            1,
        )
        report.append("  · ScoopSourceHelper 自有源排前")
    else:
        report.append("  ! ScoopSourceHelper 找不到返回值锚点")
    return text


def patch_names(text):
    if "ScoopShowAllSources" in text:
        report.append("  · SettingsEngine_Names.cs 已改过，跳过")
        return text
    text = sub(
        text,
        "        EnableScoopCleanupCache,\n",
        "        EnableScoopCleanupCache,\n        ScoopShowAllSources,\n",
        "设置枚举新增 ScoopShowAllSources",
    )
    text = sub(
        text,
        '            K.EnableScoopCleanupCache => "EnableScoopCleanupCache",\n',
        '            K.EnableScoopCleanupCache => "EnableScoopCleanupCache",\n'
        '            K.ScoopShowAllSources => "ScoopShowAllSources",\n',
        "设置项名称映射",
    )
    return text


BLOCK = (
    '            <settings:TranslatedTextBlock Text="Scoop sources"\n'
    '                                          FontSize="14"\n'
    '                                          FontWeight="SemiBold"\n'
    '                                          Margin="44,32,4,8"\n'
    '                                          automation:AutomationProperties.HeadingLevel="2"/>\n'
    "\n"
    '            <settings:CheckboxCard x:Name="ShowAllScoopSourcesCard"\n'
    '                                   SettingName="ScoopShowAllSources"\n'
    '                                   Text="显示上游 Scoop 源（main、extras 等，非自有源；自有源仍优先）"\n'
    '                                   CornerRadius="8"/>\n'
    "\n"
)


def patch_general(text):
    if "ScoopShowAllSources" in text:
        report.append("  · General.axaml 已改过，跳过")
        return text
    anchor = '            <settings:TranslatedTextBlock Text="Telemetry"\n'
    if anchor not in text:
        report.append("  ! General.axaml 找不到插入锚点")
        return text
    report.append("  · General.axaml 新增 Scoop 源开关")
    return text.replace(anchor, BLOCK + anchor, 1)


def process(path, fn, write):
    if not path.exists():
        report.append("  ! 缺少文件 %s" % path.relative_to(ROOT))
        return
    original = path.read_text("utf-8")
    updated = fn(original)
    if updated == original:
        return
    if write:
        path.write_text(updated, "utf-8")
        report.append("  ✓ %s" % path.relative_to(ROOT))
    else:
        report.append("  ~ %s（未写入）" % path.relative_to(ROOT))


def main():
    write = "--apply" in sys.argv
    process(SCOOP, patch_scoop, write)
    process(HELPER, patch_helper, write)
    process(NAMES, patch_names, write)
    process(GENERAL, patch_general, write)
    print("\n".join(report) if report else "nothing to do")
    return 0


if __name__ == "__main__":
    sys.exit(main())
