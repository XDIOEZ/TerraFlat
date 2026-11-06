-- 期望由 Lua 真值表独立定义，夹具只调用正式图并返回采样。
local tables = {
    {kind = "and", values = {false, false, false, true}},
    {kind = "or", values = {false, true, true, true}},
    {kind = "not", values = {true, false, true, false}},
    {kind = "xor", values = {false, true, true, false}}
}
for _, gate in ipairs(tables) do
    test.system("circuit_signals", "gate_table", {kind = gate.kind}, function(data)
        assert(#data.rows == 4, "逻辑门输入组合不足")
        for index, row in ipairs(data.rows) do
            assert(row.output == gate.values[index], gate.kind .. "真值表错误：" .. index)
            assert(row.first == (gate.kind == "not"), "门读取了本轮尚未提交的输入")
        end
    end, gate.kind .. "四组输入与固定步传播")
end
test.system("circuit_signals", "topology", {}, function(data)
    assert(data["or"][1] and data["or"][2] and data["or"][3] and not data["or"][4], "多源 OR 合并错误")
    assert(data.split and not data.removed, "断线或拆除后保留旧高电平")
    assert(data.feedback and not data.differentChannelsFeedback, "无线回环或频道隔离错误")
    assert(data.blockedNotification and data.unblockedNotification, "有效零来源或反馈资格改变没有设备通知")
    assert(data.wrapped, "环绕接缝信号线未连通")
end, "信号OR、断线、拆除、环绕与无线反馈")
test.system("circuit_signals", "relay", {}, function(data)
    for index, row in ipairs(data.sequence) do
        local expected = index == 3
        assert(row.connected == expected, "串联继电器触点拓扑错误")
        assert(math.abs(row.supplied - (expected and 60 or 0)) < 0.001, "继电器未控制真实电网功率")
    end
    assert(not data.rotatedConnected, "旋转触点仍错误接通原方向")
end, "继电器串联开断与真实功率")

test.system("circuit_signals", "oscillator", {}, function(data)
    local expected = {false, true, false, false, true}
    for index, value in ipairs(expected) do
        assert(data.sequence[index] == value, "振荡器变步长时序或线路输出错误：" .. index)
    end
    assert(data.pausedHigh and data.invalidRejected, "暂停或无效配置改变了相位")
    assert(not data.disabled and not data.restartedLow and data.restartedHigh, "关机或重启没有完整低半周期")
    assert(not data.changedLow and data.restoredEnabled and not data.restoredLow,
        "改频或读档没有从低电平重新起振")
    assert(not data.beforeRestoredBoundary and data.afterRestoredBoundary, "存档错误保留了运行相位")
    assert(math.abs(data.quantizedFrequency - 10 / 3) < 0.0001, "可用频率档位读数错误")
end, "振荡器模拟时钟、开关、量化频率与配置恢复")
