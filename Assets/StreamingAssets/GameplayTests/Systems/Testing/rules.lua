for _, invalid in ipairs({"unknown_field", "execution_mode", "showroom", "duplicate_id", "escape_path", "reserved_id", "disabled"}) do
    test.system("testing", "suite_contract", {invalid = invalid}, function(data)
        assert(data.validMode == "rules" and data.validCount == 1, "合法规则清单被拒绝")
        assert(data.invalidRejected, "错误清单没有在执行前阻断")
    end, "清单契约保护：" .. invalid)
end
test.system("testing", "lua_contract", {mode = "valid"}, function(data)
    assert(data.succeeded and data.failed == 0 and data.samples == 1 and data.commands == 1, "参数快照没有冻结或有效规则未执行")
end, "Lua 参数冻结及真实规则执行")
for _, mode in ipairs({"no_assert", "failed_assert", "callback_enqueue", "no_cs", "cycle", "unknown", "log_only", "live_in_rules"}) do
    test.system("testing", "lua_contract", {mode = mode}, function(data)
        assert(not data.succeeded and data.failed > 0, "没有断言、错误断言或越界操作被误记通过")
    end, "Lua 沙箱与通过判定：" .. mode)
end
test.system("testing", "bootstrap_scene_cleanup", {}, function(data)
    assert(data.saved and data.hadMeta and data.cleaned and data.deleted >= 1,
        "临时场景夹具未落盘，或空闲清理没有执行")
    assert(data.orphanDeleted and data.orphanMetaDeleted, "临时测试场景或 meta 没有清理")
    assert(data.openedPreserved and data.activeScenePreserved, "清理影响了已打开场景或活动场景")
    assert(data.lookalikePreserved and data.nestedPreserved and data.recognized and data.invalidRejected,
        "临时场景识别越界，误清理了近似命名或非根目录场景")
    assert(data.repeatedNoChange, "重复清理产生了额外删除")
end, "测试引导场景残留、meta 清理及打开场景保留")

test.system("testing", "selection", {}, function(data)
    assert(not data.firstEnabled and data.secondEnabled, "用例选择未按请求执行")
    assert(data.unknownRejected and data.emptyRejected, "无效选择误退回全量测试")
end, "用例选择与未知范围拒绝")

test.system("testing", "cleanup_failure_stop", {}, function(data)
    for _, key in ipairs({"asyncReason", "spaceReason", "syncReason"}) do
        local reason = data[key]
        assert(type(reason) == "string" and string.find(reason, "dirty_case", 1, true),
            "现场清理失败后没有标明来源并停止后续用例")
    end
    assert(data.successfulCleanupContinues and data.ordinaryFailureContinues and data.screenshotFailureContinues,
        "成功清理或非现场恢复失败错误地中断了后续用例")
end, "清理失败禁止污染后续用例")

for _, mode in ipairs({"success", "create_failure", "advance_failure"}) do
    local expected = mode
    test.system("testing", "cleanup_callback", {mode = expected}, function(data)
        assert(data.started and data.asyncCleaned and data.syncCleaned and data.asyncBeforeSync,
            "清理前回调未执行，或失败后没有按异步/同步顺序清理")
        assert(not data.followingSkipped, "截图回调失败但现场恢复成功时错误跳过后续用例")
        if expected == "create_failure" then
            assert(not data.completed and data.callbackFailures == 1, "回调创建异常未记失败")
        else
            assert(data.completed and data.preservedLease, "回调跨帧期间现场提前清理")
            assert(data.callbackFailures == (expected == "advance_failure" and 1 or 0), "回调迭代异常未准确记失败")
        end
    end, "结束截图回调与异常清理：" .. expected)
end

for _, mode in ipairs({"async_cleanup_failure", "sync_cleanup_failure"}) do
    test.system("testing", "cleanup_callback", {mode = mode}, function(data)
        assert(data.started and data.completed and data.asyncCleaned and data.syncCleaned and data.asyncBeforeSync,
            "失败清理没有继续执行其余现场收尾")
        assert(data.callbackFailures == 0 and data.followingSkipped,
            "真实执行器清理失败没有阻止后续案例")
    end, "真实清理失败中断后续案例：" .. mode)
end

test.system("testing", "gm_preference_player", {}, function(data)
    assert(not data.nullIndexHasPlayer and not data.emptyHasPlayer and not data.remoteOnlyHasPlayer,
        "GM 偏好恢复在没有本地玩家时误选目标")
    assert(data.remoteFirstProfile == "gm_local" and data.localFirstProfile == "gm_local"
        and data.staleEntriesProfile == "gm_local", "GM 偏好恢复被远程玩家、登记顺序或失效条目干扰")
    assert(data.selectedInactive and not data.selectedInitialized,
        "玩家模块尚未激活或尚未 Load 时丢失了本地档案资格")
end, "GM 偏好恢复只读取已登记本地玩家身份")

test.system("testing", "gameplay_gateway", {}, function(data)
    assert(#data.publicTools == 1 and data.publicTools[1] == "gameplay" and data.onlyTwoParameters,
        "玩法详细接口仍默认公开，或短入口展开了额外参数")
    assert(data.internalOperationsHidden and data.legacyHandlerRegistered,
        "隐藏玩法接口仍通过元数据公开，或旧命令路由被删除")
    assert(data.catalogHasNoContracts and data.schemaUsesLiveMetadata,
        "默认目录展开全部契约，或按需契约没有沿用真实操作元数据")
    local profiler = data.profilerContract
    assert(data.profilerHidden and data.profilerInDiagnostics and data.profilerHandlerRegistered
        and data.profilerSchemaUsesLiveMetadata and profiler.operation == "profiler" and profiler.domain == "diagnostics",
        "性能采集入口未隐藏、未登记诊断目录/路由，或按需契约未沿用真实元数据")
    local properties = profiler.inputSchema.properties
    assert(profiler.defaultOutput == "compact" and properties.action.default == "status",
        "性能采集契约的默认操作或输出模式变化")
    for _, action in ipairs({"status", "start", "stop", "cancel", "export"}) do
        assert(string.find(properties.action.description, action, 1, true), "性能采集契约遗漏操作：" .. action)
    end
    assert(properties.seconds.type == "number" and properties.seconds.default == 10
        and properties.enableStatistics.type == "boolean" and properties.enableStatistics.default == true
        and properties.frame.type == "string" and properties.frame.default == "latest"
        and properties.displayFrame.type == "integer" and properties.contextFrameRadius.type == "integer"
        and properties.contextFrameRadius.default == 30,
        "性能采集契约遗漏时长、统计开关、选帧或上下文半径参数")
    assert(data.unknownRejected and data.malformedRejected and data.unknownDomainRejected,
        "网关没有在执行前拒绝未知操作、错误参数或未知能力域")
    assert(data.publicMetadataChars > 0 and data.publicMetadataChars < data.internalMetadataChars,
        "常驻玩法入口元数据没有缩小")
end, "玩法MCP短入口、按需契约、白名单与旧路由保留")

test.system("testing", "world_targets", {}, function(data)
    assert(data.valid and data.crossPlayerRejected and data.foreignNoBackendAccess and data.unknownRejected,
        "统一世界目标未校验玩家身份，或外来句柄触碰了目标后端")
    assert(data.expiredRejected and data.expiredCannotRevive and data.bounded,
        "失效实体句柄恢复可用，或句柄缓存未按容量淘汰")
    assert(data.invalidRadiusRejected, "世界查询未结构化拒绝非法、非有限或非正半径")
end, "玩法MCP统一目标身份、失效不可复活、缓存上限及查询参数")
