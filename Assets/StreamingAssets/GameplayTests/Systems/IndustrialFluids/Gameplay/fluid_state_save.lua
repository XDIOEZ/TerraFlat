test.check("world_ready")
test.check("player_ready")
test.fluid("core_serialization", "共用库存序列化")
test.runtime("industrial_fluids", "ownership", {}, function(d)
    assert(d.shared and d.restoredShared and d.capacity == 200, "同类相邻罐没有共享库存与总容量")
    assert(math.abs(d.a-90)<.000001 and math.abs(d.b-90)<.000001, "倒序恢复改变组合罐总量")
    assert(math.abs(d.dismantledAmount-45)<.000001 and math.abs(d.remainingAfterRemoval-45)<.000001, "拆回没有等比分出单格内容")
    assert(d.atmosphereDelta==0, "正常拆回把气体排到环境")
    assert(d.typesAndMaterialsSeparate and #d.combinations == 4, "不同罐型或材质错误组合")
    assert(d.differentLiquidsSeparate and math.abs(d.liquidsBeforeEmptying-90)<.000001,
        "空罐桥接把不同液体合并或丢失")
    assert(d.emptyTankJoined, "排空异种液体后没有自然加入相邻组合罐")
    for _, tank in ipairs(d.combinations) do
        assert(tank.shared and tank.capacity == 300, tank.id .. "三格未共用300L容量")
        assert(tank.leftMask == 2 and tank.middleMask == 10 and tank.rightMask == 8, "自然拼接四向掩码错误")
        assert(tank.split and tank.splitMask == 0, "拆掉中间格后仍共享或保留内部连边")
        assert(math.abs(tank.carried-50)<.000001 and math.abs(tank.leftAfterSplit-50)<.000001
            and math.abs(tank.rightAfterSplit-50)<.000001, "拆开三格组合罐丢料或造料")
        assert(math.abs(tank.restored-150)<.000001, "复装未恢复整组真实内容")
        assert(math.abs(tank.refilled-90)<.000001, "模板补给只保留首格或重复补给")
    end
end, "铁钢气液罐自然组合、断桥拆分、拆回份额与倒序恢复")
