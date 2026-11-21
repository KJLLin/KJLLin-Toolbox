"""物理删除 Npm / Pip / .NET Tool / PowerShell Gallery / Cargo / Bun / vcpkg 七个包管理器。

在 polaris-brand 工作流里跑，负责：

1. 删掉七个管理器项目目录；
2. 从 PEInterface 的 using、字段、csproj 的 ProjectReference 里摘掉它们；
3. 收起 Avalonia 页面上专属于它们的分支（pip 环境卡片、vcpkg 根目录卡片、bun 版本偏好）；
4. 收拾零散引用（包大小判断、IPC 维护信息、测试工程）。

脚本可重复执行：找不到目标文本就跳过，不会报错。
"""

from __future__ import annotations

import re
import shutil
from pathlib import Path

MANAGERS = {
    "Bun": "UniGetUI.PackageEngine.Managers.Bun",
    "Cargo": "UniGetUI.PackageEngine.Managers.Cargo",
    "Dotnet": "UniGetUI.PackageEngine.Managers.Dotnet",
    "Npm": "UniGetUI.PackageEngine.Managers.Npm",
    "Pip": "UniGetUI.PackageEngine.Managers.Pip",
    "PowerShell": "UniGetUI.PackageEngine.Managers.PowerShell",
    "Vcpkg": "UniGetUI.PackageEngine.Managers.Vcpkg",
}

NAMESPACES = {
    "Bun": "BunManager",
    "Cargo": "CargoManager",
    "Dotnet": "DotNetManager",
    "Npm": "NpmManager",
    "Pip": "PipManager",
    "PowerShell": "PowerShellManager",
    "Vcpkg": "VcpkgManager",
}

FIELDS = {
    "Npm": "Npm",
    "Bun": "Bun",
    "Pip": "Pip",
    "Dotnet": "DotNet",
    "PowerShell": "PowerShell",
    "Cargo": "Cargo",
    "Vcpkg": "Vcpkg",
}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8")


def drop_directories(root: Path) -> list[str]:
    removed = []
    for directory in list(MANAGERS.values()):
        target = root / "src" / directory
        if target.exists():
            shutil.rmtree(target)
            removed.append(directory)
    return removed


def patch_pe_interface(root: Path) -> None:
    path = root / "src/UniGetUI.PackageEngine.PackageEngine/PEInterface.cs"
    if not path.exists():
        return
    text = read(path)

    for name in MANAGERS:
        ns = NAMESPACES[name]
        text = re.sub(rf"^using UniGetUI\.PackageEngine\.Managers\.{ns};\r?\n", "", text, flags=re.M)

    for field in FIELDS.values():
        text = re.sub(
            rf"^ *public static readonly [\w?]+ {field} = Create\(\(\) => new {field}\(\)\);\r?\n",
            "",
            text,
            flags=re.M,
        )

    write(path, text)


def patch_pe_csproj(root: Path) -> None:
    path = root / "src/UniGetUI.PackageEngine.PackageEngine/UniGetUI.PackageEngine.PEInterface.csproj"
    if not path.exists():
        return
    text = read(path)

    for directory in MANAGERS.values():
        text = re.sub(
            rf"^ *<ProjectReference Include=\"\.\.\\{re.escape(directory)}\\{re.escape(directory)}\.csproj\"[^>]*/>\r?\n",
            "",
            text,
            flags=re.M,
        )
        # 跨多行的写法（PowerShell 那条带了子节点）
        text = re.sub(
            rf"^ *<ProjectReference Include=\"\.\.\\{re.escape(directory)}\\{re.escape(directory)}\.csproj\".*?</ProjectReference>\r?\n",
            "",
            text,
            flags=re.M | re.S,
        )

    write(path, text)


def patch_slnx(root: Path) -> None:
    for slnx in (root / "src/UniGetUI.Avalonia.slnx", root / "src/UniGetUI.Windows.slnx"):
        if not slnx.exists():
            continue
        text = read(slnx)
        for directory in MANAGERS.values():
            text = re.sub(
                rf"^ *<Folder Name=\"/{re.escape(directory)}/\">.*?^ *</Folder>\r?\n",
                "",
                text,
                flags=re.M | re.S,
            )
            text = re.sub(
                rf"^ *<Project Path=\"{re.escape(directory)}/{re.escape(directory)}\.csproj\">.*?^ *</Project>\r?\n",
                "",
                text,
                flags=re.M | re.S,
            )
        write(slnx, text)


def remove_method(text: str, signature: str) -> str:
    """按花括号配对删掉一个方法（含上一行的注释行与后面的空行）。"""
    start = text.find(signature)
    if start == -1:
        return text
    brace = text.find("{", start)
    if brace == -1:
        return text
    depth = 0
    index = brace
    while index < len(text):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                break
        index += 1
    end = index + 1
    while end < len(text) and text[end] in " \t":
        end += 1
    if end < len(text) and text[end] == "\n":
        end += 1
    if end < len(text) and text[end] == "\n":
        end += 1
    line_start = text.rfind("\n", 0, start) + 1
    return text[:line_start] + text[end:]


def remove_block_from(text: str, anchor: str, terminator: str) -> str:
    start = text.find(anchor)
    if start == -1:
        return text
    stop = text.find(terminator, start)
    if stop == -1:
        return text
    stop += len(terminator)
    while stop < len(text) and text[stop] in " \t\r":
        stop += 1
    if stop < len(text) and text[stop] == "\n":
        stop += 1
    line_start = text.rfind("\n", 0, start) + 1
    previous = text.rfind("\n", 0, line_start - 1) + 1
    previous_line = text[previous:line_start].strip()
    if previous_line.startswith("//"):
        line_start = previous
    return text[:line_start] + text[stop:]


def patch_manager_page(root: Path) -> None:
    path = root / "src/UniGetUI.Avalonia/Views/Pages/SettingsPages/PackageManagerPage.axaml.cs"
    if not path.exists():
        return
    text = read(path)

    text = text.replace("using UniGetUI.PackageEngine.Managers.PipManager;\n", "")
    text = text.replace("using UniGetUI.PackageEngine.Managers.VcpkgManager;\n", "")

    text = remove_block_from(text, 'if (manager.Name == "Pip")', "            );\n        }\n")
    text = remove_block_from(text, 'case "Bun":', "                break;\n")
    text = remove_block_from(text, 'case "Pip":', "                break;\n")
    text = remove_block_from(text, 'case "vcpkg":', "                break;\n")

    text = remove_method(text, "private SettingsCard BuildPythonEnvironmentsCard(")
    text = remove_method(text, "private Grid BuildPythonEnvironmentRow(string path)")
    text = remove_method(text, "private ButtonCard BuildVcpkgRootCard(")

    write(path, text)


def patch_package_collections(root: Path) -> None:
    path = root / "src/UniGetUI.Avalonia/Models/PackageCollections.cs"
    if not path.exists():
        return
    text = read(path)
    text = text.replace("using UniGetUI.PackageEngine.Managers.PipManager;\n", "")
    text = text.replace("            if (Package.Manager is Pip) return true;\n", "")
    write(path, text)


def patch_ipc_api(root: Path) -> None:
    path = root / "src/UniGetUI.Interface.IpcApi/IpcManagerMaintenanceApi.cs"
    if not path.exists():
        return
    text = read(path)
    text = text.replace("using UniGetUI.PackageEngine.Managers.VcpkgManager;\n", "")
    text = re.sub(
        r"        IReadOnlyList<string> triplets = manager\.Name\.Equals\(\"vcpkg\".*?: \[\];\r?\n\r?\n",
        "",
        text,
        flags=re.S,
    )
    text = text.replace("            AvailableVcpkgTriplets = triplets,\n", "            AvailableVcpkgTriplets = [],\n")
    write(path, text)


def patch_tests(root: Path) -> list[str]:
    tests = root / "src/UniGetUI.PackageEngine.Tests"
    deleted = []
    if tests.exists():
        patterns = [
            r"Managers\.(Npm|Pip|Dotnet|PowerShell|Cargo|Bun|Vcpkg)Manager",
            r"PEInterface\.(Npm|Pip|DotNet|PowerShell|Cargo|Bun|Vcpkg)\b",
        ]
        for file in sorted(tests.rglob("*.cs")):
            body = read(file)
            if any(re.search(pattern, body) for pattern in patterns):
                file.unlink()
                deleted.append(str(file.relative_to(root)))

        csproj = tests / "UniGetUI.PackageEngine.Tests.csproj"
        if csproj.exists():
            body = read(csproj)
            for directory in MANAGERS.values():
                body = re.sub(
                    rf"^ *<ProjectReference Include=\"\.\.\\{re.escape(directory)}\\{re.escape(directory)}\.csproj\"[^>]*/>\r?\n",
                    "",
                    body,
                    flags=re.M,
                )
                body = re.sub(
                    rf"^ *<ProjectReference Include=\"\.\.\\{re.escape(directory)}\\{re.escape(directory)}\.csproj\".*?</ProjectReference>\r?\n",
                    "",
                    body,
                    flags=re.M | re.S,
                )
            write(csproj, body)

    other = root / "src/UniGetUI.Tests"
    if other.exists():
        for file in sorted(other.rglob("*.cs")):
            body = read(file)
            if re.search(r"Managers\.(Npm|Pip|Dotnet|PowerShell|Cargo|Bun|Vcpkg)Manager", body):
                file.unlink()
                deleted.append(str(file.relative_to(root)))

    return deleted


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    removed = drop_directories(root)
    patch_pe_interface(root)
    patch_pe_csproj(root)
    patch_slnx(root)
    patch_manager_page(root)
    patch_package_collections(root)
    patch_ipc_api(root)
    deleted = patch_tests(root)
    print(f"removed directories: {', '.join(removed) or '-'}")
    print(f"removed test files: {len(deleted)}")
    for item in deleted:
        print(f"  - {item}")


if __name__ == "__main__":
    main()
