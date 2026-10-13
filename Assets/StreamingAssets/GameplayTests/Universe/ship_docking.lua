-- 独立相向接口样本走双方确认和断开事务，场景按钮与多人请求由额外流程验收。
test.space("ship_docking", function(data)
    assert(data.operation == "ship_docking" and data.scope == "production-domain", "缺少正式对接领域结果")
    assert(data.candidate == true and data.groupsBefore == 2, "相向贴合接口未产生候选或提前合船")
    assert(data.firstConfirm == true and data.connectedAfterFirst == false, "单方确认不能直接建立连接")
    assert(data.secondConfirm == true and data.connectedAfterSecond == true, "双方确认未建立实际连接")
    assert(data.connectedGroups == 1, "连接后应只有一个运动整体")
    assert(type(data.momentumAfter) == "number" and math.abs(data.momentumAfter) <= 0.0000001,
        "等质量反向低速对接没有守恒零总动量")
    assert(data.disconnectAccepted == true and data.connectedAfterDisconnect == false,
        "断开入口未撤销实际连接")
    assert(data.disconnectedGroups == 2, "断开后两个成员未恢复独立运动整体")
    assert(data.distantCandidateAccepted == false, "接口远离后不能继续生成对接候选")
    assert(#data.ships == 2 and data.ships[1].id ~= data.ships[2].id, "对接事务丢失独立船体身份")
    return "覆盖相向捕获、双方确认、运动整体合并、动量守恒、断开和远距离拒绝。"
end, "飞船双方对接与断开")
