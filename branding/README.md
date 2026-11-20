# branding

把 UniGetUI 改造成 KJLLin Toolbox 的全部配置与工具都在这里。

- `toolbox.json` —— 品牌参数：产品名、发行方、网址、安装器 GUID、自有源列表、是否只保留自有源，以及 `bootstrap_scoop` / `startup_page` 两个开关
- `apply_brand.py` —— 幂等改造脚本，支持 `--check` / `--apply` / `--verify`
- `patch_installer.py` —— 把安装器接到 Scoop、并把启动页固定成首页，支持 `--check` / `--apply`
- `patch_sources.py` —— Scoop 源的可视范围（默认只自有源，设置里可开、自有源优先）
- `assets/` —— 放 `icon.ico` 与 `icon.png`，脚本会自动替换到项目里

想改名、换源、换域名，只改 `toolbox.json`，别改脚本。

`only_own_sources` 为 `true` 时，`Scoop.cs` 的 `KnownSources` 会被整体重写为配置里的源，并把 `DefaultSource` 指向第一个源。

## 装完就能用

`bootstrap_scoop` 为 `true` 时，`patch_installer.py` 会往 `UniGetUI.iss` 里插两行：

1. `[Files]` —— 把 `InstallerExtras/EnsureScoop.ps1` 拷到 `{tmp}`
2. `[Run]` —— 安装收尾时以原用户身份、隐藏窗口跑它：检测 `scoop`，没有就装官方版；补 `git`；注册 `toolbox` / `toolbox-beta` 两个 bucket。日志在 `%TEMP%\kjllin-toolbox-scoop.log`，任何一步失败都 `exit 0`，不影响安装成功。

`startup_page` 为 `discover` 时，`MainWindowViewModel.LoadDefaultPage` 的兜底分支固定回 `Discover` —— 打开就是源里的应用列表。

## 源只显示自有

`KnownSources` 只管默认值，真正的源列表来自 `scoop bucket list`（本机所有 bucket），搜索来自 `scoop-search`（全库搜）。所以 `patch_sources.py` 在代码层加了闸：

| 改动 | 位置 |
|---|---|
| 新增设置 `ScoopShowAllSources`（默认关） | `SettingsEngine_Names.cs` |
| 开关「Scoop sources」 | `SettingsPages/General.axaml` |
| 源列表只留自有 bucket | `ScoopSourceHelper.ParseSources` |
| 搜索/首页只留自有源 | `Scoop.ParseSearchOutput` |
| 更新只报自有源 | `Scoop.ParseAvailableUpdates` |
| 打开开关后自有源排前、同应用保留自有源那条 | 上述两处 |

注意：这个开关只约束 Scoop 的 bucket。其它包管理器（winget、chocolatey、 pip…）若想不显示，需在「包管理器」页里停用。

## 构建

推送到 `polaris` 且改动命中 `branding/**`、`src/**`、`UniGetUI.iss`、`global.json` 时会自动构建；机器人带 `[skip ci]` 的品牌提交不会触发，需再推一次改动才会真正编译。
