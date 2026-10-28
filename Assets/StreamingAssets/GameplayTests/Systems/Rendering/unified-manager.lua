-- 统一入口只采正式组件状态；最终 GPU 画面仍需现场验收。
test.system("rendering", "unified_manager", {}, function(data)
    assert(data.registered and data.repeatedRegistered and data.nullRejected and data.pooledRejected,
        "统一渲染入口没有拒绝空/池物品，或重复注册改变结果")
    assert(data.registeredSorting and data.independentSorting,
        "物品或独立 Renderer 没有通过统一入口应用正式排序键")
    assert(data.earlyMemberLayer == data.earlyExpectedLayer and data.earlyMemberOrder == data.earlyExpectedOrder and
        data.earlyMemberPreservedFacade,
        "独立排序成员早于统一入口绑定时没有复用现有后端，或意外创建了管理器")
    assert(data.globalSortingRestored, "独立渲染夹具没有恢复原有全局透明排序模式和坐标轴")
    assert(#data.samples == 17, "统一入口生命周期采样不完整")
    local s = {}
    for _, sample in ipairs(data.samples) do s[sample.step] = sample end
    for _, step in ipairs({ "registered", "registered_twice", "registered_again", "taken_from_pool" }) do
        local sample = s[step]
        assert(sample.registered and sample.registeredCount == 1 and sample.shadowEnabled and not sample.inWater,
            "注册、重复注册或池复用继承了上一次请求：" .. step)
        assert(sample.contactBound == sample.contactBackendEnabled and sample.sunBound == sample.sunBackendEnabled,
            "注册没有接入启用的接触/太阳阴影后端：" .. step)
        assert(not sample.highlighted and not sample.manuallyHighlighted and sample.interactionRequests == 0,
            "重新注册继承了旧高亮：" .. step)
    end
    assert(s.in_water.inWater and (not s.in_water.contactBackendEnabled or s.in_water.contactInWater),
        "统一水态请求没有传给真实接触阴影绑定")
    for _, step in ipairs({ "shadow_disabled", "structure_refreshed" }) do
        local sample = s[step]
        assert(sample.registered and sample.registeredCount == 1 and not sample.shadowEnabled and sample.inWater,
            "结构重建丢失物品阴影开关或水态请求：" .. step)
        assert(not sample.contactBound and not sample.sunBound,
            "关闭物品阴影后仍有接触/太阳阴影绑定：" .. step)
    end
    assert(s.shadow_reenabled.shadowEnabled and s.shadow_reenabled.inWater and
        s.shadow_reenabled.contactBound == s.shadow_reenabled.contactBackendEnabled and
        s.shadow_reenabled.sunBound == s.shadow_reenabled.sunBackendEnabled and
        (not s.shadow_reenabled.contactBackendEnabled or s.shadow_reenabled.contactInWater),
        "重新开启阴影没有恢复绑定或丢失已有水态")
    assert(s.manual_highlight.highlighted and s.manual_highlight.manuallyHighlighted,
        "物品手动高亮没有接入正式描边")
    assert(s.interaction_a.highlighted and s.interaction_a.manuallyHighlighted and s.interaction_a.interactionRequests == 1,
        "交互重复请求创建了重复描边或覆盖手动高亮")
    assert(s.manual_cleared.highlighted and not s.manual_cleared.manuallyHighlighted and s.manual_cleared.interactionRequests == 1,
        "取消手动高亮错误清除了交互描边")
    assert(s.interaction_b.interactionRequests == 2 and s.interaction_a_cleared.highlighted and
        s.interaction_a_cleared.interactionRequests == 1,
        "多个交互来源互相覆盖或取消一个来源导致全部描边消失")
    assert(not s.all_highlights_cleared.highlighted and s.all_highlights_cleared.interactionRequests == 0,
        "所有请求取消后仍留下物品描边")
    for _, step in ipairs({ "manual_highlight", "interaction_a", "manual_cleared", "interaction_b", "interaction_a_cleared" }) do
        assert(s[step].outlineControllers == 1, "多个调用入口重复创建描边控制器：" .. step)
    end
    for _, step in ipairs({ "unregistered_twice", "returned_to_pool", "destroyed" }) do
        local sample = s[step]
        assert(not sample.registered and sample.registeredCount == 0 and not sample.contactBound and not sample.sunBound,
            "注销、回池或直接销毁没有清理注册和阴影：" .. step)
        assert(not sample.highlighted and not sample.manuallyHighlighted and sample.interactionRequests == 0,
            "注销、回池或直接销毁残留高亮请求：" .. step)
    end
end, "统一渲染管理器：排序分发、阴影水态、双来源描边、幂等和池生命周期")

test.system("rendering", "weather_screen_context", {}, function(data)
    assert(not data.missingPlayer and not data.inactivePlayer and not data.remotePlayer and data.localPlayer,
        "天气效果没有正确识别当前活动本地玩家")
    assert(data.missingModule and data.resolvedCamera and data.changedCamera,
        "天气效果没有获取玩家相机或跟踪相机替换")
    assert(not data.missingCamera and data.orthographicCamera and not data.perspectiveCamera
        and not data.disabledCamera and not data.inactiveCamera,
        "天气效果没有正确识别可用正交相机")
    assert(data.ownCamera and data.stackCamera and not data.nonFinalCamera and not data.unrelatedCamera
        and not data.overlaySource and not data.removedCamera,
        "天气效果没有正确识别本地相机栈的最终目标")
end, "天气效果：本地玩家与最终相机栈")

for _, quads in ipairs({1, 2, 4096}) do
    local count = quads
    test.system("rendering", "shadow_quad_indices", {quads = count}, function(data)
        assert(data.vertexCount == count * 4 and data.indexCount == count * 6 and data.uint16,
            "阴影四边形批次容量或16位格式改变")
        local pattern = {0, 1, 2, 0, 2, 3}
        local lastBase = (count - 1) * 4
        assert(#data.first == 6 and #data.last == 6, "阴影批次没有返回完整首尾三角形")
        for index, vertex in ipairs(pattern) do
            assert(data.first[index] == vertex and data.last[index] == lastBase + vertex,
                "阴影四边形绕序、首尾偏移或16位边界改变")
        end
    end, "阴影四边形索引：" .. count)
end

test.system("rendering", "water_surface_palette", {}, function(data)
    assert(#data.materials == 3, "缺少 Tilemap、Mesh 或 BRG 正式水面材质")
    local limits = { _ReflectionStrength = 0.35, _SpecularStrength = 0.30, _RippleStrength = 0.28,
        _WhitecapStrength = 0.18, _ShoreFoamStrength = 0.30 }
    for _, material in ipairs(data.materials) do
        local colors, strengths = material.colors, material.strengths
        local deep, shallow, foam = colors._DeepColor, colors._ShallowColor, colors._FoamColor
        assert(material.shaderSupported and material.passCount >= 2 and material.litPass >= 0 and material.forwardPass >= 0,
            "水面 Shader 不可用或缺少光照 Pass：" .. material.name)
        assert(deep[1] < deep[2] and deep[3] > deep[2] * 0.7 and deep[3] < deep[2] * 1.35,
            "深水没有保持自然灰青色：" .. material.name)
        assert(shallow[1] < shallow[2] and math.abs(shallow[3] - shallow[2]) < 0.16 and
            shallow[1] + shallow[2] + shallow[3] > deep[1] + deep[2] + deep[3] + 0.2,
            "浅水没有比深水更亮或偏色过强：" .. material.name)
        assert(math.min(foam[1], foam[2], foam[3]) > 0.65 and
            math.max(foam[1], foam[2], foam[3]) - math.min(foam[1], foam[2], foam[3]) < 0.16,
            "泡沫偏色过强或过暗：" .. material.name)
        for property, limit in pairs(limits) do
            assert(strengths[property] > 0 and strengths[property] <= limit,
                "水面反光或泡沫过强：" .. material.name .. property)
            assert(math.abs(strengths[property] - data.materials[1].strengths[property]) < 0.00001,
                "三条水面路径强度不一致：" .. property)
        end
        for property, color in pairs(colors) do
            for channel = 1, 3 do assert(math.abs(color[channel] - data.materials[1].colors[property][channel]) < 0.00001,
                "三条水面路径颜色不一致：" .. property) end
        end
    end
end, "水面材质：灰青深浅色、中性泡沫、克制波光与三路径一致")
