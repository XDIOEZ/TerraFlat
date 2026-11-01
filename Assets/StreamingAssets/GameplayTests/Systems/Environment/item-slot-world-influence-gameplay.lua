local function near(actual, expected, label, tolerance)
    assert(type(actual) == "number" and type(expected) == "number"
        and math.abs(actual - expected) <= (tolerance or 0.05), label)
end

local function changed(actual, baseline, label)
    assert(type(actual) == "number" and type(baseline) == "number"
        and math.abs(actual - baseline) > 0.05, label)
end

test.runtime("environment", "item_slot_world_influence", {}, function(data)
    changed(data.temperatureBeforeInventoryMapReplacement, data.temperatureBefore, "嵌套库存映射替换前温度来源未生效")
    changed(data.radiationBeforeInventoryMapReplacement, data.radiationBefore, "嵌套库存映射替换前辐射来源未生效")
    changed(data.filthBeforeInventoryMapReplacement, data.filthBefore, "嵌套库存映射替换前污染来源未生效")

    near(data.temperatureAfterInventoryMapReplacement, data.temperatureBefore, "替换嵌套库存映射后旧温度来源未撤销")
    near(data.radiationAfterInventoryMapReplacement, data.radiationBefore, "替换嵌套库存映射后旧辐射来源未撤销")
    near(data.filthAfterInventoryMapReplacement, data.filthBefore, "替换嵌套库存映射后旧污染来源未撤销")

    changed(data.temperatureAdded, data.temperatureBefore, "嵌套槽内物品没有影响外界温度")
    changed(data.radiationAdded, data.radiationBefore, "嵌套槽内物品没有影响辐射层")
    changed(data.filthAdded, data.filthBefore, "嵌套槽内物品没有影响污染层")

    near(data.temperatureAfterLiveRemoval, data.temperatureBefore, "移出物品后温度来源没有撤销")
    near(data.radiationAfterLiveRemoval, data.radiationBefore, "移出物品后辐射来源没有撤销")
    near(data.filthAfterLiveRemoval, data.filthBefore, "移出物品后污染来源没有撤销")

    near(data.temperatureSecondAdd, data.temperatureAdded, "重复放入后温度来源叠加或丢失")
    near(data.radiationSecondAdd, data.radiationAdded, "重复放入后辐射来源叠加或丢失")
    near(data.filthSecondAdd, data.filthAdded, "重复放入后污染来源叠加或丢失")
    near(data.temperatureSnapshotRetained, data.temperatureSecondAdd, "冷快照未保留嵌套温度来源")
    near(data.radiationSnapshotRetained, data.radiationSecondAdd, "冷快照未保留嵌套辐射来源")
    near(data.filthSnapshotRetained, data.filthSecondAdd, "冷快照未保留嵌套污染来源")
    changed(data.radiationBeforeInventoryUnbind, data.radiationSnapshotRetained, "实时库存宿主新增辐射来源未与冷快照叠加")
    changed(data.filthBeforeInventoryUnbind, data.filthSnapshotRetained, "实时库存宿主新增污染来源未与冷快照叠加")
    changed(data.temperatureBeforeInventoryUnbind, data.temperatureSnapshotRetained, "实时库存宿主新增温度来源未与冷快照区分")
    near(data.temperatureAfterInventoryUnbind, data.temperatureSnapshotRetained, "真实库存解绑误删冷快照温度来源")
    near(data.radiationAfterInventoryUnbind, data.radiationSnapshotRetained, "真实库存解绑未撤销实时辐射来源")
    near(data.filthAfterInventoryUnbind, data.filthSnapshotRetained, "真实库存解绑未撤销实时污染来源")
    near(data.temperatureAfterPostUnbindSlotMutation, data.temperatureAfterInventoryUnbind, "解绑后槽位变化重新登记温度来源")
    near(data.radiationAfterPostUnbindSlotMutation, data.radiationAfterInventoryUnbind, "解绑后槽位变化重新登记辐射来源")
    near(data.filthAfterPostUnbindSlotMutation, data.filthAfterInventoryUnbind, "解绑后槽位变化重新登记污染来源")

    near(data.temperatureRestored, data.temperatureBefore, "移除全部宿主后温度底值未恢复")
    near(data.radiationRestored, data.radiationBefore, "移除全部宿主后辐射底值未恢复")
    near(data.filthRestored, data.filthBefore, "移除全部宿主后污染底值未恢复")
end, "嵌套库存内容动态影响温度、辐射和污染；冷快照接管及来源撤销")
