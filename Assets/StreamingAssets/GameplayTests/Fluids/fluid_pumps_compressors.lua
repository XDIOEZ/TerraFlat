-- 泵与压缩机：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"device_gas_pump", "机械气泵抽气及阻塞边界"},
    {"device_liquid_pump", "工业入口液泵冻结输送"},
    {"device_compressor_mechanical", "机械压缩机供能与堵塞"},
    {"device_compressor_electric", "电动压缩机供能与堵塞"},
    {"device_compressor_controls", "压缩目标压力合法性"},
    {"device_electrical_compressor", "电池电线压缩机完整电网调度"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
