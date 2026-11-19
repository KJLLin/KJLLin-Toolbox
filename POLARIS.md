# Polaris Store

一个基于 [UniGetUI](https://github.com/Devolutions/UniGetUI)（MIT）改造的私有应用分发客户端：界面、包管理能力全部继承上游，品牌与默认源换成自己的。

## 改了什么

| 位置 | 改动 |
|---|---|
| `src/SharedAssemblyInfo.cs` | 程序集 Product / Title / Description / DefaultAlias → Polaris Store |
| `src/UniGetUI.Avalonia/app.manifest` | 应用清单身份 → `Polaris.Store.Desktop` |
| `UniGetUI.iss` | 安装器名称、发行方、网址、AppId（换成自己的 GUID，避免与上游冲突） |
| `src/UniGetUI.PackageEngine.Managers.Scoop/Scoop.cs` | 预置 `polaris` / `polaris-beta` 两个私有 bucket 到 KnownSources |
| `src/Languages/lang_*.json` | 60 个语言包里的产品名替换 |
| `media/icon.png`、`src/SharedAssets/icon.ico` | 品牌图标（放入 `branding/assets/` 即生效） |

## 一键完成

```bash
# 预览
python branding/apply_brand.py --check

# 应用（幂等，可重复运行）
python branding/apply_brand.py --apply

# 检查残留
python branding/apply_brand.py --verify
```

品牌参数集中在 `branding/polaris.json`，改名字、换源、改网址都只改这一个文件。

向 `polaris` 分支推送 `branding/**` 时，`.github/workflows/polaris-brand.yml` 会自动在 GitHub 上跑一遍并提交结果，不需要本地环境。

## 构建

```powershell
# Windows（需要 .NET SDK + VS 生成工具）
dotnet build src/UniGetUI.Avalonia/UniGetUI.Avalonia.csproj -c Release

# 安装器（Inno Setup 6）
iscc UniGetUI.iss
```

上游的 NativeAOT 发布脚本仍可用：

```powershell
pwsh ./scripts/publish-nativeaot.ps1 -Platform x64
```

## 还必须在你本机做的三件事

1. **图标**：把你的 `icon.ico` / `icon.png` 放进 `branding/assets/`，重跑脚本即可替换
2. **签名**：买或自签一张代码签名证书，否则用户首次运行会被 SmartScreen 拦
3. **发布**：Release 里上传 `Polaris Store Setup.exe`，并同步更新 Scoop/Chocolatey 清单（可选）

## 许可与归属

上游是 MIT：**保留 `LICENSE` 与原版权声明**即可商用或分发。建议在 README 里注明「基于 UniGetUI 修改」。

## 后续可做的功能改造

- 隐藏公共源，只留自有 bucket（`Scoop.KnownSources` 里删掉上游条目）
- 把“稳定/内测”做成界面上的一级切换，而不是两个 bucket
- 给自有源加灰度：客户端上报设备 ID，由服务端 `/check` 决定是否下发
