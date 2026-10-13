-- 环境抽排与容器：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"environment_atmosphere", "有限大气抽排与逸散"},
    {"environment_outlet_vent", "环境排气与管段显式放气"},
    {"container_liquid_transfer", "工业液体端口原子事务"},
    {"surface_liquid_transactions", "真实地表抽液倾倒与拒收"},
    {"surface_liquid_pump", "真实地表液泵与湿阻"},
    {"surface_liquid_outlet", "管道液体排入地表及满格回滚"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
