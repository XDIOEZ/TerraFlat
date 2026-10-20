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
test.system("testing", "selection", {}, function(data)
    assert(not data.firstEnabled and data.secondEnabled, "用例选择未按请求执行")
    assert(data.unknownRejected and data.emptyRejected, "无效选择误退回全量测试")
end, "用例选择与未知范围拒绝")

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
