-- 第一份Lua留下自有清理租约，收尾后由下一份Lua核对。
local token
test.check("world_ready", "测试宿主的真实世界就绪")
test.runtime("testing", "begin_lease", {}, function(d)
    token = d.token
    assert(type(token) == "string" and #token > 0, "没有创建测试租约")
    assert(not d.cleaned and not d.asyncCleaned, "租约提前释放")
    assert(type(d.controlOwner) == "string" and #d.controlOwner > 0, "真实玩家没有控制租约")
end, "登记本Lua的同步和跨帧收尾")
test.wait(.1)
for expected = 1, 2 do
    local index = expected
    test.runtime("testing", "sample_lease", {}, function(d)
        assert(d.token == token and d.sample == index, "命令顺序或回调状态没有保留")
        assert(d.elapsed >= .1 and d.frames >= 1, "等待没有实际跨帧")
        assert(d.profilePreserved and not d.cleaned and not d.asyncCleaned, "租约在Lua结束前丢失")
    end, "验证实际命令队列采样" .. index)
end

-- 在真实帧持续发出自有标记，验证性能 JSON 的选帧和调用次数。
for _, mode in ipairs({"profiler_unclosed", "profiler_stop_only", "profiler_double_start", "profiler_in_rules"}) do
    test.system("testing", "lua_contract", {mode = mode}, function(d)
        assert(not d.succeeded and d.failed > 0, "无效性能采集顺序或规则模式被误记通过")
    end, "性能采集编排边界：" .. mode)
end
test.runtime("testing", "profiler_marker", {}, function(d)
    assert(d.created and d.marker == "FlatWorld.GameplayTest.ProfilerEvidence", "性能自检标记未创建")
end, "建立跨帧性能标记")
test.profiler_start(30, "开始真实动作性能采集")
test.move(1, 0, 1)
test.profiler_stop("FlatWorld.GameplayTest.ProfilerEvidence", function(d)
    assert(d.state == "completed" and d.summary.validFrameCount > 0 and d.selectedDisplayFrame > 0,
        "没有导出本轮有效性能帧")
    assert(d.callTreeScope == "selected-frame-only" and d.selectionMode == "newest-frame-containing-marker" and
        d.selectedTreeNodeCount > 0 and d.selectedMarkerCalls > 0,
        "导出的实际调用树没有选中指定标记")
    assert(type(d.analysisPath) == "string" and string.find(d.analysisPath, "profiler-analysis.json", 1, true) and
        type(d.captureSessionPath) == "string" and string.find(d.captureSessionPath, "capture-session.json", 1, true),
        "性能 JSON 或采集过程文件没有发布")
    assert(d.settingsRestored, "性能采集后没有恢复原录制设置")
end, "导出含实际 Marker 的性能 JSON")
test.runtime("testing", "contract_classification", {}, function(data)
    local expected = {"behavior", "architecture", "configuration", "mixed"}
    assert(data.legacyKind == "unspecified", "旧清单没有保留未分类状态")
    assert(#data.kinds == #expected and data.unknownRejected, "契约类别丢失或接受了未知类别")
    assert(data.noOracleRejected == 5 and data.resultCommandAccepted and data.profilerOracleAccepted,
        "纯准备脚本被误判为结果验证，或有效移动/性能断言被拒绝")
    for i, kind in ipairs(expected) do assert(data.kinds[i] == kind, "契约类别解析不一致") end
end, "案例契约分类保持旧格式兼容且不接受未知类别")
