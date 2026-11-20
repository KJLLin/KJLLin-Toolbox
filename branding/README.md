# branding

把 UniGetUI 改造成 KJLLin Toolbox 的配置与工具。

- `toolbox.json` —— 产品名、发行方、网址、安装器 GUID、安装目录、自有源、界面字面量映射
- `apply_brand.py` —— 幂等改造脚本，支持 `--check` / `--apply` / `--verify`
- `assets/` —— 放 `icon.ico` 与 `icon.png`，脚本会自动替换到项目里

## 为什么需要 `ui_replacements`

窗口标题（`MainWindow.axaml` 的 `Title="Devolutions UniGetUI"`）是**硬编码**字符串，
不走 `Translate()`，所以语言包改了没用。`ui_replacements` 只替换完整短语，
绝不裸替 `UniGetUI`——那会改坏 `using:UniGetUI` 命名空间和 `x:Class`，直接编译失败。

改完推 `branding/**`，CI 会自动跑脚本并提交；构建工作流会重新出包。
