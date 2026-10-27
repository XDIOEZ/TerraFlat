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
