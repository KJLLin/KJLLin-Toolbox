# branding

把 UniGetUI 改造成 KJLLin Toolbox 的全部配置与工具都在这里。

- `toolbox.json` —— 品牌参数：产品名、发行方、网址、安装器 GUID、自有源列表、是否只保留自有源，以及 `bootstrap_scoop` / `startup_page` 两个开关
- `apply_brand.py` —— 幂等改造脚本，支持 `--check` / `--apply` / `--verify`
- `patch_installer.py` —— 把安装器接到 Scoop、并把启动页固定成首页，支持 `--check` / `--apply`
- `assets/` —— 放 `icon.ico` 与 `icon.png`，脚本会自动替换到项目里

想改名、换源、换域名，只改 `toolbox.json`，别改脚本。

`only_own_sources` 为 `true` 时，`Scoop.cs` 的 `KnownSources` 会被整体重写为配置里的源，并把 `DefaultSource` 指向第一个源——公共 bucket 在客户端里就彻底不出现了。

## 装完就能用

`bootstrap_scoop` 为 `true` 时，`patch_installer.py` 会往 `UniGetUI.iss` 里插两行：

1. `[Files]` —— 把 `InstallerExtras/EnsureScoop.ps1` 拷到 `{tmp}`
2. `[Run]` —— 安装收尾时**以原用户身份、隐藏窗口**跑这个脚本：检测 `scoop`，没有就装上（官方 `get.scoop.sh`）；再补 `git`（`scoop bucket add` 需要）；最后把 `toolbox` / `toolbox-beta` 两个 bucket 注册上。全程写日志到 `%TEMP%\kjllin-toolbox-scoop.log`，任何一步失败都只记日志、`exit 0`，不影响安装成功。

`startup_page` 为 `discover` 时，`MainWindowViewModel.LoadDefaultPage` 的兜底分支会被改成固定回 `Discover`——打开客户端首页就是源里的应用列表，不再因为是可更新就跳去 Updates 页。

跳过引导：安装器传 `/NoScoop` 即可；脚本单独重跑：

```powershell
powershell -ExecutionPolicy Bypass -File InstallerExtras\EnsureScoop.ps1 -BucketUrls 'toolbox=https://github.com/KJLLin/KJLLin-Toolbox'
```

## 构建

推送到 `polaris` 且改动命中 `branding/**`、`src/**`、`UniGetUI.iss`、`global.json` 时会自动构建；机器人带 `[skip ci]` 的品牌提交不会触发，需要再推一次改动才能真正编译。
