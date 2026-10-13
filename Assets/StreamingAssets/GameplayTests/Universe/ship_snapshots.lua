-- 独立生产会话模型往返验证身份和数值，不把内存 JSON 等同于磁盘存档验收。
local function near(actual, expected, label)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
end
test.space("ship_snapshots", function(data)
    assert(data.operation == "ship_snapshots" and data.scope == "production-domain", "缺少正式快照领域结果")
    assert(data.version == 2 and data.stableRepeatSnapshot == true, "会话快照版本或重复往返不稳定")
    near(data.simulationSeconds, 123.5, "会话时钟往返丢失")
    assert(data.shipCount == 1 and data.flightCount == 1 and data.navigationCount == 1 and data.looseCount == 1,
        "快照往返丢失船体、飞行、导航或散落物")
    assert(data.ship.id == "gameplay-probe-snapshot" and data.ship.pieces == 3, "船体身份或成员数量丢失")
    assert(data.ship.sectorX == "-3" and data.ship.sectorY == "4", "船体星区身份丢失或未按文字传输")
    near(data.ship.velocityX, 17, "船体横向速度丢失")
    near(data.ship.velocityY, -8, "船体纵向速度丢失")
    assert(data.flightPhase == "Cruising" and data.flightIgnited == true, "真实飞行阶段和点火状态丢失")
    assert(data.navigationSectorX == "-2" and data.navigationSectorY == "4", "导航目标星区丢失")
    near(data.passengerReleasedVelocity.x, 20, "读回后松开步行误删横向继承速度")
    near(data.passengerReleasedVelocity.y, -2, "读回后松开步行误删纵向继承速度")
    assert(#data.scaleKeys == 3, "缩放快照应只保存三个真实分量")
    local keys = {}
    for _, key in ipairs(data.scaleKeys) do
        assert(key == "x" or key == "y" or key == "z", "缩放包含可能递归的计算属性")
        assert(not keys[key], "缩放分量重复")
        keys[key] = true
    end
    assert(keys.x and keys.y and keys.z, "缩放缺少真实分量")
    near(data.scaleX, -2, "负缩放没有完整保存")
    near(data.scaleY, 0, "零缩放没有完整保存")
    near(data.scaleZ, 3.5, "非等比缩放没有完整保存")
    assert(data.loosePayload == "opaque-fixture-payload", "散落物原始载荷被快照修改")
    near(data.loosePosition.x, 8, "散落物横向位置丢失")
    near(data.loosePosition.y, -9, "散落物纵向位置丢失")
    near(data.looseVelocity.x, 10, "散落物横向速度丢失")
    near(data.looseVelocity.y, -11, "散落物纵向速度丢失")
    assert(type(data.limitations) == "string" and #data.limitations > 0, "快照结果必须保留覆盖边界")
    return "覆盖内存快照身份、时钟、船速、航点、步行分量、散落物载荷和三分量缩放往返。"
end, "太空会话快照往返")
