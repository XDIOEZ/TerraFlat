-- 热力学与相变：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"core_heat_pressure", "热量压力与传热守恒"},
    {"core_compression", "压缩做功与功率裁剪"},
    {"core_phase_change", "蒸发冷凝与相变边界"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
