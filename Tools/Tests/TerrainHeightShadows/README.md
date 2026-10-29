# 高低阴影采样复用回归

运行 `python Tools/Tests/TerrainHeightShadows/verify.py`，在 `Temp/TerrainHeightShadowsValidation` 编译正式单轮缓存，检查 32×32 格九邻域查询的 1,156 次底层采样上界、未知邻区复用、下轮配置变化、刷新中失效、环绕 halo 与尺寸变更。

Lua `rendering/terrain_shadow_surface_cache` 另用同一正式掩码方法比较缓存前后随机地形、支撑、液体与缺失邻区边界的结果；Unity 正在 PlayMode 时只添加断言，不运行。离线计数不代表实际阴影耗时或帧率。
