# 地形纯规则回归

- 覆盖 `TerrainNoiseKernel` 的种子确定性、噪声范围、零八度拒绝和高度强化；覆盖 `BiomeResolver` 区间边界、重叠顺序、后续候选和未匹配标记。
- 同脚本通过正式 `Module.ApplyResourceConfiguration` 和 `ModuleJsonConfigurator` 配置独立工具并执行生产校验，核对 JSON 参数确实写入等级/效率字段并拒绝只读计算属性，再经 `TileDefinitionFactory` 构建采挖定义；调用 `GroundTileHarvestSystem.ResolveRequiredUses/ReadProgress` 检查品质/效率、最低次数、进度钳制及无效工具/间隔/层数配置。`GroundTileLayerSystem.Capture/Restore` 直接检查多层深度与草状态快照。
- 样本只调用小规模纯函数，不生成真实地图。未覆盖完整地表/洞穴生成、河流连接、自然资源装配、区块加载和视觉接缝。
- 采挖只创建停用工具、临时 Tile 资源和独立 2×2 地形，不登记正式目录或保存世界。**尚未执行真实玩家采挖**；连续挥动的冷却只验证配置边界，未验证实际限频、挥动、掉落事务或挖尽后替换地表。
- 无视觉现场，因此没有截图命令。本轮规则案例已通过，见 [统一执行记录](../../执行记录与失败待办_2026-10-14.md)；未执行真实玩家采挖，不能据此宣称完整生成或采挖流程通过。
