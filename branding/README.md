# branding

把 UniGetUI 改造成 KJLLin Toolbox 的全部配置与工具都在这里。

- `toolbox.json` —— 品牌参数：产品名、发行方、网址、安装器 GUID、自有源列表、是否只保留自有源，以及 `bootstrap_scoop` / `startup_page`
- `apply_brand.py` —— 主改造脚本，支持 `--check` / `--apply` / `--verify`
- `patch_installer.py` —— 安装器接入 Scoop、启动页固定首页
- `patch_sources.py` —— Scoop 源的可视范围（默认只自有源，设置里可开）
- `patch_curation.py` —— 默认目录 = 自有源 + 精选第三方应用
- `patch_curated_remote.py` —— 精选名单改为拉远端 `curated.json`
- `patch_managers.py` —— 只保留 Scoop 一个包管理器
- `patch_uninstall_hooks.py` —— 系统卸载请求先经过 Toolbox 确认
- `patch_developer_mode.py` —— 开发人员选项开关，默认关闭时隐藏高级设置入口
- `patch_scoop_bundle.py` —— 把构建时的 Scoop 安装脚本塞进安装包
- `assets/` —— 放 `icon.ico` 与 `icon.png`，脚本会自动替换到项目里

想改名、换源、换域名，只改 `toolbox.json`，别改脚本。

## 目录构成

默认首页和「更新」页显示两类东西：

1. 自有 bucket（`toolbox` / `toolbox-beta`）里的应用，永远优先；
2. 精选第三方应用：名单从 `https://raw.githubusercontent.com/KJLLin/KJLLin-Toolbox/main/curated.json` 拉，缓存到 `%LOCALAPPDATA%\KJLLin Toolbox\curated.json`，每 6 小时刷新一次；拉不到就用 `Scoop.CuratedApps` 内置的那份。

改精选名单只需改 bucket 仓库里的 `curated.json`（`{"apps": ["7zip", "vscode", ...]}`），客户端下一次刷新就生效，不用重新构建。

## 只一个包管理器

`patch_managers.py` 把 `PEInterface.CreateManagers()` 的候选列表改成只有 `Scoop`，npm / pip / .NET Tool / PowerShell Gallery / Cargo / Bun / vcpkg（以及 winget、chocolatey、WinInstaller）都不会被加载。

## 系统卸载入口

`ScoopUninstallEntries.cs` 每 60 秒把自有 bucket 的应用镜像到 `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\KJLLin.Toolbox.<app>`，卸载字符串是 `"<当前 exe>" --scoop-uninstall <app>`。

`patch_uninstall_hooks.py` 把这个参数接到 `App.StartupAsync` 和次级实例处理里，于是点「卸载」会拉起 Toolbox、弹确认框，确认后才跑 `scoop uninstall` 并删除注册表项。

## 开发人员选项

设置 → 通用里新增「开发人员选项」开关（`DeveloperMode`，默认关闭）。关闭时隐藏设置首页上的「管理员权限」「包管理器」「扩展」「网络」「实验性」五个入口，只留普通用户会用的项。

## Scoop 的安装

构建工作流会先把 `get.scoop.sh` 下载成 `InstallerExtras/scoop-install.ps1`，安装器把它和 `EnsureScoop.ps1` 一起拷到 `{tmp}`；安装收尾时优先执行包内的这份脚本，包内缺失才退回网上拉。注意 Scoop 自身在初始化时仍需要网络去拿 git / 7zip / 仓库（完全离线需要把这三个也打进包，体积会涨到 80MB 以上）。

## 构建

推送到 `polaris` 且改动命中 `branding/**`、`src/**`、`UniGetUI.iss`、`global.json` 时会自动构建；机器人带 `[skip ci]` 的品牌提交不会触发，需再推一次改动才会真正编译。
