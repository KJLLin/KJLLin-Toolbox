# branding

把 UniGetUI 改造成 KJLLin Toolbox 的全部配置与工具都在这里。

- `toolbox.json` —— 品牌参数：产品名、发行方、网址、安装器 GUID、`engine` / `startup_page`
- `apply_brand.py` —— 主改造脚本，支持 `--check` / `--apply` / `--verify`
- `patch_installer.py` —— 安装器装配：部署并调度 `EnsureWinGet.ps1`，同时清掉历史 Scoop 引导行
- `patch_sources.py` —— 源可见范围（Scoop 阶段留下的裁剪逻辑，单引擎后仅作兜底）
- `patch_curation.py` —— 默认目录裁剪（同上）
- `patch_curated_remote.py` —— 精选名单远端化（同上）
- `patch_managers.py` —— 引擎列表固定为 `WinGet`，删掉 Scoop 实例与命名空间引用
- `remove_legacy_managers.py` —— 把 Npm / Pip / .NET Tool / PowerShell Gallery / Cargo / Bun / vcpkg 的代码整个删掉
- `patch_uninstall_hooks.py` —— 系统卸载请求先经过 Toolbox 确认（历史 Scoop 镜像项仍可正确处理）
- `patch_developer_mode.py` —— 开发人员选项开关，关闭时隐藏高级设置入口
- `patch_scoop_bundle.py` —— 清理器：删除残留的 `scoop-install.ps1` / `EnsureScoop.ps1` 行
- `assets/` —— 放 `icon.ico` 与 `icon.png`，脚本会自动替换到项目里

想改名、换源、换域名，只改 `toolbox.json`，别改脚本。

## 引擎

只保留 winget。`PEInterface.cs` 的候选列表是 `[WinGet]`，Scoop 实例、Scoop 工程引用、Scoop 测试与 `ScoopUninstallEntries`（原来每 60 秒镜像「程序和功能」的那套）全部移除；winget 装的应用自带原生卸载项，不需要再镜像。

## 精选目录

客户端只展示精选清单，不展示整个 winget 社区源。浏览时逐个 `winget search --id` 拉取元数据，搜索时按 ID 过滤。清单内置在 `CuratedWinGet.DefaultIds`（14 项），云端 `toolbox-config.json` 的 `curated` 字段可整体覆盖，改配置即生效。

## 云端配置

`https://gitee.com/kjllin/kjllin-toolbox-bucket/raw/main/toolbox-config.json`

`ToolboxCloudConfig` 按 `mirrors` 顺序拼出配置地址（默认 Gitee raw → `ghfast.top` → GitHub raw），成功后写 `%LOCALAPPDATA%\KJLLin Toolbox\cloud-config.json`，之后按 `refreshHours`（默认 6 小时，限 1–168）刷新；全部失败时用缓存，缓存也没有才用内置默认值。

| 字段 | 作用 |
|---|---|
| `curated` | 精选 ID，数组顺序即浏览顺序 |
| `blockedIds` | 拉黑：浏览和搜索都不再出现，优先于 `curated` |
| `searchScope` | `curated` 只搜精选，`all` 搜整个源 |
| `mirrors` | 下载前缀顺序：拉配置 + 把 GitHub 安装包地址改写成加速前缀 |
| `refreshHours` | 刷新间隔 |
| `shortcuts` / `desktopShortcuts` | 开始菜单 / 桌面快捷方式开关 |
| `notice` | 主窗口横幅，`id` 关掉后本机不再弹 |
| `announcement` | 旧版纯文本公告（`notice` 优先） |
| `ownedApps[]` | 自有应用清单 |

## 自有应用

`ownedApps[]` 里每项包含 `key`、`identifier`、`version`、`installerUrl`、`sha256`、`commandAlias`、`shortcutName`、`autoInstall`、`autoUpdate`。

- `WinGetLocalManifest` 把这一项写成三件套 YAML（version / installer / locale），跑 `winget install --manifest <dir>`；第一次被 `LocalManifestFiles` 策略拦住时，弹一次 UAC 执行 `winget settings --enable LocalManifestFiles` 再重试，用户拒绝就放弃这个应用，不影响其他功能。
- `StartMenuShortcuts` 安装后在 `%APPDATA%\Microsoft\Windows\Start Menu\Programs` 建 `.lnk`（用 WScript.Shell，避免 AOT 下的 COM interop 问题）。
- `ToolboxOwnedApps` 启动 25 秒后在后台比对版本：缺失且 `autoInstall` 为真就装，已装且云端版本更高、`autoUpdate` 为真就重装，末尾补快捷方式。状态记在 `%LOCALAPPDATA%\KJLLin Toolbox\owned-apps.json`。

命令行：`--toolbox-own-list` 打印云端自有应用，`--toolbox-own-install <key>` 立刻装/更新。

## 国内镜像

App Installer 引导包放在 Gitee Release `winget-bootstrap`：`AppInstaller_x64.msix`（62.5 MB，从官方 217 MB bundle 里拆出单架构）加 `WindowsAppRuntime.1.8` / `VCLibs.140.00` / `VCLibs.140.00.UWPDesktop` 三个 x64 依赖。`EnsureWinGet.ps1` 只在 `winget --version` 失败时才下载，每个文件 sha256 固定，Gitee 失败依次退到 `ghfast.top`、官方 GitHub 大包解压。

实测：Gitee 直链约 2 MB/s，62 MB 约 30 秒；本机已有 winget 时下载 0 字节。

## 开发者选项

设置 → 通用里新增「开发人员选项」开关（`DeveloperMode`，默认关闭）。关闭时隐藏设置首页上的「管理员权限」「包管理器」「扩展」「网络」「实验性」五个入口。

## 构建

推送到 `polaris` 且改动命中 `branding/**`、`src/**`、`UniGetUI.iss`、`global.json` 时会自动构建；机器人带 `[skip ci]` 的品牌提交不触发编译。

滚动 Release `nightly` 的发布步骤已改成幂等：先 `gh release delete --cleanup-tag`，删不掉就改为 `gh release upload --clobber` 往已存在的 release 里覆盖，避免并发构建撞 tag。
