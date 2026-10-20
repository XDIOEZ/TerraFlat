-- 泵与压缩机：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

-- 空输入和满缓冲会主动停止用电需求，不能据此误报为没有电。
for _, case in ipairs({
    {scenario = "compressor_empty", status = "入口没有气体", demand = 0},
    {scenario = "compressor_unpowered", status = "没有电", demand = 1},
    {scenario = "liquid_buffer_full", status = "液体缓冲已满，等待输出", demand = 0},
    {scenario = "reaction_empty", status = "缺少可用反应原料", demand = 0},
    {scenario = "reaction_output_full", status = "产物空间不足，等待输出", demand = 0},
    {scenario = "reaction_no_filter", status = "缺少可用滤材", demand = 1},
    {scenario = "reaction_unpowered", status = "没有电", demand = 1}
}) do
    test.runtime("industrial_fluids", "device_status_feedback", {scenario = case.scenario}, function(data)
        assert(data.status == case.status, "机器拒绝原因错误：" .. case.scenario .. " / " .. data.status)
        assert(data.demand == case.demand and data.suppliedWatts == 0, "没有形成真实的零供电/主动停用需求现场")
        assert(data.inputUnchanged, "只报告拒绝原因却改变了原料")
    end, "工业设备拒绝原因：" .. case.scenario)
end

test.runtime("industrial_fluids", "liquid_pump_flow", {}, function(data)
    assert(data.noPowerBufferEmpty and data.afterNoPower == data.initialMoles, "断电液泵旁路扣了工业原料")
    assert(data.awaitingBufferEmpty and data.afterAwaitingNetwork == data.initialMoles, "设备抽液绕过正式管网")
    assert(data.pumpGas + data.pumpLiquid > 0 and data.sourceGas + data.sourceLiquid < data.initialMoles,
        "正式冻结输送没有向液泵输入原料")
    assert(math.abs(data.sourceGas + data.sourceLiquid + data.pumpGas + data.pumpLiquid + data.targetGas + data.targetLiquid - data.initialMoles) < .00000001,
        "工业液泵两相水摩尔量不守恒")
    assert(data.targetGas + data.targetLiquid == 0, "本轮新料立即穿透液泵出口")
    assert(data.pumpBeforeRetry == data.pumpAfterRetry, "同轮重复设备操作旁路扣量")
    assert(data.lastFlowPhase == "liquid", "实际工业入口输送相态错误")
end, "工业液泵真实冻结输送、两相守恒和重复操作")

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
