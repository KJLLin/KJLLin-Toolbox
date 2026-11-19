# branding

把 UniGetUI 改造成 Polaris Store 的全部配置与工具都在这里。

- `polaris.json` —— 品牌参数：产品名、发行方、网址、安装器 GUID、私有源列表
- `apply_brand.py` —— 幂等改造脚本，支持 `--check` / `--apply` / `--verify`
- `assets/` —— 放 `icon.ico` 与 `icon.png`，脚本会自动替换到项目里

想改名、换源、换域名，只改 `polaris.json`，别改脚本。
