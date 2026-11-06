local function maximumDrivenSpeed(samples)
    assert(#samples > 0, "骑行期间没有采集固定步速度")
    local maximum = 0
    for _, sample in ipairs(samples) do maximum = math.max(maximum, sample.drivenSpeed) end
    return maximum
end

test.check("world_ready", "自行车隔离世界已进入")
test.runtime("player", "bicycle_lifecycle", {action = "begin"}, function(data)
    assert(data.initialPositionError < .01 and data.available and data.mountedAfterAction and data.bodySimulated and not data.riderBodySimulated and
        data.riderTerrainContact and data.wakeReady,
        "正式车体或上车租约未建立")
    assert(data.contactShadowBound, "自行车生成后未注册正式接触阴影")
end, "生成自行车并通过正式交互上车")
test.move(1, 0, .7)
test.runtime("player", "bicycle_lifecycle", {action = "land"}, function(data)
    local speed = maximumDrivenSpeed(data.rideSamples)
    assert(data.mounted and data.distance > 1 and speed > 5.5 and speed <= 6.01 and
        not data.riderInWater,
        "模拟移动输入没有驱动自行车或陆地限速失效")
    assert(data.wheelAngle > .01 and data.sameWheelRotation and data.pedalAngle > .01 and
        data.pedalTransformSynced and data.seatError < .05,
        "骑行车轮或脚踏没有同步转动，或乘员没有准确跟座")
    assert(data.shadowGrounded, "自行车阴影落点被车架轮廓夹高，没有贴地")
end, "真实骑行、速度、车轮与跟座")
test.wait(.5)
test.runtime("player", "bicycle_lifecycle", {action = "wall"}, function(data)
    assert(data.drivenSpeed < .01, "松手后自行车未制动停止")
end, "松手制动并准备实体墙")
test.move(1, 0, .7)
test.runtime("player", "bicycle_lifecycle", {action = "wall_mark"}, function(data)
    assert(data.speed < .05, "真实碰撞未阻挡车体")
end, "撞墙后记录真实车体和轮子状态")
test.move(1, 0, .3)
test.runtime("player", "bicycle_lifecycle", {action = "wall_sample"}, function(data)
    assert(data.blockedDistance < .01 and data.blockedWheelTurn < .1 and data.blockedPedalTurn < .1,
        "车体堵住后仍位移，或车轮及脚踏按输入空转")
end, "保持骑行输入时，堵住的车轮不空转")
test.wait(.5)
test.runtime("player", "bicycle_lifecycle", {action = "water"}, function(data)
    assert(data.drivenSpeed < .01, "松手后自行车未制动停止")
end, "松手制动并建立真实涉水区域")
test.move(1, 0, 1)
test.runtime("player", "bicycle_lifecycle", {action = "water_sample"}, function(data)
    local speed = maximumDrivenSpeed(data.rideSamples)
    assert(data.mounted and data.inWater and speed > .55 and speed < .65 and
        data.riderTerrainContact and data.riderInWater and data.riderLiquidDepth > 0,
        "真实水面骑行速度没有保持陆地的10%")
end, "真实水面骑行限速0.6格每秒")
test.wait(.4)
test.runtime("player", "bicycle_lifecycle", {action = "push"}, function(data)
    assert(not data.mountedAfterAction, "再次交互未下车")
end, "下车并准备碰撞推动")
test.move(1, 0, 1)
test.runtime("player", "bicycle_lifecycle", {action = "push_sample"}, function(data)
    assert(not data.mounted and data.riderBodySimulated and data.pushDistance > .1,
        "没有乘坐时，实际碰撞未能推动自行车")
end, "真实移动碰撞可以推动空车")
test.wait(.5)
test.runtime("player", "bicycle_lifecycle", {action = "push_coast"}, function(data)
    assert(not data.pusherIsPlayer and data.coastStartSpeed > .5 and
        data.coastDistance > .3 and data.drivenSpeed > .2,
        "玩家松开推动后，自行车没有继续以滚动惯性前进")
end, "松开推动后车轮继续滚动，速度逐渐衰减")
test.runtime("player", "bicycle_lifecycle", {action = "finish"}, function(data)
    assert(data.mountedAfterAction, "推动后无法重新骑车")
end, "重新上车并保留截图现场")
