local function near(actual, expected, label)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) < 0.00001, label)
end

test.system("mechanical", "railway_paths", {}, function(data)
    assert(#data.paths == 12, "铁路没有覆盖四向直线和八向弯道")
    for _, path in ipairs(data.paths) do
        near(path.startError, 0, "轨道入口没有对齐半格端点")
        near(path.endError, 0, "轨道出口没有对齐半格端点")
        near(path.projectionError, 0, "轨道投影与推进弧长不一致")
        near(path.minTangentLength, 1, "轨道切线不是单位方向")
        near(path.startDirection, 1, "入口切线反向")
        near(path.endDirection, 1, "出口切线反向")
        near(path.length, path.curved and math.pi / 4 or 1, "弯轨没有使用真实四分之一圆弧长")
    end
    assert(data.teeMask == 5 and not data.teeCurved, "三邻轨错误连成岔路")
    assert(data.crossMask == 10 and not data.crossCurved, "四邻轨错误连成十字穿轨")
end, "铁路：十二种路径端点、切线、投影与无岔路选择")

test.system("mechanical", "engine_power", {}, function(data)
    assert(data.genericMotorRecognized and data.genericSupplied, "动力接收方未通过通用能力接受独立马达")
    near(data.genericAvailable, 2, "通用动力探测错误")
    near(data.genericPower, 0.75, "提供方超过需求供能")
    near(data.lowPower, 1, "蒸汽低档输出错误")
    near(data.highPower, 3, "蒸汽高档输出错误")
    near(data.offPower, 0, "关闭引擎仍输出")
    near(data.dryPower, 0, "缺水仍输出")
    near(data.emptyPower, 0, "缺燃料仍输出")
    assert(not data.shortWater and not data.shortFuel and data.exactResources and not data.invalidResources,
        "资源不足、等量或无效资源的供能资格错误")
    assert(not data.dryActualSupply and data.dryActualPower == 0 and data.dryFuelUnchanged,
        "真实冷库存蒸汽机缺水时仍扣燃料储备")
end, "通用引擎：独立马达扩展、请求上限、开关档位与缺水事务")

local directions = {
    {name = "横轨D向右", args = {x = 1, y = 0}, value = 1},
    {name = "横轨A向左", args = {x = -1, y = 0}, value = -1},
    {name = "横轨W不侧滑", args = {x = 0, y = 1}, value = 0},
    {name = "竖轨W向上", args = {x = 0, y = 1, tx = 0, ty = 1}, value = 1},
    {name = "竖轨S向下", args = {x = 0, y = -1, tx = 0, ty = 1}, value = -1},
    {name = "轨道正向相反仍按屏幕方向", args = {x = 1, y = 0, tx = -1}, value = -1}
}
for _, case in ipairs(directions) do
    test.system("mechanical", "minecart_contracts", case.args, function(data)
        assert(math.abs(data.throttle - case.value) < 0.0001, "WASD方向与轨道切线不一致")
        assert(data.zeroPower == 0 and data.idle == 0, "引擎与人力都未供能或松键后不应自行驾驶")
        assert((case.value == 0 and data.speed == 0) or data.speed * case.value > 0, "动力运动方向错误")
    end, "矿车：" .. case.name)
end
test.system("mechanical", "minecart_contracts", {power = 0.5}, function(data)
    assert(math.abs(data.speed - 1.5) < 0.0001, "实际供能减少时加速度仍按预估满功率结算")
end, "矿车：按实际输出计算加速")

local manual_cases = {
    {name = "无引擎消耗体力", args = {}, power = .25, stamina = 5, transactions = 1},
    {name = "倒车同样消耗体力", args = {throttle = -1}, power = .25, stamina = 5, transactions = 1},
    {name = "半油门按比例消耗", args = {throttle = .5}, power = .25, stamina = 7.5, transactions = 1},
    {name = "分步按实际时间消耗", args = {seconds = .2}, power = .25, stamina = 9, transactions = 1},
    {name = "蒸汽动力优先", args = {enginePower = 1}, power = 0, stamina = 10, transactions = 0},
    {name = "弱引擎不额外扣体力", args = {enginePower = .1}, power = 0, stamina = 10, transactions = 0},
    {name = "松键不扣体力", args = {throttle = 0}, power = 0, stamina = 10, transactions = 0},
    {name = "离轨不做人力驾驶", args = {onRail = false}, power = 0, stamina = 10, transactions = 0},
    {name = "体力不足原子拒绝", args = {stamina = 4}, power = 0, stamina = 4, transactions = 0},
    {name = "体力耗尽没有动力", args = {stamina = 0}, power = 0, stamina = 0, transactions = 0},
    {name = "恰好足够可驱动", args = {stamina = 5}, power = .25, stamina = 0, transactions = 1},
    {name = "没有体力模块不免费供能", args = {missingStamina = true}, power = 0, stamina = 10, transactions = 0},
    {name = "零时间不供能不扣费", args = {seconds = 0}, power = 0, stamina = 10, transactions = 0}
}
for _, case in ipairs(manual_cases) do
    test.system("mechanical", "minecart_manual_drive", case.args, function(data)
        near(data.power, case.power, "人力供能资格错误")
        near(data.stamina, case.stamina, "人力驾驶体力扣除错误")
        assert(data.transactions == case.transactions, "人力驾驶重复扣费或拒绝时仍扣费")
        if case.transactions > 0 then
            assert(data.source == "flatworld.railway.manual_drive", "人力体力消耗来源错误")
            assert(data.singleSpeed > 0 and data.trainSpeed > 0 and data.trainSpeed < data.singleSpeed,
                "人力不能驱动车列或增加车厢没有降低速度")
        end
    end, "矿车人力：" .. case.name)
end

test.system("mechanical", "minecart_held_turn", {}, function(data)
    assert(#data.paths == 24, "长按过弯没有覆盖十二种轨道的正反方向")
    for _, path in ipairs(data.paths) do
        near(path.minimum, 1, "按住原方向过弯时油门随切线转向衰减或归零")
        near(path.reverse, -path.sign, "到弯道出口换按原按键的反方向不能倒车")
        near(path.released, 0, "松键后仍沿用旧驾驶方向")
        assert(path.cleared, "松键没有清除上次选定的方向")
        near(path.restarted, path.curved and 0 or path.sign, "松键后起步没有按当前轨道重新判断方向")
        near(path.changed, path.sign, "换方向键后没有按当前位置选择沿轨方向")
        near(path.analog, path.sign * .4, "模拟输入幅度没有独立于转弯角度控制油门")
    end
end, "矿车：长按贯穿弯道、反向、松键清理及重新起步")

test.system("mechanical", "minecart_surface", {}, function(data)
    near(data.rails, 1, "整列在轨时错误套用了地面阻力")
    near(data.ground, .025, "整列在地面时没有使用高阻速度倍率")
    assert(data.ground < data.mixed and data.mixed < data.lighterGroundFraction and data.lighterGroundFraction < data.rails,
        "地面车厢比例与整列阻力不一致")
    near(data.railPush.x, 3, "轨道推动错误削减切向速度")
    near(data.railPush.y, 0, "轨道推动仍产生侧滑")
    near(data.groundPush.x, 3, "地面推动丢失横向分量")
    near(data.groundPush.y, 4, "地面推动丢失纵向分量")
    near(data.crossPush.x, 0, "横推轨道错误变成前进")
    near(data.crossPush.y, 0, "横推轨道错误变成侧滑")
    near(data.scaledPush.x, 1.5, "推动倍率未作用于横向速度")
    near(data.scaledPush.y, 2, "推动倍率未作用于纵向速度")
    near(data.groundStop.x, 0, "地面高阻未快速制停")
    assert(data.railStop.x > data.groundStop.x and data.railStop.x < 5, "轨道与地面制动没有区别")
    near(data.acceleration.x, 3, "表面运动没有按真实时间推进加速")
    near(data.zeroTime.x, 5, "零时间仍推进表面运动")
    assert(data.captureNear and not data.captureFar and not data.captureEndpointOutward and not data.captureEndpointStopped and data.captureEndpointInward
        and data.captureOrdinarySegment and not data.captureInvalid and data.captureCurvePlacement and not data.captureParallelMidpoint,
        "上轨距离、离轨端点方向或无效距离的吸附资格错误")
    assert(data.curveCenterDistance > .2 and data.curveCenterDistance < .25,
        "弯轨格心没有落在正式吸轨范围内")
    near(data.parallelMidpointDistance, .5, "平行轨之间的中点距离采样错误")
    near(data.limitedSubmit.x, 2.1, "推动反馈没有只裁掉超速的主动法向分量")
    near(data.limitedSubmit.y, 6, "推动反馈错误削减切向或环境速度")
    near(data.repeatedSubmit.x, 2.1, "以同一推动意图重新提交时速度继续衰减")
    near(data.repeatedSubmit.y, 6, "重复提交丢失切向速度")
    near(data.preservedIntent.x, 3, "推动反馈覆盖了原始横向意图")
    near(data.preservedIntent.y, 4, "推动反馈覆盖了原始纵向意图")
    near(data.externalOnlySubmit.x, 2, "没有推动意图时仍裁剪环境速度")
    near(data.externalOnlySubmit.y, 2, "没有推动意图时改变了环境方向")
    near(data.tangentSubmit.x, 2, "纯切向推动误削减环境法向速度")
    near(data.tangentSubmit.y, 6, "纯切向推动被高阻反馈减速")
end, "矿车：地面高阻、整列表面阻力、推动方向和进出轨资格")

local reject_modes = {"quantity", "drag", "add", "batch"}
for _, mode in ipairs(reject_modes) do
    test.system("mechanical", "inventory_acceptance", {mode = mode}, function(data)
        assert(not data.accepted and data.preview == 0 and not data.uiPreview, "非引擎绕过了库存接收规则")
        assert(data.sourceUnchanged and data.targetUnchanged and data.sourceEvents == 0 and data.targetEvents == 0,
            "拒绝转移时库存或事件已被修改")
    end, "引擎槽统一拒收：" .. mode)
end
test.system("mechanical", "inventory_acceptance", {mode = "swap", target = "engine"}, function(data)
    assert(not data.accepted and data.source == "ore" and data.target == "engine" and data.sourceUnchanged and data.targetUnchanged,
        "不合格物品通过交换挤掉了引擎，或拒绝时丢失物品")
end, "引擎槽：异类交换原子拒绝")
test.system("mechanical", "inventory_acceptance", {mode = "swap", source = "engine", target = "ore", sourcePolicy = true, policy = false}, function(data)
    assert(not data.accepted and data.sourceUnchanged and data.targetUnchanged, "交换未校验来源槽的反向接收资格")
end, "引擎槽：交换双向接收校验")
test.system("mechanical", "inventory_acceptance", {source = "engine"}, function(data)
    assert(data.accepted and data.source == "" and data.target == "engine" and data.preview == 1 and data.uiPreview,
        "有效引擎不能进入专用槽")
    assert(data.sourceEvents == 1 and data.targetEvents == 1, "成功转移应恰好通知双方一次")
end, "引擎槽：正式事务保留单件")
test.system("mechanical", "inventory_acceptance", {policy = false}, function(data)
    assert(data.accepted and data.source == "" and data.target == "ore", "无策略的普通库存行为被改变")
end, "普通货仓：无策略继续接受货物")
test.system("mechanical", "inventory_acceptance", {mode = "expired", source = "engine"}, function(data)
    assert(not data.accepted and data.sourceUnchanged and data.targetUnchanged and data.sourceEvents == 0,
        "失效的矿车面板会话仍能完成物品转移")
end, "矿车货仓：过期会话拒绝转移")
