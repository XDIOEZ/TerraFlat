-- 独立四乘四草稿调用正式生态生成器。
test.system("ecology", "body_read_batch", {}, function(data)
    assert(data.initialGuid == 41 and data.initialRevision == 5,
        "发布批次丢失自然物身份或外观版本")
    assert(data.inactiveDead == 1 and data.inactiveSuspended == 1,
        "批量读取漏掉停用实体或改变死亡状态")
    assert(data.deadAfterWrite == 1 and data.movedX == 8 and data.movedRevision == 6,
        "批次内生命或姿态写入后仍读到旧值")
    assert(data.externalRevision == 9 and data.removed and data.createdGuid == 43,
        "显式通知、移除或新增没有使当前发布快照失效")
    assert(data.afterBatchRevision == 12 and data.nextBatchScale == 3 and data.plainDead == 0,
        "发布快照跨批保留了旧外观，或把没有生命能力的实体当作死亡")
end, "自然物发布批量读取保留停用状态与批内写入、移除、新增边界")

test.system("ecology", "spatial_geometry", {}, function(data)
    assert(data.bodyX == 12 and data.bodyY == 20 and data.bodyWidth == 4 and data.bodyHeight == 4,
        "冷编译阻挡框改变了根姿态、偏移或缩放")
    assert(data.hitX == 10 and data.hitY == 22 and data.hitWidth == 2 and data.hitHeight == 1,
        "冷编译受击框丢失模块局部姿态")
    assert(data.treeContainsBodyMin and data.treeContainsBodyMax and data.blocking,
        "树木受击框没有覆盖真实阻挡范围")
    assert(data.compiledUnchanged and data.compiledEnabled and not data.refreshedEnabled and data.refreshedWidth == 18,
        "热路径重新解析了已编译定义，或配置重编译没有更新碰撞参数")
end, "自然物碰撞几何冷编译保留姿态、树干覆盖与配置刷新")

test.system("ecology", "soil_snapshot_reuse", {}, function(data)
    local function near(a, b) return math.abs(a - b) < 0.00001 end
    assert(data.generatedWater == 6 and near(data.generatedFertility, 0.7),
        "复用容器没有读取自然地表生成水肥")
    assert(data.latestWater == 3 and near(data.latestFertility, 0.4) and near(data.latestRemainder, 0.25),
        "同批土壤读取缓存了旧数值或丢失共用耗水尾数")
    assert(data.emptyWater == 0 and data.emptyRemainder == 0,
        "土壤水量耗尽后复用容器残留了旧耗水尾数")
end, "自然物土壤容器复用仍逐次读取权威水肥与耗水尾数")

test.system("ecology", "capture_batch", {}, function(data)
    local expected = {0, 5, 7, 5, 11, 13}
    assert(#data.guids == #expected, "批量自然物快照新增了空数据或错误合并已有记录")
    for index, guid in ipairs(expected) do
        assert(data.guids[index] == guid, "批量写入改变了原记录顺序或新记录的首次出现顺序")
    end
    assert(data.lastFive and data.lastEleven and data.duplicatePreserved,
        "同 GUID 没有按最后输入覆盖第一条原记录，或改动了已有重复记录")
    assert(#data.removed == 1 and data.removed[1] == 19,
        "批量写入没有逐次清除相同 GUID 的删除标记，或误删了其他标记")
    assert(data.unrelatedPreserved and data.chunkCount == 2,
        "批量写入影响了其他区块，或空批次创建了重复区块")
    assert(data.secondBatchReplaced and data.lastGuid == 17 and data.itemCount == 7,
        "后续批次复用了失效索引，或新增记录没有按顺序追加")
end, "自然物快照批量写入保留顺序、重复覆盖与删除标记语义")

for _, seed in ipairs({17413, 8321}) do
    local value = seed
    test.system("ecology", "generation", {seed = value}, function(data)
        assert(#data.warm == 16 and #data.repeated == 16, "百分之百概率的合格格子没有生成自然物")
        local identities, cells = {}, {}
        for index, item in ipairs(data.warm) do
            local repeated = data.repeated[index]
            assert(item.guid ~= 0 and not identities[item.guid], "自然物身份为空或重复")
            identities[item.guid] = true
            assert(item.guid == repeated.guid and item.x == repeated.x and item.y == repeated.y,
                "重复生成改变了稳定身份或位置")
            assert(item.x >= 0 and item.x < 4 and item.y >= 0 and item.y < 4, "生态生成越出小区块")
            local cell = item.x .. ":" .. item.y
            assert(not cells[cell], "单产物规则在同格重复生成")
            cells[cell] = true
            assert(item.itemId == "probe-plant" and item.ruleId == "system-probe:plant", "生成丢失规则归属")
        end
        assert(#data.cold == 0 and #data.blocked == 0 and #data.disabled == 0, "低温、阻挡或零倍率没有阻止生成")
        assert(data.lowerBoundary and data.upperBoundary and not data.wrongBiome, "生态气候边界或群系掩码错误")
    end, "生态稳定身份、气候筛选与阻挡：种子" .. value)
end

-- 正式生成器的邻区标签查询由扩展输入计数，业务判断全部在 Lua。
test.system("ecology", "neighborhood_cache", {scenario = "identity"}, function(data)
    local sample = data.samples
    assert(sample.initial.found and sample.initial.built == 1 and sample.initial.exception == "",
        "首次查询没有真实生成地形并找到花朵")
    for _, name in ipairs({"caseRepeat", "newRequestVersion"}) do
        assert(sample[name].found and sample[name].built == 0 and sample[name].exception == "",
            "同标签大小写或任务版本变化没有复用确定性缓存：" .. name)
    end
    assert(not sample.otherSeed.found and sample.otherSeed.built == 1 and sample.otherSeed.exception == "",
        "不同世界种子串用了已有花朵缓存")
    assert(not sample.negativeRepeat.found and sample.negativeRepeat.built == 0 and sample.negativeRepeat.exception == "",
        "没有花朵的邻区未缓存否定结果")
    for _, name in ipairs({"otherEpoch", "otherDimension", "otherOrigin", "otherProfile", "wrapped"}) do
        assert(not sample[name].found and sample[name].built == 1 and sample[name].exception == "",
            "缓存未隔离世界纪元、地址、配置或环绕拓扑：" .. name)
    end
    assert(not sample.missingTag.found and sample.missingTag.built == 0 and sample.missingTag.exception == "",
        "无宿主规则提供的标签仍创建了整块地形")
end, "生态邻区正负缓存复用与种子、纪元、地址、配置、拓扑隔离")

test.system("ecology", "neighborhood_cache", {scenario = "lifecycle"}, function(data)
    local sample = data.samples
    assert(sample.cancelledBuild.exception == "OperationCanceledException" and sample.cancelledBuild.built == 1,
        "生成中取消没有中断真实邻区构建")
    assert(sample.retry.found and sample.retry.built == 1 and sample.retry.exception == "" and
        sample.retryRepeat.found and sample.retryRepeat.built == 0 and sample.retryRepeat.exception == "",
        "取消残留污染缓存，或重试成功后仍重复生成")
    assert(sample.cancelledHit.exception == "OperationCanceledException" and sample.cancelledHit.built == 0,
        "已取消请求命中缓存后仍被当成正常完成")
    assert(sample.retiredEpoch.exception == "OperationCanceledException" and sample.retiredEpoch.built == 0,
        "世界退出后旧纪元仍能查询或重建缓存")
    assert(sample.newEpoch.found and sample.newEpoch.built == 1 and sample.newEpoch.exception == "" and
        sample.newEpochRepeat.found and sample.newEpochRepeat.built == 0 and sample.newEpochRepeat.exception == "",
        "新世界未重新建立并复用独立缓存")
end, "生态邻区生成取消、重试与世界退出清理")

test.system("ecology", "neighborhood_cache", {scenario = "prefill"}, function(data)
    local sample = data.samples
    assert(sample.afterGeneration.found and sample.afterGeneration.built == 0 and sample.afterGeneration.exception == "" and
        sample.afterRepeatedGeneration.found and sample.afterRepeatedGeneration.built == 0 and
        sample.afterRepeatedGeneration.exception == "",
        "正式区块生成未复用现成地形预填伴生物所需花朵标签")
    assert(#sample.first.placements > 0 and #sample.first.placements == #sample.second.placements and
        sample.first.terrainHash == sample.second.terrainHash,
        "缓存预填改变了正式生成的地形或生态数量")
    local companions = 0
    for index, first in ipairs(sample.first.placements) do
        local second = sample.second.placements[index]
        for _, field in ipairs({"guid", "itemId", "x", "y", "offsetX", "offsetY", "hostGuid", "ruleId"}) do
            assert(first[field] == second[field], "缓存命中改变生态放置记录：" .. field)
        end
        if first.itemId == "probe-hive" then
            companions = companions + 1
            assert(first.hostGuid ~= 0, "蜂巢伴生记录丢失真实宿主身份")
        end
    end
    assert(companions > 0, "预填夹具未经过真正的蜂巢伴生生成")
end, "正式地形预填花朵标签与蜂巢宿主放置稳定")
