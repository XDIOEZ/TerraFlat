-- 快照冻结真实掉落物；位置、数量和物质变化不应重复编码稳定载荷。
test.system("save", "dropped_payload_cache", {}, function(data)
    assert(data.samePayload and data.motionReused, "重复保存或数量、运动、物质变化时重新编码了稳定载荷")
    assert(data.initialModuleEncodes == 1 and data.dynamicModuleEncodes == 1 and data.rootChangeModuleEncodes == 1,
        "独立模块未复用，或仅修改轻量字段触发了模块编码")
    assert(data.firstAmount == 5 and math.abs(data.firstDurability - .8) < .0001 and data.firstTemperature == 21,
        "后续拾取或物质变化污染了先前快照")
    assert(data.movedAmount == 3 and math.abs(data.movedDurability - .4) < .0001 and data.movedTemperature == 80
        and math.abs(data.movedMoisture - .6) < .0001 and data.movedBurning and data.movedBurnSeconds == 2,
        "数量、耐久或物质变化没有往返")
    assert(data.positionX > 1 and data.scaleX == .75 and data.flight and data.flightElapsed == .25
        and data.water and data.waterKind == 1 and data.waterElapsed > .5, "运动和水态没有独立往返")
    assert(data.specialChanged and data.inventoryChanged and data.childAmount == 2, "特殊数据或内嵌库存变化未保存")
    assert(data.physicsChanged and data.childVelocityX == 7, "内嵌物品的物理状态变化未保存")
    assert(data.fluidChanged and data.gasMoles == 8 and data.reservedGas == 2 and data.energy == 240,
        "气液库存、预留或内能变化未保存")
    assert(data.liquidChanged and data.liquidAmount == 1.5 and data.liquidReserved == .5,
        "液体容器组分或预留变化未保存")
    assert(data.binaryChanged and data.binaryByte == 9, "原位修改的二进制模块未失效")
    assert(data.moduleChanged and data.changedModuleEncodes == 2 and data.moduleValue == 7,
        "模块真实变化没有重编码，或无关变化重复编码模块")
    assert(data.replacementChanged and data.replacementId == "probe:replacement", "同 GUID 物态替换仍使用旧载荷")
end, "掉落物独立动态状态、模块复用与气液内容保存")

-- 冷世界走正式库存转移并往返，验证分堆、合并与迁移副本隔离。
test.system("save", "dropped_cold_payload", {}, function(data)
    assert(data.blocked == 0 and data.partial == 2 and data.firstRemainder == 3,
        "拒绝或部分转移后掉落物数量错误")
    assert(data.merged == 2 and data.secondRemainder == 1 and data.mergedAmount == 4,
        "合并到已有库存堆后存档没有保留真实余量")
    assert(data.sourceGuid == 4301 and data.splitGuid ~= 0 and data.splitGuid ~= data.sourceGuid,
        "分堆没有使用独立身份")
    assert(data.reusedAfterPartial and data.reusedAfterMerge, "仅数量变化时重复编码稳定物品载荷")
    assert(data.originalAmount == 5 and data.originalX == 2, "冷世界变更污染了先前快照")
    assert(data.projectedAmount == .5 and data.projectedX == 12 and data.projectedRotation == 90
        and data.sourceAmount == 1 and data.sourceX == 2, "跨世界投影的数量或位姿污染来源")
    assert(data.archiveVersion == 2 and data.archiveSourceAmount == 1 and data.archiveTargetAmount == .5
        and data.oldVersionRejected, "独立存档版本或世界隔离往返错误")
    assert(data.canonicalIndependentCaptures, "不同运行体捕获同一稳定内容时混入了动态状态")
    assert(not data.unknownReused and data.unknownEncodes == 2, "未知扩展模块被错误复用")
    assert(not data.cycleReusable, "循环引用被误判为可复用的内容")
end, "冷世界分堆、合并、迁移副本与未知模块回退")

-- 同一缓存先私有、后公开、再私有时，不暴露嵌套玩家的私有库存。
test.system("save", "dropped_public_payload", {}, function(data)
    assert(data.privateBeforeSpecial == "private-special" and data.privateBeforeCount == 1,
        "公开捕获前的私有玩家状态缺失")
    assert(data.publicSpecial == "public-special" and data.publicCount == 0 and
        data.serializedPublicSpecial == "public-special" and data.serializedPublicCount == 0,
        "公开捕获或冷记录公开序列化暴露了嵌套玩家状态")
    assert(not data.publicReused, "公开策略下错误复用了稳定缓存")
    assert(data.privateAfterSpecial == "private-special" and data.privateAfterCount == 1 and
        data.privateRecapturedSpecial == "private-special" and data.privateRecapturedCount == 1,
        "公开序列化后覆盖了原始私有数据，或私有捕获复用了公开结果")
end, "掉落物公开与私有快照交错时的嵌套玩家隔离")
