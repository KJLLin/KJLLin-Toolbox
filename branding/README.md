# branding

把 UniGetUI 改造成 KJLLin Toolbox 的全部配置与工具都在这里。

- `toolbox.json` —— 品牌参数：产品名、发行方、网址、安装器 GUID、自有源列表、是否只保留自有源，以及 `bootstrap_scoop` / `startup_page`
- `apply_brand.py` —— 主改造脚本，支持 `--check` / `--apply` / `--verify`
- `patch_installer.py` —— 安装器接入 Scoop、启动页固定首页
- `patch_sources.py` —— Scoop 源的可视范围（默认只自有源，设置里可开）
- `patch_curation.py` —— 默认目录 = 自有源 + 精选第三方应用
- `patch_managers.py` —— 只保留 Scoop 一个包管理器
- `patch_uninstall_hooks.py` —— 系统卸载请求先经过 Toolbox 确认
- `assets/` —— 放 `icon.ico` 与 `icon.png`，脚本会自动替换到项目里

想改名、换源、换域名，只改 `toolbox.json`，别改脚本。

## 目录构成

默认首页和「更新」页显示两类东西：

1. 自有 bucket（`toolbox` / `toolbox-beta`）里的应用，永远优先；
2. `patch_curation.py` 里 `CuratedApps` 白名单列出的第三方应用（7zip、vscode、everything、obsidian、powertoys、notepadplusplus、potplayer、snipaste、git、ripgrep、quicklook、bandizip、googlechrome、firefox、steam）。

改精选列表只改那个数组。设置里打开「Scoop sources」后，所有 bucket 的应用都会出现，自有源仍排最前、同名时保留自有源那条。

## 只一个包管理器

`patch_managers.py` 把 `PEInterface.CreateManagers()` 的候选列表改成只有 `Scoop`，npm / pip / .NET Tool / PowerShell Gallery / Cargo / Bun / vcpkg（以及 winget、chocolatey、WinInstaller）都不再加载，所以设置页里也看不到它们。

## 系统卸载入口

`ScoopUninstallEntries.cs` 每 60 秒把自有 bucket 的应用镜像到 `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\KJLLin.Toolbox.<app>`，卸载字符串是 `"<当前 exe>" --scoop-uninstall <app>`。

`patch_uninstall_hooks.py` 把这个参数接到 `App.StartupAsync` 和次级实例处理里，于是点「卸载」会拉起 Toolbox、弹确认框，确认后才跑 `scoop uninstall` 并删除注册表项。

## 构建

推送到 `polaris` 且改动命中 `branding/**`、`src/**`、`UniGetUI.iss`、`global.json` 时会自动构建；机器人带 `[skip ci]` 的品牌提交不会触发，需再推一次改动才会真正编译。

> 精选名单、管理器集合、卸载钩子三处补丁均由 `polaris-brand` 工作流在 CI 里落地，本地无需手工改代码。
