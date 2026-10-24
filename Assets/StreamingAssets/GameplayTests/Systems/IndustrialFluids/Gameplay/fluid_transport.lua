-- 冻结输送与吞吐：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"network_frozen_chain", "气相整网统一供给、液相保持逐段"},
    {"network_shared_throughput", "多出口共享吞吐预算"},
    {"network_blocked_retry", "堵塞保料与原批次重试"},
    {"network_tick_scheduler", "完整机器固定步长调度"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
