-- 行为契约：设备事件→原Action→输入仲裁→角色位移/异步动作；不覆盖触屏或物理硬件驱动。
local budget = { minimumTravel = .1, maximumSpeedFactor = 1.15, stoppedSpeed = .05, blockedTravel = .03, chargeError = .001 }
local checks = {
    input = function(d) return d.actionValue > .5 and d.actionDevice == d.device end,
    movement = function(d) return d.distance > budget.minimumTravel and d.speed > budget.stoppedSpeed end,
    speed = function(d) return d.speed <= d.expectedSpeed * budget.maximumSpeedFactor end,
    stop = function(d) return d.stoppedSpeed <= budget.stoppedSpeed end,
    gates = function(d) return d.lockedDistance <= budget.blockedTravel and d.textFocused and d.focusedDistance <= budget.blockedTravel end,
    roll = function(d) return d.rollStarted and d.rollCompleted and d.rollDistance > budget.minimumTravel end,
    charge = function(d) return d.charges == 1 and math.abs(d.charged - d.expectedCost) <= budget.chargeError end
}
local mutations = { drop_input = {input = true, movement = true}, wrong_speed = {speed = true}, double_charge = {charge = true} }
local function validate(data, expectedDevice, expectedFault)
    assert(data.device == expectedDevice and data.fault == expectedFault, "探针执行场景与Lua请求不一致")
    local rejected = mutations[expectedFault] or {}
    for name, condition in pairs(checks) do
        local actual = condition(data)
        if rejected[name] then
            assert(not actual, "业务断言没有检出故障：" .. data.fault .. "/" .. name)
        else
            assert(actual, "非目标业务断言失败：" .. data.fault .. "/" .. name)
        end
    end
end
for _, device in ipairs({"keyboard", "gamepad"}) do
    local expectedDevice = device
    test.runtime("player", "device_movement_roll", {device = device}, function(data)
        validate(data, expectedDevice, "none")
    end, device .. "正式移动、门禁、恢复与键盘翻滚")
end
for _, fault in ipairs({"drop_input", "wrong_speed", "double_charge"}) do
    local expectedFault = fault
    test.runtime("player", "device_movement_roll", {device = "keyboard", fault = fault}, function(data)
        validate(data, "keyboard", expectedFault)
    end, "故障检出：" .. fault)
end
test.runtime("player", "device_movement_roll", {device = "keyboard"}, function(data)
    validate(data, "keyboard", "none")
end, "故障撤销后正常行为恢复")
