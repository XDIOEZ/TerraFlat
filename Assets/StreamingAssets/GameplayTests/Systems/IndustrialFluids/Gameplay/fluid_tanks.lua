-- 储罐生命周期与充装：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"tank_merge_split", "同材共享罐组合与真实拆分"},
    {"tank_scope_transfer", "跨船整批迁移与重新组合"},
    {"tank_dismantle", "方块罐拆除份额与持久溢流"},
    {"tank_overpressure", "超压固定受损成员"},
    {"tank_rupture_once", "共享破裂预算与防重复释放"},
    {"device_portable_fill", "便携气罐限压及冻结充装"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
