#!/usr/bin/env python3
"""让「程序和功能」里的卸载先经过 Toolbox 确认（注入钩子 + 修正注册表路径常量）。"""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
APP = ROOT / "src/UniGetUI.Avalonia/App.axaml.cs"
HANDLER = ROOT / "src/UniGetUI.Avalonia/Infrastructure/UninstallRequestHandler.cs"

HOOK = "        _ = UninstallRequestHandler.HandleAsync(mainWindow, args);\n"

A1 = """    private static async Task StartupAsync(MainWindow mainWindow, string[] args)
    {
"""
A2 = """        mainWindow.ShowFromTray();

        _ = StartupArgumentProcessor.ProcessAsync(args);
"""

BS = chr(92)
BAD_KEY = (
    '@"Software' + BS * 2 + "Microsoft" + BS * 2 + "Windows" + BS * 2
    + "CurrentVersion" + BS * 2 + "Uninstall" + BS * 2 + "KJLLin.Toolbox." + '"'
)
GOOD_KEY = (
    '@"Software' + BS + "Microsoft" + BS + "Windows" + BS
    + "CurrentVersion" + BS + "Uninstall" + BS + "KJLLin.Toolbox." + '"'
)

report = []


def patch_app(text):
    if "UninstallRequestHandler.HandleAsync" in text:
        report.append("  · App.axaml.cs 已有卸载钩子，跳过")
        return text
    if A1 not in text or A2 not in text:
        report.append("  ! App.axaml.cs 锚点缺失，未注入")
        return text
    text = text.replace(A1, A1 + HOOK, 1)
    text = text.replace(A2, HOOK + "\n" + A2, 1)
    report.append("  ✓ App.axaml.cs 注入卸载确认钩子")
    return text


def patch_handler(text):
    if BAD_KEY in text:
        report.append("  ✓ UninstallRequestHandler.cs 修正注册表路径分隔符")
        return text.replace(BAD_KEY, GOOD_KEY)
    report.append("  · UninstallRequestHandler.cs 路径已正确")
    return text


def main():
    write = "--apply" in sys.argv
    for path, fn in ((APP, patch_app), (HANDLER, patch_handler)):
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
