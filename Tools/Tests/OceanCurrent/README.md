# 海岸分带洋流离线验证

运行 `python Tools/Tests/OceanCurrent/verify.py`，在 `Temp/OceanCurrentValidation/` 隔离编译当前生产 WorldModel，以及正式 `WaterEnvironmentRules`、`WorldItemWaterRules`。不会刷新、编译或切换正在运行的 Unity。

覆盖近岸朝岸/外侧朝海、三格弱流带、强度上限、空间倍率、直岸/斜岸/岛屿、等距双岸/双岛、环绕与整块/单格一致、远洋反向衔接、极值配置、河口与零流连续性，以及 simple/legacy 正式生成字段一致。正式运行时的枚举直接从源码提取；CLR 类型壳只补未调用的物品与取水入口，水流算法编译生产源码。Unity 原生 Profiler 初始化与 Burst 原生调用在临时副本中关闭，不替换地形或水流计算。

`python Tools/Tests/OceanCurrent/verify.py --benchmark` 从正式 Surface Profile 读取数字配置和湿度半径 12，以真实地形噪声比较搜索 20/32/48/64 格、空间倍率 1/4 的 16×16 整块生成耗时。为定位岸线额外采样成本，关闭河湖、岩浆、草与远程降水；每组预热一次后记录三次中位数，并确认实际生成了海水。这个控制基准不能代表完整世界的帧耗时。

环境 Lua 的 `ocean_shore_current` 与 `ocean_shore_geometry` 另覆盖正式 `ChunkMgr`、角色推速、掉落物推速、平台/湖泊/干格拒绝和河口优先。离线通过不能代替 Unity Lua、真实玩家游泳、船舶、联机与水面视觉验收；用户保留 PlayMode 时不运行这些场景。
