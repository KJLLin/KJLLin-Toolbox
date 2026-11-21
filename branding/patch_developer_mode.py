#!/usr/bin/env python3
"""新增「开发人员选项」开关，默认关闭；关闭时隐藏高级设置入口。"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
NAMES = ROOT / "src/UniGetUI.Core.Settings/SettingsEngine_Names.cs"
GENERAL = ROOT / "src/UniGetUI.Avalonia/Views/Pages/SettingsPages/General.axaml"
HOME = ROOT / "src/UniGetUI.Avalonia/Views/Pages/SettingsPages/SettingsHomepage.axaml"
HOME_CS = ROOT / "src/UniGetUI.Avalonia/Views/Pages/SettingsPages/SettingsHomepage.axaml.cs"

ENUM_ANCHOR = "        ScoopShowAllSources,\n"
ENUM_NEW = "        DeveloperMode,\n"

MAP_ANCHOR = '            K.ScoopShowAllSources => "ScoopShowAllSources",\n'
MAP_NEW = '            K.DeveloperMode => "DeveloperMode",\n'

GENERAL_ANCHOR = '            <settings:TranslatedTextBlock Text="Telemetry"\n'
GENERAL_BLOCK = """            <settings:TranslatedTextBlock Text="Developer options"
                                          FontSize="14"
                                          FontWeight="SemiBold"
                                          Margin="44,32,4,8"/>

            <settings:CheckboxCard x:Name="DeveloperModeCard"
                                   SettingName="DeveloperMode"
                                   Text="开发人员选项（显示包管理器设置、扩展、实验功能、代理等高级项）"
                                   CornerRadius="8"/>

"""

HOME_BUTTONS = (
    ("NavigateToAdministratorCommand", "AdministratorButton"),
    ("NavigateToManagersCommand", "ManagersButton"),
    ("NavigateToExtensionsCommand", "ExtensionsButton"),
    ("NavigateToInternetCommand", "InternetButton"),
)

HOME_CS_ANCHOR = """        ExperimentalButton.UnderText = CoreTools.Translate("Beta features and other options that shouldn't be touched");
    }
}
"""
HOME_CS_NEW = """        ExperimentalButton.UnderText = CoreTools.Translate("Beta features and other options that shouldn't be touched");

        ApplyDeveloperMode();
    }

    /// <summary>未打开「开发人员选项」时，隐藏普通用户用不到的高级设置入口。</summary>
    private void ApplyDeveloperMode()
    {
        bool developerMode = global::UniGetUI.Core.SettingsEngine.Settings.Get(
            global::UniGetUI.Core.SettingsEngine.Settings.K.DeveloperMode);

        foreach (
            Control button in new Control[]
            {
                AdministratorButton,
                ManagersButton,
                ExtensionsButton,
                InternetButton,
                ExperimentalButton,
            }
        )
        {
            button.IsVisible = developerMode;
        }
    }
}
"""

report = []


def apply_enum(text):
    if ENUM_NEW.strip() in text:
        report.append("  · SettingsEngine_Names.cs 已有 DeveloperMode")
        return text
    if ENUM_ANCHOR not in text or MAP_ANCHOR not in text:
        report.append("  ! SettingsEngine_Names.cs 锚点缺失")
        return text
    text = text.replace(ENUM_ANCHOR, ENUM_ANCHOR + ENUM_NEW, 1)
    text = text.replace(MAP_ANCHOR, MAP_ANCHOR + MAP_NEW, 1)
    report.append("  ✓ SettingsEngine_Names.cs 新增 DeveloperMode")
    return text


def apply_general(text):
    if "DeveloperModeCard" in text:
        report.append("  · General.axaml 已有开发人员选项")
        return text
    if GENERAL_ANCHOR not in text:
        report.append("  ! General.axaml 锚点缺失")
        return text
    report.append("  ✓ General.axaml 新增开发人员选项开关")
    return text.replace(GENERAL_ANCHOR, GENERAL_BLOCK + GENERAL_ANCHOR, 1)


def apply_home(text):
    changed = False
    for command, name in HOME_BUTTONS:
        anchor = '                Command="{Binding %s}"/>' % command
        if anchor not in text:
            report.append("  ! SettingsHomepage.axaml 缺少 %s" % command)
            continue
        if ('x:Name="%s"' % name) in text:
            continue
        replacement = '                x:Name="%s"\n' % name + anchor
        text = text.replace(anchor, replacement, 1)
        changed = True
    if changed:
        report.append("  ✓ SettingsHomepage.axaml 给高级入口加上命名")
    return text


def apply_home_cs(text):
    bad = "global::UniGetUI.Core.Settings.Settings"
    if bad in text:
        text = text.replace(bad, "global::UniGetUI.Core.SettingsEngine.Settings")
        report.append("  ✓ SettingsHomepage.axaml.cs 修正 Settings 命名空间")
        return text
    if "ApplyDeveloperMode" in text:
        report.append("  · SettingsHomepage.axaml.cs 已有隐藏逻辑")
        return text
    if HOME_CS_ANCHOR not in text:
        report.append("  ! SettingsHomepage.axaml.cs 锚点缺失")
        return text
    report.append("  ✓ SettingsHomepage.axaml.cs 按开关隐藏高级入口")
    return text.replace(HOME_CS_ANCHOR, HOME_CS_NEW, 1)


def main():
    write = "--apply" in sys.argv
    for path, fn in (
        (NAMES, apply_enum),
        (GENERAL, apply_general),
        (HOME, apply_home),
        (HOME_CS, apply_home_cs),
    ):
        if not path.exists():
            report.append("  ! 缺少 %s" % path.relative_to(ROOT))
            continue
        original = path.read_text("utf-8")
        updated = fn(original)
        if updated != original:
            if write:
                path.write_text(updated, "utf-8")
            else:
                report.append("  ~ %s（未写入）" % path.relative_to(ROOT))
    print("\n".join(report) if report else "nothing to do")
    return 0


if __name__ == "__main__":
    sys.exit(main())
