-- 同格从上到下按显示层级等分，查询顺序、重复视觉和边界不能改变结果。
for _, case in ipairs({
    {layers = {-1, 0}, height = .9, selected = 1},
    {layers = {-1, 0}, height = .51, selected = 1},
    {layers = {-1, 0}, height = .5, selected = 2},
    {layers = {-1, 0}, height = .1, selected = 2},
    {layers = {-2, -1, 0}, height = .9, selected = 1},
    {layers = {-2, -1, 0}, height = .5, selected = 2},
    {layers = {-2, -1, 0}, height = .1, selected = 3},
    {layers = {-2, -1, 0}, height = 0, selected = 3},
    {layers = {0}, height = .95, selected = 1},
    {layers = {0}, height = .05, selected = 1}
}) do
    for _, reverse in ipairs({false, true}) do
        test.system("player", "grid_interaction_bands", {
            layers = case.layers, height = case.height, reverse = reverse, duplicate = true
        }, function(data)
            assert(data.count == 1 and data.selected == case.selected, "同格交互没有按光标高度等分或查询顺序影响了层级")
            assert(data.ordinaryPreserved and data.otherGridPreserved, "同格分区误删普通目标或另一套网格")
            assert(data.reusedCount == 1 and data.reusedSelected == 1, "复用查询仍残留上一格的目标或份数")
            assert(data.pipeLayer < data.wireLayer and data.wireLayer < data.deviceLayer,
                "管道、电线、设备的交互顺序与显示层级不一致")
            assert(data.cellTopHit and data.cellBottomHit and not data.neighbourHit,
                "同格交互没有覆盖整格或泄漏到邻格")
        end, "同格交互等分：" .. #case.layers .. "层，高度" .. case.height .. (reverse and "，反序" or ""))
    end
end

test.system("player", "interaction_completion", {}, function(data)
    assert(not data.wrongTargetAccepted and data.originalPreserved, "不匹配的完成请求清掉当前交互")
    assert(data.accepted and data.currentCleared and data.onlyCompletedRemoved, "完成交互没有撤掉自身目标，或误删其他候选")
    assert(not data.repeatedAccepted and data.completedCancelCount == 0, "重复完成或后续取消再次回调已结束目标")
    assert(not data.staleAccepted and data.nextPreserved and data.nextCancelCount == 1, "旧完成请求污染新交互，或新目标取消失效")
end, "交互完成清理、重复调用与迟到取消隔离")

-- 玩家与动物复用正式地表重力响应，实际碰撞与场景切换另由现场验收。
for _, player in ipairs({true, false}) do
    test.system("player", "surface_gravity_movement", {player = player}, function(data)
        local function near(actual, expected, label)
            assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label)
        end
        local normal, low, high, zero, minimum, ice = data.normal, data.low, data.high, data.zero, data.minimum, data.ice
        near(normal.speedMultiplier, 1, "普通重力移速改变")
        near(normal.responseMultiplier, 1, "普通重力响应改变")
        near(low.speedMultiplier, 2, "月球级重力没有沿用2倍移速上限")
        near(low.responseMultiplier, 1 / 6, "月球级重力响应没有慢约6倍")
        assert(low.start > 0 and low.start < normal.start, "低重力没有更慢起步")
        assert(low.stop > normal.stop and low.reverse > normal.reverse, "低重力刹停或反向没有保留更多惯性")
        assert(low.turnX > normal.turnX and low.turnY < normal.turnY, "低重力转向没有保留原方向惯性")
        assert(high.speedMultiplier < 1, "高重力移速限制失效")
        near(high.start, normal.start, "高重力额外改变了起步响应")
        near(high.stop, normal.stop, "高重力额外改变了刹停响应")
        near(high.turnX, normal.turnX, "高重力额外改变了转向响应")
        assert(zero.responseMultiplier > 0 and zero.responseMultiplier < low.responseMultiplier,
            "地表零重力没有使用有限响应下限")
        near(zero.responseMultiplier, minimum.responseMultiplier, "极低重力响应没有钳制")
        near(zero.speedMultiplier, 2, "极低重力移速突破上限")
        assert(ice.start < low.start and ice.stop > low.stop, "地表打滑倍率与低重力没有组合")
        for _, sample in ipairs({normal, low, high, zero, minimum, ice}) do
            near(sample.settledSpeed, 10 * sample.speedMultiplier, "持续输入没有达到目标移速或超速")
            near(sample.settledStopSpeed, 0, "松手后不能最终停止")
            near(sample.zeroTime, 10, "零时间仍推进惯性")
            near(sample.negativeTime, 10, "负时间仍推进惯性")
        end
    end, player and "低重力玩家移动：提速、起步、刹停、转向及表面组合" or "低重力动物移动：共用惯性、有限下限及最终刹停")
end

-- 手机曲线调用正式规则，保留低速区、正常走速平台和前快后缓的跑步提速。
local function mobile_near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label)
end
for _, runRate in ipairs({1.5, 2}) do
    test.system("player", "mobile_movement_curve", {runRate = runRate}, function(data)
        local samples = data.samples
        assert(#samples == 101, "手机曲线没有覆盖完整摇杆行程")
        mobile_near(data.deadZone, .08, "手机默认死区改变")
        mobile_near(data.atDeadZone, 0, "死区边界仍输出移动")
        mobile_near(samples[21], .5, "推到0.2没有达到最大走速的50%")
        mobile_near(samples[51], 1, "推到0.5没有达到最大走速")
        mobile_near(samples[101], runRate, "推满没有达到配置的最大跑速")
        local previous = 0
        for i, value in ipairs(samples) do
            assert(value >= previous - .000001 and value >= 0 and value <= runRate, "手机曲线倒退或越界")
            if i <= 9 then mobile_near(value, 0, "摇杆死区内仍缓慢移动") end
            if i >= 51 and i <= 71 then mobile_near(value, 1, "0.5到0.7没有保持最大走速") end
            previous = value
        end
        assert(data.lowMidpoint > .25 and data.lowMidpoint < .5, "低速区仍是线性或提前跳到端点")
        assert(data.walkMidpoint > .75 and data.walkMidpoint < 1, "低速到走速没有前快后缓")
        assert(data.runMidpoint > (1 + runRate) / 2 and data.runMidpoint < runRate, "走速到跑速没有前快后缓")
        assert(data.negative == 0 and data.nan == 0 and data.infinity == 0, "异常摇杆力度没有安全归零")
        mobile_near(data.beyondFull, runRate, "越过摇杆外圈后继续加速")
    end, "手机移动曲线：0.2半走速、0.5到0.7走路平台、非线性提速与" .. runRate .. "倍满跑")
end

local function mobile_walking(sample, speed)
    assert(not sample.running and not sample.bodySimulated, "手机步行误入奔跑或启动夹具物理")
    mobile_near(sample.targetSpeed, speed, "手机步行没有达到对应目标速度")
    mobile_near(sample.movingNutrition, 1.6, "走路平台仍使用奔跑营养消耗")
    mobile_near(sample.movingWater, 1, "走路平台仍使用奔跑水分倍率")
end
test.system("player", "mobile_movement_state", {finalAdditive = 3, steps = {
    {x = .2}, {x = .5}, {x = .6}, {x = .7}, {x = .70001}, {x = 1}, {x = .7}, {x = 0},
    {x = .4, y = -.3, mobile = false}
}}, function(data)
    local s = data.samples
    mobile_walking(s[1], 6.5)
    for _, i in ipairs({2, 3, 4, 7}) do mobile_walking(s[i], 13) end
    assert(s[5].running and s[5].targetSpeed >= s[4].targetSpeed and s[5].targetSpeed - s[4].targetSpeed < .001,
        "走跑边界在存在最终加算移速时发生速度跳变")
    assert(s[6].running, "摇杆推满没有进入实际奔跑")
    mobile_near(s[6].targetSpeed, 18, "满跑错误放大最终加算移速或没有达到实际跑速")
    mobile_near(s[6].movingNutrition, 3.2, "手机奔跑没有接入原营养规则")
    mobile_near(s[6].movingWater, .25, "手机奔跑没有接入原水分规则")
    mobile_walking(s[8], 0)
    mobile_near(s[9].x, .4, "退出手机输入后仍改写普通横向输入")
    mobile_near(s[9].y, -.3, "退出手机输入后仍改写普通纵向输入")
    for _, sample in ipairs(s) do assert(not sample.runPreference, "手机自动跑污染了手动奔跑偏好") end
end, "手机走跑接管：分段速度、实际消耗状态、最终加算连续与普通输入隔离")

test.system("player", "mobile_movement_state", {running = true, finalAdditive = 3, steps = {
    {x = .6}, {x = 1}, {x = 0}, {x = 1, mobile = false}, {x = .6},
    {manualRun = false, action = "reset"}
}}, function(data)
    local s = data.samples
    mobile_walking(s[1], 13)
    mobile_walking(s[3], 0)
    assert(s[2].running and s[4].running, "手机满推或退出接管没有恢复原手动奔跑")
    mobile_near(s[4].targetSpeed, 18, "退出接管后的手动奔跑倍率丢失或叠加")
    for i = 1, 5 do assert(s[i].runPreference, "手机走路或松杆提前清掉了原奔跑偏好") end
    assert(not s[6].running and not s[6].runPreference and s[6].speed == 13,
        "明确停止奔跑后的运动重置又恢复了过期奔跑偏好")
end, "手机手动偏好：接管时保留、退出恢复、明确停跑后重置不复活")

test.system("player", "mobile_movement_state", {finalAdditive = 3, steps = {
    {x = .6}, {action = "hold"}, {x = 1}, {action = "release"}, {action = "toggle"},
    {x = 1, mobile = false}, {action = "release"}
}}, function(data)
    local s = data.samples
    mobile_walking(s[1], 13)
    assert(not s[2].running and s[2].runPreference and s[2].speed == 13,
        "手机走路平台按住Shift改变了实际跑步或没有保存手动偏好")
    assert(s[3].running and s[4].running and not s[4].runPreference and s[4].speed == 18,
        "手机外圈奔跑时松开Shift打断了实际跑步或没有清除手动偏好")
    assert(s[5].running and s[5].runPreference, "手机接管时切换键没有依据手动偏好切换")
    assert(s[6].running and s[6].runPreference, "退出手机接管没有恢复最新手动偏好")
    mobile_near(s[6].targetSpeed, 18, "恢复手动奔跑后跑速改变")
    assert(not s[7].running and not s[7].runPreference and s[7].speed == 13,
        "退出手机接管后松开Shift不再立即停止手动奔跑")
end, "手机接管键鼠并用：走路按Shift、跑步松Shift、切换偏好与退出后恢复")

test.system("player", "mobile_movement_state", {finalAdditive = 3, steps = {
    {x = 1}, {x = 1, stamina = 1}, {x = 1, stamina = 100}, {x = .7}, {x = 1},
    {x = 1, stamina = 1}, {x = 0, mobile = false}, {x = 1, stamina = 100}
}}, function(data)
    local s = data.samples
    assert(s[1].running and s[5].running and s[8].running, "正常、回杆解锁或新手机会话不能发动奔跑")
    for _, i in ipairs({2, 3, 4, 6}) do mobile_walking(s[i], 13) end
    assert(s[2].stamina == 1 and s[3].stamina == 100, "夹具没有使用正式体力模块切换低体力与恢复")
    assert(not s[7].running and not s[7].runPreference, "退出手机接管仍遗留实际奔跑或手动偏好")
end, "手机体力中断：退回全速走、恢复体力不反复起跑、回到0.7后重推解锁")

test.system("player", "mobile_movement_bindings", {}, function(data)
    local function count(paths, expected)
        local total = 0
        for _, path in ipairs(paths) do if path == expected then total = total + 1 end end
        return total
    end
    assert(count(data.roll, "<FlatWorldMobileDevice>/roll") == 1, "手机翻滚按钮没有独立绑定正式翻滚动作")
    assert(count(data.roll, "<Keyboard>/space") == 1 and count(data.holdRun, "<Keyboard>/shift") == 1,
        "手机改动覆盖了原键鼠翻滚或按住奔跑")
    assert(count(data.toggleRun, "<Gamepad>/leftStickPress") == 1, "手机改动覆盖了手柄切换奔跑")
    for _, paths in ipairs({data.holdRun, data.toggleRun}) do
        for _, path in ipairs(paths) do
            assert(not string.find(path, "<FlatWorldMobileDevice>/", 1, true), "手机按钮仍接入旧奔跑动作")
        end
    end
end, "手机翻滚绑定：单独翻滚、移除按钮跑步、保留键鼠和手柄动作")

-- 移动、奔跑与营养水分的职责沿用正式移动动作配置。
local cases = {
    {name = "静止", args = {moving = false, running = false}, nutrition = 1, water = 1},
    {name = "静止时奔跑开关", args = {moving = false, running = true}, nutrition = 1, water = 1},
    {name = "步行", args = {moving = true}, nutrition = 1.6, water = 1},
    {name = "奔跑", args = {moving = true, running = true}, nutrition = 3.2, water = .25},
    {name = "关闭动作", args = {enabled = false, moving = true, running = true}, nutrition = 1, water = 1},
    {name = "负配置钳制", args = {move = -2, run = -3, water = -4, running = true}, nutrition = 0, water = 0},
    {name = "配置组合", args = {move = 2, run = 3, water = .5, running = true}, nutrition = 6, water = .5}
}
for _, case in ipairs(cases) do
    test.system("player", "movement_cost", case.args, function(data)
        assert(math.abs(data.nutrition - case.nutrition) < .00001, "营养消耗倍率错误")
        assert(math.abs(data.water - case.water) < .00001, "水分消耗倍率错误")
        assert(data.moveConfiguration >= 0 and data.runConfiguration >= 0, "配置未钳制")
    end, "移动消耗：" .. case.name)
end

-- 运动重置采样只驱动独立模块，检查重复调用和回调中的旧状态回写。
test.system("player", "motion_reset", {}, function(data)
    local function stopped(s)
        assert(not s.moving and not s.lease, "重置后仍残留走路或临时移动租约")
        assert(s.velocity == 0 and s.angularVelocity == 0 and s.driven == 0 and s.external == 0 and s.input == 0,
            "停止后仍残留刚体、主动、被动速度或输入")
        assert(s.running and s.speed == data.before.speed, "重置改动了奔跑偏好或速度倍率")
        assert(not s.acquiredInReset, "重置回调重新取得了主动移动租约")
    end
    assert(data.before.moving and data.before.velocity > 0, "夹具没有通过正式输入开始移动")
    stopped(data.reset)
    stopped(data.repeated)
    assert(data.reset.x == 17 and data.reset.y == 23, "重置覆盖了新的传送落点")
    assert(data.reset.starts == 1 and data.reset.ends == 1 and data.reset.resets == 1,
        "停止事件里的重置发生了递归或漏发")
    assert(data.repeated.ends == 1 and data.repeated.resets == 2, "重复重置重复发出停止事件")
    assert(data.resumed.moving and data.resumed.starts == 2, "重置后新输入不能正常恢复移动")
    stopped(data.locked)
    assert(data.acquired and data.owned.lease and not data.owned.moving and data.owned.driven == 3,
        "普通输入、目标移动或导航覆盖了临时动作的移动所有权")
    stopped(data.leaseReset)
    assert(data.acquiredAgain and data.afterStale.lease and data.afterStale.driven == 2,
        "旧租约写入或释放影响了新租约")
    for _, sample in ipairs(data.invalid) do stopped(sample) end
    for _, sample in ipairs(data.smooth) do
        for _, value in pairs(sample) do
            assert(type(value) == "number" and value == value and math.abs(value) < math.huge,
                "非法速度或时间污染了速度过渡结果")
        end
        assert(sample.timeX == 1 and sample.timeY == 0, "非法时间仍推进了速度过渡")
    end
    stopped(data.reentrant)
    stopped(data.nextReentrant)
    assert(data.nextReentrant.starts == data.reentrant.starts + 1 and data.nextReentrant.ends == data.reentrant.ends + 1,
        "开始事件里的重置破坏了下一次移动状态")
    stopped(data.missingBody)
end, "移动重置：落点、重复调用、回调重入、动作所有权、失效租约与非法输入")

-- 翻滚采样实走模块的发动、体力事务、碰撞缩放和收尾，不推进场景物理。
local function roll_near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label)
end
local function roll_restored(sample)
    assert(not sample.rolling and not sample.invulnerable and not sample.motionLease, "翻滚结束后仍残留动作或租约")
    roll_near(sample.width, .4, "身体宽度没有恢复")
    roll_near(sample.height, .3, "身体高度没有恢复")
    roll_near(sample.visualAngle, 0, "贴图旋转没有归零")
    roll_near(sample.offsetY, -.07, "翻滚改动了碰撞偏移")
end

for _, case in ipairs({
    {args = {x = 3, y = 4}, x = .6, y = .8},
    {args = {x = 1, cursorX = -1, facingX = -1}, x = 1, y = 0},
    {args = {x = 0, y = -1, cursorY = 1}, x = 0, y = -1},
    {args = {cursorX = 3, cursorY = 4, facingX = -1}, x = .6, y = .8},
    {args = {cursorY = -1}, x = 0, y = -1},
    {args = {facingX = -1}, x = -1, y = 0},
    {args = {x = 0, y = -1, facingX = -1}, x = 0, y = -1},
    {args = {facingX = 0}, x = 1, y = 0}
}) do
    test.system("player", "roll_direction", case.args, function(data)
        roll_near(data.x, case.x, "翻滚横向没有遵循移动、准星、朝向的优先级")
        roll_near(data.y, case.y, "翻滚纵向没有遵循移动、准星、朝向的优先级")
    end, "翻滚方向：移动优先、静止朝准星、准星重合回退朝向、对角归一化")
end

test.system("player", "roll_input", {}, function(data)
    assert(data.path == "<Keyboard>/space" and data.actionType == "Button", "翻滚默认空格按钮没有接入输入资产")
    assert(data.editableCount == 1 and not data.initialStateCheck, "翻滚按键未进入重绑或会在启用时重复触发")
end, "翻滚输入：默认空格、可重绑、按住不连续触发")

for _, running in ipairs({false, true}) do
    test.system("player", "roll_action", {running = running, duplicateBody = true, steps = {
        {action = "start", x = 3, y = 4}, {action = "start"}, {seconds = .2}, {seconds = .2},
        {action = "start"}, {action = "cancel"}
    }}, function(data)
        local s = data.samples
        assert(s[1].accepted and s[1].rolling and s[1].invulnerable and s[1].motionLease, "正式翻滚没有同时建立移动和无敌")
        roll_near(s[1].requestedCost, 10, "翻滚基础体力消耗不是10")
        assert(s[1].transactions == 1 and s[1].costSource == "flatworld.movement.roll", "翻滚体力没有使用独立来源事务")
        roll_near(s[1].drivenSpeed, 20, "翻滚没有按步行两倍速度，不应叠加奔跑倍率")
        roll_near(s[1].width, .2, "碰撞宽度没有缩为50%或重复缩放")
        roll_near(s[1].height, .15, "碰撞高度没有缩为50%")
        assert(not s[1].isWalking and not s[1].bodySimulated, "翻滚冒充步行或开启了夹具物理")
        assert(not s[2].accepted and s[2].stamina == s[1].stamina and s[2].transactions == 1, "重复按键扣费或重开了动作")
        roll_near(s[3].visualAngle, 180, "翻滚中点贴图没有转半圈")
        roll_near(s[3].directionX, .6, "中途输入改变了翻滚方向")
        roll_near(s[3].rootAngle, 0, "贴图旋转污染角色物理根节点")
        assert(s[3].spriteFacing < 0 and s[3].invulnerable, "贴图翻面丢失或无敌提前结束")
        roll_restored(s[4])
        roll_near(s[4].cooldown, 0, "翻滚结束后仍有冷却")
        assert(s[5].accepted and s[5].transactions == 2 and s[5].invulnerable and s[5].motionLease,
            "上一轮结束后不能立即再次翻滚，或没有独立支付体力和建立租约")
        roll_restored(s[6])
    end, "翻滚正式动作：尺寸、旋转、无敌、体力、防重入、零冷却接续和奔跑隔离")
end

for _, args in ipairs({{stamina = 0}, {missingBody = true}, {triggerOnly = true}}) do
    args.steps = {{action = "start"}}
    test.system("player", "roll_action", args, function(data)
        local s = data.samples[1]
        assert(not s.accepted and s.transactions == 0 and s.cooldown == 0, "拒绝翻滚仍然扣费或进入冷却")
        roll_restored(s)
    end, "翻滚拒绝：体力不足或缺少真实身体碰撞")
end

for _, action in ipairs({"cancel", "disable", "unload", "locked", "dead", "mover_reset", "mover_disable"}) do
    test.system("player", "roll_action", {steps = {{action = "start"}, {action = action}}}, function(data)
        assert(data.samples[1].accepted, "取消边界夹具未能发动")
        roll_restored(data.samples[2])
    end, "翻滚中断立即还原：" .. action)
end

test.system("player", "roll_action", {serverRemote = true, reentrant = true, steps = {
    {action = "start"}, {seconds = .4}
}}, function(data)
    local first = data.samples[1]
    assert(first.accepted and first.invulnerable and not first.motionLease, "服务端远端动作没有保持生命权威与位置归属")
    assert(first.moverLocked and not first.bodySimulated and first.transactions == 1 and not first.reentrantAccepted,
        "服务端远端动作改了移动锁/物理或允许扣费回调重入")
    roll_restored(data.samples[2])
end, "翻滚联机权威：远端不加载本地模块，不接管刚体，事务回调不能重入")

test.system("player", "roll_action", {steps = {
    {action = "presentation", sequence = 2}, {action = "presentation", active = false, sequence = 1},
    {action = "presentation", active = false, sequence = 2}, {action = "presentation", sequence = 2}
}}, function(data)
    local s = data.samples
    assert(s[1].rolling and not s[1].invulnerable and s[1].transactions == 0 and not s[1].motionLease,
        "远端视觉错误扣费或建立本地移动/无敌")
    assert(s[2].rolling, "旧停止包取消了新翻滚")
    roll_restored(s[3])
    roll_restored(s[4])
end, "翻滚远端表现：绝对进度、不叠缩放、旧包拒绝、停止后不复活")

test.system("player", "roll_action", {stamina = 0, steps = {
    {action = "owned", sequence = 8, elapsed = .2}, {action = "owned", sequence = 8, elapsed = .1},
    {seconds = .2}, {action = "owned", sequence = 8, elapsed = .1}
}}, function(data)
    local s = data.samples
    assert(s[1].rolling and s[1].motionLease and s[1].invulnerable and s[1].stamina == 0 and s[1].transactions == 0,
        "服务器许可没有使用正式移动，或客户端重复扣费")
    roll_near(s[2].elapsed, .2, "同序号快照倒退或重开动作")
    roll_restored(s[3])
    roll_restored(s[4])
end, "翻滚客户端许可：复用移动租约，不重复扣费，同序号不能续无敌")

test.system("player", "interaction_query_context", {}, function(data)
    assert(data.wrapped == 1 and data.infinite == 0, "交互空间查询没有复用正确拓扑或循环重复了同一目标")
    assert(data.pointerHit == 1 and data.pointerMiss == 0, "空间交互的精确落点范围改变")
    assert(data.denied == 0 and data.inactive == 0, "同轮新查询没有读取最新准入或活动状态")
    assert(data.moved == 0 and data.removed == 0, "移动或注销目标仍残留在交互候选")
end, "交互单轮拓扑：循环接缝、重复视觉、落点与实时失效")

-- 手机准线必须使用有限距离，零力度、推满和异常数值都不能生成无效坐标。
local cursorCases = {
    {name = "普通交互回中", args = {strength = 0}, distance = 0},
    {name = "普通交互半推", args = {strength = .5}, distance = 1},
    {name = "普通交互推满", args = {}, distance = 2},
    {name = "建筑放置推满", args = {maximum = 4}, distance = 4},
    {name = "较大有限距离半推", args = {maximum = 24, strength = .5}, distance = 12},
    {name = "最大距离正无穷", args = {invalid = "maximum_infinity"}, distance = 10},
    {name = "最大距离负无穷", args = {invalid = "maximum_negative_infinity"}, distance = 10},
    {name = "最大距离非数", args = {invalid = "maximum_nan"}, distance = 10},
    {name = "无穷距离回中", args = {invalid = "maximum_infinity", strength = 0}, distance = 0},
    {name = "最小距离无穷", args = {invalid = "minimum_infinity"}, distance = 2},
    {name = "最小距离非数", args = {invalid = "minimum_nan"}, distance = 2},
    {name = "力度非数", args = {invalid = "strength_nan"}, distance = 0},
    {name = "力度超出上限", args = {strength = 2}, distance = 2},
    {name = "负力度", args = {strength = -1}, distance = 0},
    {name = "负距离", args = {minimum = -1, maximum = -2}, distance = 0},
    {name = "最小距离大于最大距离", args = {minimum = 3, maximum = 2}, distance = 3},
    {name = "零方向回退", args = {zeroDirection = true}, distance = 2}
}
for _, case in ipairs(cursorCases) do
    test.system("player", "aim_cursor", case.args, function(data)
        assert(data.finite, "手机准线产生了无效世界坐标")
        assert(type(data.distance) == "number" and math.abs(data.distance - case.distance) < .00001,
            "手机准线没有遵守当前距离与力度")
        assert(math.abs(data.x - case.distance) < .00001 and data.y == 0 and data.z == 0,
            "手机准线方向或二维平面坐标错误")
    end, "手机准线距离：" .. case.name)
end

test.system("player", "focus_aim_batch", {}, function(data)
    assert(data.deltasCorrect and data.samples == 168,
        "鼠标瞄准合并距离后改变了环绕接缝、直瞄或半周期方向")
    assert(data.stopsCorrect and data.specialThresholds,
        "瞄准死区的距离临界值、负数或无效阈值行为改变")
end, "玩家瞄准复用一次指针与环绕拓扑，保持方向和死区边界")

test.system("player", "interaction_registry_prefilter", {}, function(data)
    assert(data.correctRange and data.distantSkipped and data.pointerMatched,
        "交互目标预筛没有提前排除远目标或误改了光标命中")
    assert(data.infiniteRange and data.movedTargetUpdated and data.negativeRadiusRejected and data.radiusEdges,
        "交互目标跨周、实时移动或距离边界选择与原规则不一致")
end, "交互光标预筛先查距离再执行能力：环绕、鼠标与实时移动")

test.system("player", "trauma_modifiers", {}, function(data)
    local function check(sample, attack, inverted, label)
        assert(type(sample.attack) == "number" and math.abs(sample.attack - attack) < .00001,
            label .. "的创伤攻速倍率错误")
        assert(sample.moveX == (inverted and -2 or 2) and sample.moveY == (inverted and 3 or -3),
            label .. "的创伤移动方向错误")
    end
    check(data.baseline, 1, false, "无 Buff")
    check(data.stacked, .75, true, "同一角色双来源叠加")
    check(data.isolated, 1.8, true, "另一角色独立来源")
    check(data.oneConfusionRemoved, .75, true, "只移除一个混乱")
    check(data.bothConfusionsRemoved, .75, false, "移除全部混乱")
    check(data.oneAttackRemoved, .5, false, "只移除一个攻速修正")
    check(data.replacedAttack, .8, false, "替换同来源攻速修正")
    check(data.otherUnchanged, 1.8, true, "其它角色不受本角色变更影响")
    check(data.cleared, 1, false, "本角色全部修正撤销")
    check(data.otherCleared, 1, false, "另一角色全部修正撤销")
    assert(data.nullAttack == 1, "空角色不应带创伤加成")
end, "创伤 Buff 汇总缓存：多来源叠加、单独移除、更新及角色隔离")
for _, offset in ipairs({{x = 10, y = -20}, {x = -128, y = 256}}) do
    test.system("player", "physics_source", {offsetX = offset.x, offsetY = offset.y}, function(data)
        assert(data.ordinaryResolved and data.imageResolved, "普通碰撞体或镜像碰撞体没有归到真实源")
        assert(data.sourceHierarchyActive and data.collidersDisabled, "物理归源夹具没有遵守活动源层级或启用了实际碰撞")
        assert(data.ownerResolved, "镜像命中无法找到真实Item")
        assert(data.siblingResolved, "镜像命中无法找到真实Item的兄弟模块能力")
        assert(data.offsetX == offset.x and data.offsetY == offset.y, "镜像偏移绑定丢失")
        assert(data.absentCapability and data.nullRejected, "缺失能力或空碰撞源产生了虚假目标")
        assert(data.destroyedRejected and data.clearedRejected, "失效源被镜像自有组件冒充成新的命中目标")
    end, "物理命中镜像归源与兄弟能力：" .. offset.x .. "," .. offset.y)
end
