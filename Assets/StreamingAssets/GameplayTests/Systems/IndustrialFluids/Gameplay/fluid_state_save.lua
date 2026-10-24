-- 状态快照与存档：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"core_frozen_snapshot", "起始快照与库存回滚"},
    {"core_observation", "质量事件与缓存失效"},
    {"core_serialization", "JSON 与 MemoryPack 保存隔离"},
    {"tank_snapshot_restore", "机器气液快照恢复"},
    {"gas_network_lifecycle", "气网唯一所有权、非拓扑刷新和组主回收"},
    {"gas_network_restore_order", "气网组主先加载与完整接管"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
