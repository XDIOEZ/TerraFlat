-- 采样使用附近合格空格执行正式耕作与灌溉，返回前恢复现场，因此只做数据断言。
test.check("player_ready", "农业现场测试玩家就绪")
test.runtime("agriculture", "tillage_irrigation", {}, function(data)
    assert(data.scope == "live-isolated-world", "农业测试没有进入隔离世界")
    assert(data.partialAccepted and not data.partialCompleted and data.partialGroundPreserved,
        "未完成耕作提前改变正式地形")
    assert(data.completionAccepted and data.tillCompleted and data.groundAfter == "Tile_Farmland",
        "完成耕作没有提交正式耕地")
    assert(math.abs(data.afterIrrigation - 30) < 0.0001 and math.abs(data.afterConsumption - 25) < 0.0001,
        "正式土壤灌溉与消耗没有提交到真实区块")
    assert(data.savedSource > 0 and data.savedSource == data.currentSource and data.savedCropAbsent
        and math.abs(data.savedWater - data.afterConsumption) < 0.0001, "农业独立差量与真实水肥不一致")
    assert(data.restored, "农业现场或自有农业差量没有恢复")
end, "真实分段耕作、灌溉提交与农业差量")
