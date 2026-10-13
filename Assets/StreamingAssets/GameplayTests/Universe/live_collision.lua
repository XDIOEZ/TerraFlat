-- 用正式地板事务构造自有夹具，等待真实固定步计算跨星区船体接触。
test.space("ship_collision", function(data)
    assert(data.scope == "live-fixed-step-contacts" and data.fixtureSetup == "production-floor-transactions", "没有真实固定步接触结果")
    assert(data.afterTime > data.beforeTime, "真实宇宙会话没有推进")
    local left, right = data.afterLeft, data.afterRight
    assert(left.id == data.beforeLeft.id and right.id == data.beforeRight.id, "接触改变了船体身份")
    assert(left.mass > 0 and right.mass > 0, "接触夹具没有真实质量")
    local beforeMomentum = data.beforeLeft.mass * data.beforeLeft.velocity.x + data.beforeRight.mass * data.beforeRight.velocity.x
    local afterMomentum = left.mass * left.velocity.x + right.mass * right.velocity.x
    assert(math.abs(afterMomentum - beforeMomentum) <= math.max(0.001, (left.mass + right.mass) * 0.0001), "跨区船体接触没有保持总动量")
    assert(math.abs(left.velocity.x - data.beforeLeft.velocity.x) > 0.1 and math.abs(right.velocity.x - data.beforeRight.velocity.x) > 0.1, "相向跨区船体互相穿过，未结算冲量")
    assert(math.abs(data.afterRemote.velocity.x - data.beforeRemote.velocity.x) < 0.000001 and math.abs(data.afterRemote.velocity.y) < 0.000001, "遥远星区的夹具受到错误接触")
    assert(data.afterRemote.id == data.beforeRemote.id and data.afterRemote.mass == data.beforeRemote.mass, "远区船体身份或质量被改变")
    return "真实固定步：跨星区船体相撞、冲量守恒、远区排除与自有夹具回收"
end, "真实船体跨区接触与远区排除")
