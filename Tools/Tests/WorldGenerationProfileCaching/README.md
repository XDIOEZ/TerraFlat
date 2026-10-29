# 世界生成配置缓存离线检查

在项目根目录运行 `python Tools/Tests/WorldGenerationProfileCaching/verify.py`。检查编译正式缓存、Hook、入口和洞穴配对方法，使用轻量替身验证命中、字段原位修改、世界和资源替换、首次冻结、临时 Hook、源世界从缺失到就绪以及热路径分配。产物写入 `Temp/WorldGenerationProfileCachingValidation`。

这不启动 Unity，不验证真实资源装配、区块流送、真实世界切换或联机；这些仍需 Unity/Lua 运行时回归。
