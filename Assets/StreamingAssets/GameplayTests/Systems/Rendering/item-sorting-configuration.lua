-- 采样正式装配与最终排序键，画面仍需现场复核。
test.system("rendering", "item_sorting_configuration", {}, function(data)
    assert(data.definitionBound and data.relativeOrderPreserved, "物品未持有当前视觉配置，或修改了分组内部排序")
    assert(data.heldRestored and data.removedConfigurationCleared and data.pooledConfigurationCleared,
        "手持、删除配置或对象池复用残留旧排序")
    assert(data.rejectsUnknownLayer and data.rejectsEmptyLayer and data.rejectsOrderUnderflow and data.rejectsOrderOverflow,
        "无效图层或越界排序值未在定义编译时拒绝")
    assert(data.droppedLayer == "Default" and data.droppedOrder == 0, "轻量掉落物忽略了物品排序配置")
    local samples = {}
    for _, sample in ipairs(data.samples) do samples[sample.step] = sample end
    for _, step in ipairs({"floor_north", "floor_south", "grouped_floor", "reused_as_floor"}) do
        local sample = samples[step]
        assert(sample.layer == "Default" and sample.order == 0 and sample.layerValue < data.playerLayerValue,
            "地板被自动类别、分组或池复用改回玩家图层：" .. step)
        assert(sample.depthBatch and sample.groundBatch, "地板错误进入动态建筑合批：" .. step)
    end
    assert(samples.floor_north.y > 0 and samples.floor_south.y < 0, "没有覆盖玩家两侧的地板排序")
    for _, step in ipairs({"custom_order", "returned_from_hand", "reenabled"}) do
        local sample = samples[step]
        assert(sample.layer == "Player" and sample.order == 147 and sample.order > data.playerOrder,
            "自定义排序被热刷新、手持切换或重新显示覆盖：" .. step)
        assert(not sample.depthBatch, "自定义排序被固定排序键的动态合批吞掉：" .. step)
    end
    assert(samples.order_only.layer == "Player" and samples.order_only.order == 99,
        "只配置排序值时没有保留默认一级图层")
    assert(samples.layer_only.layer == "Default" and samples.layer_only.order == 100 and not samples.layer_only.depthBatch,
        "只配置图层时没有保留默认二级顺序，或被合批覆盖")
    assert(samples.defaults_restored.layer == "Player" and samples.defaults_restored.order == 100 and
        samples.defaults_restored.depthBatch and not samples.defaults_restored.groundBatch,
        "清除配置没有回到正式动态排序")
    assert(samples.minimum_order.order == -32768 and samples.maximum_order.order == 32767,
        "合法排序边界被截断")
    local catalog = {}
    for _, item in ipairs(data.catalog) do catalog[item.id] = item end
    for _, id in ipairs({"ShipFloor_Iron", "ShipFloor_Refractory", "RailTrack"}) do
        assert(catalog[id].layer == "Default" and catalog[id].order == 0 and
            catalog[id].material == "Assets/9_Shaders/Material/Ground-Facility-Lit.mat", "贴地物品配置错误：" .. id)
    end
    for _, id in ipairs({"ShipFloor_Iron_Summoner", "Stick_Wood"}) do
        assert(catalog[id].layer == "Player" and catalog[id].order == 100, "普通物品未继承动态默认图层：" .. id)
    end
end, "物品配置排序：一级图层、二级顺序、地板、分组、热更新、手持、池复用与合批")
