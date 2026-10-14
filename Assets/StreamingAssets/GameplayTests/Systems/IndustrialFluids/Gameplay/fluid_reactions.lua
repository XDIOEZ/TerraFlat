-- 过滤电解与发动机：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"device_filter", "真实滤材消耗与残渣记账"},
    {"device_filter_blocked", "过滤产物和残渣满槽保护"},
    {"device_electrolysis_recipes", "全部实际电解配方产物比例"},
    {"device_electrolysis_blocked", "电解断电及混气液碱满腔保护"},
    {"device_electrolysis_residue_port", "独立液碱取液与访问权限"},
    {"device_engine_blocked", "氢能发动机缺气无负载保护"},
    {"device_engine_work", "氢能发动机实际负载与能量结算"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
