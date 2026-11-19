# KJLLin Toolbox

基于 [UniGetUI](https://github.com/Devolutions/UniGetUI)（MIT）改造的 Windows 应用分发客户端：界面与包管理能力继承上游，只保留自己的应用源。

## 改造内容

| 位置 | 改动 |
|---|---|
| `src/SharedAssemblyInfo.cs` | 程序集 Product / Title / Description / DefaultAlias → `KJLLin Toolbox` |
| `src/UniGetUI.Avalonia/app.manifest` | 应用清单身份 → `KJLLin.Toolbox.Desktop` |
| `UniGetUI.iss` | 安装器名称 / 发行方 / 网址，以及独立 `AppId`（避免与上游安装包互相覆盖） |
| `src/UniGetUI.PackageEngine.Managers.Scoop/Scoop.cs` | **源列表裁剪成只剩 `toolbox` 与 `toolbox-beta`，默认源也切到自有 bucket** |
| `src/Languages/lang_*.json` | 60 个语言包的译文替换（`Translate()` 查找用的英文 key 保持不动） |
| `media/icon.png`、`src/SharedAssets/icon.ico` | 品牌图标（放进 `branding/assets/` 即生效） |

## 自有源仓库

| 源 | 仓库 | 用途 |
|---|---|---|
| `toolbox` | `KJLLin/KJLLin-Toolbox` | 稳定版清单 |
| `toolbox-beta` | `KJLLin/KJLLin-Toolbox-Beta` | 内测清单 |

两个仓库都是标准 Scoop bucket 结构：清单放在 `bucket/*.json`，客户端按 `checkver` / `autoupdate` 自动跟版本。

## 一键改造

```bash
python branding/apply_brand.py --check    # 预览
python branding/apply_brand.py --apply    # 写入
python branding/apply_brand.py --verify   # 查残留
```

参数全在 `branding/toolbox.json`：改名、换源、换域名只改这一个文件。脚本幂等，重复跑不会叠加。

向 `polaris` 分支推送 `branding/**` 时，`.github/workflows/polaris-brand.yml` 会先在 CI 里把语言包还原成上游版本再执行改造并提交，所以 60 个语言包、850 行的 `Scoop.cs` 都不需要本地处理。

## 自动构建（不需要本地环境）

`.github/workflows/kjllin-toolbox-build.yml` 在 `windows-latest` 上跑上游自带的 `scripts/build.ps1`：还原 → 发布 → Inno Setup 打包 → 校验和 → 上传产物 → 发布滚动 Release。

下载地址（每次 build 都会覆盖）：

- 安装包：`https://github.com/KJLLin/UniGetUI/releases/download/nightly/KJLLin.Toolbox.Installer.x64.exe`
- 便携 zip：`https://github.com/KJLLin/UniGetUI/releases/download/nightly/KJLLin.Toolbox.x64.zip`
- 校验和：`https://github.com/KJLLin/UniGetUI/releases/download/nightly/checksums.x64.txt`

触发方式：推送到 `polaris` 分支的 `src/**`、`branding/**`、`UniGetUI.iss`、`global.json` 或该 workflow 文件；也可以在 Actions 页面手动 `workflow_dispatch`。

## 本地构建

```powershell
# 需要 .NET SDK 10 + VS 生成工具
pwsh ./scripts/build.ps1 -Configuration Release -Platform x64

# 或只要编译
dotnet build src/UniGetUI.Avalonia/UniGetUI.Avalonia.csproj -c Release
```

## 还需要你本机做的

1. **图标**：`branding/assets/` 目前为空，客户端仍是上游图标。放 `icon.ico` / `icon.png` 后推一次即可替换。
2. **签名**：CI 构建会临时去掉 `SignTool=azsign`。要消掉 SmartScreen 警告，得买或自签一张证书，再把 `scripts/sign.ps1` 需要的签名配置补上。
3. **发布二进制**：`toolbox` / `toolbox-beta` 里的清单指向 `KJLLin/HPCL`、`KJLLin/ClipFlow` 的 Release 资产，这些 Release 需要真实上传对应 zip，否则安装会 404。

## 许可

上游 MIT，保留 `LICENSE` 与原版权声明即可分发。`AssemblyCompany` / `AssemblyCopyright` 保留 `Devolutions Inc.` 是刻意的署名，不要清掉。
