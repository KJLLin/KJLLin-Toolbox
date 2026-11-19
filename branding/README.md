`only_own_sources: true` 时，`Scoop.cs` 的 `KnownSources` 被整体重写为配置里的源，`DefaultSource` 指向第一个源——公共 bucket 在客户端里不再出现。

改品牌只需改 `toolbox.json`，然后推 `branding/**`，CI 会自动执行 `apply_brand.py` 并提交结果。
