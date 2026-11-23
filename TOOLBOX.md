# KJLLin Toolbox

基于 [UniGetUI](https://github.com/Devolutions/UniGetUI)（MIT）改造的 Windows 应用分发客户端：界面与包管理能力继承上游，引擎只保留 winget，精选清单与自有应用全部由云端配置下发。

## 改造内容

| 位置 | 改动 |
|---|---|
| `src/SharedAssemblyInfo.cs` | 程序集 Product / Title / Description / DefaultAlias → `KJLLin Toolbox` |
| `src/UniGetUI.Avalonia/app.manifest` | 应用清单身份 → `KJLLin.Toolbox.Desktop` |
| `UniGetUI.iss` | 安装器名称 / 发行方 / 网址，以及独立 `AppId`（避免与上游安装包互相覆盖） |
| `src/UniGetUI.PackageEngine.PackageEngine/PEInterface.cs` | 引擎列表只保留 `WinGet`，Scoop 实例与工程引用已移除 |
| `src/UniGetUI.PackageEngine.Managers.WinGet/Helpers/CuratedWinGet.cs` | 浏览只列精选项，搜索范围由 `searchScope` 决定，清单与拉黑可由云端覆盖 |
| `src/UniGetUI.PackageEngine.PackageEngine/Toolbox/ToolboxCloudConfig.cs` | 云端配置拉取（镜像顺序、刷新间隔、横幅、已读记录均可由云端控制） |
| `src/UniGetUI.Avalonia/Infrastructure/Toolbox/WinGetLocalManifest.cs` | 自有应用走 `winget install --manifest`，首次按需一次提权启用策略 |
| `src/UniGetUI.Avalonia/Infrastructure/Toolbox/StartMenuShortcuts.cs` | 安装后自建开始菜单 `.lnk`（portable 包本身不带快捷方式） |
| `src/UniGetUI.Avalonia/Infrastructure/Toolbox/ToolboxOwnedApps.cs` | 自有应用版本比对、自动更新与状态记录 |
| `InstallerExtras/EnsureWinGet.ps1` | 设备缺少 App Installer / winget 时从 Gitee 镜像补装 |
| `src/Languages/lang_*.json` | 60 个语言包的译文替换（`Translate()` 查找用的英文 key 保持不动） |
| `media/icon.png`、`src/SharedAssets/icon.ico` | 品牌图标（放进 `branding/assets/` 即生效） |

## 云端配置

客户端启动后按镜像顺序拉 `toolbox-config.json`（默认 Gitee raw → ghfast → GitHub raw），成功则缓存到 `%LOCALAPPDATA%\KJLLin Toolbox\cloud-config.json`，之后按 `refreshHours`（默认 6 小时）刷新，拉不到就用缓存，缓存也没有才用内置默认值。

| 字段 | 作用 |
|---|---|
| `curated` | 精选 winget 包 ID 列表，覆盖客户端内置的 14 项，数组顺序即浏览顺序 |
| `blockedIds` | 从浏览和搜索中拉黑的 ID，优先级高于 `curated` |
| `searchScope` | `curated` 时搜索只命中精选，`all` 时搜索覆盖整个源（浏览始终只列精选） |
| `updatesScope` | `store`（默认）时更新页只列精选与自有应用，`all` 时显示全部 winget 更新 |
| `installedScope` | `all`（默认）保留机器上所有包（否则卸载入口消失），`store` 只列精选与自有应用 |
| `mirrors` | 下载前缀顺序：既用于拉配置本身，也用于把 GitHub 的安装包地址改写成加速前缀 |
| `refreshHours` | 配置刷新间隔，1–168 小时 |
| `shortcuts` | 自有应用装完是否建开始菜单快捷方式（总开关） |
| `desktopShortcuts` | 是否额外放一份桌面快捷方式 |
| `notice` | 主窗口横幅：`id`（关掉后同一 id 不再弹）、`level`（info/warning/error/success）、`title`、`message`、`url`、`urlLabel`、`closable` |
| `announcement` | 旧版纯文本公告，`notice` 存在时以 `notice` 为准 |
| `ownedApps[]` | 自有应用：`key` / `identifier` / `version` / `installerUrl` / `sha256` / `autoInstall` / `autoUpdate` |

发一个版本只要改 `ownedApps[].version` 与 `installerUrl`，不需要重新出包。`notice` 的 `id` 留空表示每次启动都弹；填了 id 则用户关掉后本机不再显示。

自有应用在 winget 社区源里查不到，因此它们由客户端从云端配置注入：浏览时排在最前，搜索时按 `key` / 显示名 / 标识符 / 别名匹配。安装走 `winget install --manifest <本地目录>`（manifest 在 `%LOCALAPPDATA%\KJLLin Toolbox\manifests\` 现写），`LocalManifestFiles` 只在首次安装时向用户要一次提权，成功后写 `localmanifest-policy.ok`，之后不再弹 UAC。

更新页默认与搜索一样只认精选和自有应用（`updatesScope` 改 `all` 可放开）：`winget upgrade` 的结果会先被过滤到这两个集合，自有应用的待更新项由云端版本与本地记录的版本比对后补上。装在其他来源的软件不会出现在更新页（仍可在“已安装”页查看和卸载）。

## 自有源仓库

| 源 | 仓库 | 用途 |
|---|---|---|
| `toolbox` | `KJLLin/KJLLin-Toolbox-Bucket` | 云端配置、精选清单、App Installer 引导包 |
| `toolbox-beta` | `KJLLin/KJLLin-Toolbox-Bucket-Beta` | 内测清单 |

两个仓库都镜像到 Gitee（`kjllin/kjllin-toolbox-bucket`、`kjllin-toolbox-bucket-beta`），客户端默认走 Gitee raw，GitHub 只作兜底。

App Installer 引导包在 Gitee Release `winget-bootstrap` 里：单架构 `AppInstaller_x64.msix`（62.5 MB）加三个 x64 依赖包，匿名可下、sha256 已固定在校验表里。

## 一键改造

```bash
python branding/apply_brand.py --check    # 预览
python branding/apply_brand.py --apply    # 写入
python branding/apply_brand.py --verify   # 查残留
```

参数全在 `branding/toolbox.json`：改名、换源只改这一个文件。脚本幂等，重复跑不会叠加。

向 `polaris` 分支推送 `branding/**` 时，`.github/workflows/polaris-brand.yml` 会先在 CI 里把语言包还原成上游版本再执行改造并提交，所以 60 个语言包不需要本地处理。

## 自动构建（不需要本地环境）

`.github/workflows/kjllin-toolbox-build.yml` 在 `windows-latest` 上跑上游自带的 `scripts/build.ps1`：还原 → 发布 → Inno Setup 打包 → 校验和 → 上传产物 → 发布滚动 Release。

下载地址（每次 build 都会覆盖）：

- 安装包：`https://github.com/KJLLin/KJLLin-Toolbox/releases/download/nightly/KJLLin.Toolbox.Installer.x64.exe`
- 便携 zip：`https://github.com/KJLLin/KJLLin-Toolbox/releases/download/nightly/KJLLin.Toolbox.x64.zip`
- 校验和：`https://github.com/KJLLin/KJLLin-Toolbox/releases/download/nightly/checksums.x64.txt`

触发方式：推送到 `polaris` 分支的 `src/**`、`branding/**`、`UniGetUI.iss`、`global.json` 或该 workflow 文件；也可以在 Actions 页面手动 `workflow_dispatch`。

## 本地构建

```powershell
# 需要 .NET SDK 10 + VS 生成工具
pwsh ./scripts/build.ps1 -Configuration Release -Platform x64

# 或只要编译
dotnet build src/UniGetUI.Avalonia/UniGetUI.Avalonia.csproj -c Release
```

## 命令行

```
KJLLin.Toolbox.exe --toolbox-own-list                # 列出云端声明的自有应用
KJLLin.Toolbox.exe --toolbox-own-install hpcl       # 立刻装/更新一个自有应用
```

安装走本地 manifest，第一次会弹一次 UAC 启用 `LocalManifestFiles`；用户拒绝时该应用保持未安装，客户端其余功能不受影响。

## 还需要你本机做的

1. **图标**：`branding/assets/` 目前为空，客户端仍是上游图标。放 `icon.ico` / `icon.png` 后推一次即可替换。
2. **签名**：CI 构建会临时去掉 `SignTool=azsign`。要消掉 SmartScreen 警告，得买或自签一张证书，再把 `scripts/sign.ps1` 需要的签名配置补上。
3. **发布二进制**：`toolbox-config.json` 里 `ownedApps[].installerUrl` 指向 `kjllin/hpcl-launcher` 的 Release 资产，版本更新时这两个字段要一起改。

## 许可

上游 MIT，保留 `LICENSE` 与原版权声明即可分发。`AssemblyCompany` / `AssemblyCopyright` 保留 `Devolutions Inc.` 是刻意的署名，不要清掉。
