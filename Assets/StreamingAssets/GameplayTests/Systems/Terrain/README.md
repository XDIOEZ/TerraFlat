# 地形纯规则回归

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

- 覆盖 `TerrainNoiseKernel` 的种子确定性、噪声范围、零八度拒绝和高度强化；覆盖 `BiomeResolver` 区间边界、重叠顺序、后续候选和未匹配标记。
- 同脚本通过正式 `Module.ApplyResourceConfiguration` 和 `ModuleJsonConfigurator` 配置独立工具并执行生产校验，核对 JSON 参数确实写入等级/效率字段并拒绝只读计算属性，再经 `TileDefinitionFactory` 构建采挖定义；调用 `GroundTileHarvestSystem.ResolveRequiredUses/ReadProgress` 检查品质/效率、最低次数、进度钳制及无效工具/间隔/层数配置。`GroundTileLayerSystem.Capture/Restore` 直接检查多层深度与草状态快照。
- 样本只调用小规模纯函数，不生成真实地图。未覆盖完整地表/洞穴生成、河流连接、自然资源装配、区块加载和视觉接缝。
- 采挖只创建停用工具、临时 Tile 资源和独立 2×2 地形，不登记正式目录或保存世界。**尚未执行真实玩家采挖**；连续挥动的冷却只验证配置边界，未验证实际限频、挥动、掉落事务或挖尽后替换地表。
- 无视觉现场的规则测试不截图；PlayMode 测试由宿主自动截图。本轮规则案例已通过，见 [统一执行记录](../../执行记录与失败待办_2026-10-14.md)；未执行真实玩家采挖，不能据此宣称完整生成或采挖流程通过。

`gameplay-suite.json` / `gameplay.lua` 与自然生态共用一套现场：选取真实已加载的干燥空地，经 `GroundTileLayerSystem.TryApplyPlayerCover` 连续覆盖土/草两层。Lua 核对实际 Ground、隐藏下层的先后顺序、独立 Liquid 未改，以及正式 `SaveDataMgr` 区块差量中保存了两层覆盖栈。

同一地格经 `NaturalEntityEcsService` 正式注册自有松树、绑定实际区块表现，Lua 检查实体身份快照、真实导航占地从可走变阻挡，快照移除后恢复可走，随后用保存的同一身份恢复正式资源实体。PlayMode 宿主定时截图。

结束通过正式差量入口恢复该格原地表、草、进度、覆盖栈并删除自有资源实体；自有区块持有模拟租约，失败/中断也由 Lua 级收尾执行。 [Ecology](../Ecology/README.md) 共用本清单，不复制案例。

现场缺口：实际工具挥动采挖、冷却限频、掉落事务、挖尽替换、完整地表/洞穴生成、河道/地形接缝、资源自动分布与年度补位。这份覆盖/资源生命周期不能替代这些流程。

新增现场已在本轮隔离测试中执行，玩法验收由用户进行。
