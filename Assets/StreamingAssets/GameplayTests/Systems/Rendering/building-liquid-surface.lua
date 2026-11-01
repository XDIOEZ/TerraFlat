-- 建筑水面覆盖独立规则，避免与其他渲染主题合并后超过步骤上限。
test.system("rendering", "building_liquid_surface", {}, function(data)
    local expected = { false, true, false, true, false, true, true }
    assert(#data.samples == #expected, "建筑水面状态采样不完整")
    for index, submerged in ipairs(expected) do
        assert(data.samples[index].submerged == submerged,
            "建筑水面覆盖错误：" .. data.samples[index].stage)
    end
    assert(data.samples[3].depth > 0 and data.samples[3].support ~= 0,
        "平台错误地清空了原始水层")
    assert(data.samples[7].support == 0, "相邻平台错误地支撑当前建筑")
    assert(not data.outsideSubmerged and not data.missingSubmerged, "缺失地形错误地标记为水下")
    assert(data.groundQueue < data.buildingQueue and data.buildingQueue < data.waterQueue,
        "水下建筑未处于海床之后、水面之前")
    local batch = {}
    for _, notification in ipairs(data.notifications) do
        if notification.kind == "batch" then batch[#batch + 1] = notification end
    end
    assert(#batch == 3 and batch[1].index == 0 and not batch[1].submerged,
        "批量排水没有读取已提交的干地状态")
    assert(batch[2].index == 0 and batch[2].submerged and batch[3].index == 1 and batch[3].submerged,
        "批量涨水未覆盖浅水和相邻深水格")
end, "建筑水面：直接入水、平台增删、批量排水与涨水")

test.system("rendering", "frozen_ice_surface", {}, function(data)
    local initial = data.initial
    assert(#initial == 7, "冰面测试缺少海洋、河流、湖泊或回退场景")
    local beds = { 3, 4, 1 }
    local liquids = { "core:sea_water", "core:dirty_water", "core:dirty_water" }
    for i = 1, 3 do
        local cell = initial[i]
        assert(cell.ground and cell.groundMatches and cell.sourceBed == beds[i], "冰下没有绘制冻结前的底床")
        assert(cell.ice and cell.iceMatches and math.abs(cell.iceAlpha - cell.expectedIceAlpha) < .003,
            "天然冰没有使用 50% 透明度")
        assert(cell.sourceLiquid == liquids[i] and cell.sourceLevel == 81 - i and cell.waterCells == 0,
            "冰下出现额外水面或冻结来源水量被改变")
        assert(cell.groundQueue < cell.iceQueue and cell.iceQueue < data.waterQueue,
            "底床、冰面与水面排序错误")
    end
    assert(initial[1].iceCells == 4 and initial[1].iceGroups == 1, "同区块冰面没有按纹理合批")
    for _, i in ipairs({ 4, 5, 6 }) do
        assert(initial[i].ground and initial[i].groundMatches and not initial[i].ice,
            "缺失来源、缺少底床贴图或地块被替换时没有保留原地块")
    end
    assert(initial[7].groundMatches and initial[7].ice, "底床也是冰时没有保留原底床与冰层")
    assert(data.partial.ice and data.partial.groundMatches, "未完全融化时冰层或底床丢失")
    assert(data.meltedProgress.ice and data.meltedProgress.groundMatches,
        "融化进度完成但地块未切换时冰层提前消失")
    assert(not data.meltedBed.ice and data.meltedBed.groundMatches, "解冻后没有显示恢复的底床")
    assert(not data.replacedIce.ice and data.replacedIce.groundMatches,
        "融化后重新放置的冰被误判为原来的天然冰面")
    assert(not data.rollbackBeforeCell.ice and data.rollbackBeforeCell.groundMatches,
        "回滚进度先于地块恢复时提前画冰")
    assert(data.rollback.ice and data.rollback.groundMatches, "地块回滚后没有恢复半透明冰层")
    assert(data.unbound and data.other.groundMatches and data.other.iceCells == 0,
        "解绑或复用区块残留旧冰层")
    assert(data.rebound.ice and data.rebound.groundMatches, "重绑原区块没有恢复冰面")
    assert(data.unknownLayerRefreshed, "未指定环境层的通知被冰层刷新吞掉")
    assert(data.bindingWasIncomplete and data.bindingCompleted and data.changedDuringBinding.ice
        and data.changedDuringBinding.groundMatches and data.restoredAfterBinding.ice,
        "分帧重绑上传期间的融冰或回滚没有更新")
    assert(data.lazyInitiallyEmpty and data.lazyRebound.ice and data.lazyRebound.groundMatches and data.lazyMeshUploaded,
        "无冰区块在上传期间回滚时没有创建并上传冰面")
    local geometry = data.geometry
    if geometry.available then
        assert(geometry.initial and geometry.interior and geometry.shrinkX and geometry.shrinkY,
            "冰面网格增删后边界错误")
        assert(geometry.empty and geometry.reused and geometry.switched and geometry.bulk,
            "冰面网格清空、重用、纹理切换或批量更新错误")
    end
end, "天然海冰、河湖冰：底床、半透明、合批、融化回滚与重绑")

test.system("rendering", "frozen_ice_visual", {}, function(data)
    assert(data.metricScope == "editor-camera-render-submission", "冰面现场没有使用隔离预览相机")
    assert(data.chunkCount == 16 and data.cellCount == 4096 and data.iceGroupCount == 16,
        "冰面没有按区块合批")
    assert(data.preview and data.preview.bytes > 0 and type(data.preview.path) == "string"
        and #data.preview.path > 0 and #data.preview.rows == 3, "隔离相机没有读回完整冰面画面")
    for _, row in ipairs(data.preview.rows) do
        assert(row.eligible > 100 and row.blended > row.eligible * .6, "真实画面没有把冰与底床混合")
    end
    assert(data.renderSamples == 16 and data.bindMilliseconds >= 0
        and data.baselineRenderMilliseconds >= 0 and data.iceRenderMilliseconds >= 0,
        "冰面 CPU 提交采样不完整")
end, "冰面隔离相机：实际混合画面与区块合批采样")
