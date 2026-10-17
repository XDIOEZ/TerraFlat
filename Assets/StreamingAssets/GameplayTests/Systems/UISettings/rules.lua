-- 调用偏好正式净化规则，不写 PlayerPrefs 或改变当前 UI。
for _, row in ipairs({
    {text = "1", valid = true, value = 1, custom = 1, target = 1},
    {text = "137", valid = true, value = 137, custom = 137, target = 137},
    {text = "144", valid = true, value = 144, custom = 144, target = 144},
    {text = "1000", valid = true, value = 1000, custom = 1000, target = 1000},
    {text = "0", valid = false, custom = 0, target = 60},
    {text = "1001", valid = false, custom = 1001, target = 60},
    {text = "-1", valid = false, custom = -1, target = 60}, {text = "60.5", valid = false},
    {text = "", valid = false}, {text = "abc", valid = false},
    {text = "999999999999999999", valid = false}
}) do
    test.system("ui_settings", "frame_rate", {text = row.text, custom = row.custom or 137}, function(data)
        local expected = {-1, 30, 40, 45, 50, 60, 72, 75, 90, 100, 120, 144, 165, 180, 200, 240, 300, 360, 480}
        assert(#data.targets == #expected + 1 and #data.ids == #expected + 1, "帧率档位丢失或自定义选项缺失")
        local unique = {}
        for index, rate in ipairs(expected) do
            assert(data.targets[index] == rate, "帧率预设映射错误")
            assert(not unique[data.ids[index]], "帧率选项稳定身份重复")
            unique[data.ids[index]] = true
        end
        assert(data.ids[1] == "unlimited" and data.ids[#data.ids] == "custom", "不限帧与自定义身份错误")
        assert(data.defaultTarget == 60 and data.invalidOptionTarget == 60, "默认帧率或无效选项回退不是60帧")
        assert(data.customTarget == (row.target or 137), "自定义帧率边界或与预设相同的自定义值错误")
        assert(data.parsed == row.valid, "非法帧率输入没有拒绝或合法输入被拒绝")
        if row.valid then assert(data.parsedValue == row.value, "自定义帧率解析值错误") end
    end, "帧率预设与自定义边界：" .. row.text)
end

test.system("ui_settings", "new_game_deferred_content", {}, function(data)
    assert(data.initialWorldChildren == 0 and data.initialDifficultyChildren == 0,
        "名称页首次显示前已经实例化隐藏世界或难度内容")
    assert(not data.initialHasWorld and not data.initialHasDifficulty, "未访问页面错误登记为已创建")
    assert(data.firstWorldCreated and data.difficultyStillUncreated, "世界页首次访问没有单独创建自己的内容")
    assert(not data.repeatWorldCreated and data.worldInstancePreserved and data.worldStatePreserved,
        "世界页重复访问重建实例或丢失已有控件状态")
    assert(data.firstDifficultyCreated and not data.repeatDifficultyCreated,
        "难度页没有遵循首次创建、以后复用的规则")
    assert(data.worldChildren == 1 and data.difficultyChildren == 1,
        "隐藏页面重复创建了内容")
    assert(data.cachedWorldButtonPreserved and data.difficultyButtonRegistered,
        "动态页面没有刷新所属面板的控件缓存或替换了已绑定控件")
    assert(data.instancesOwnedByPanel and data.childrenDestroyedWithPanel,
        "按需内容脱离所属面板或面板销毁后残留")
end, "新世界隐藏页面首次按需创建、重复复用与面板释放")

for _, row in ipairs({
    {mode = "empty", count = 0, max = 0, ticks = 0, names = {}},
    {mode = "mixed", count = 4, max = 1, ticks = 300, names = {"Newest", "Middle", "Old", "Missing"}},
    {mode = "ties", count = 4, max = 1, ticks = 200, names = {"Alpha", "beta", "Gamma", "zeta"}}
}) do
    test.system("ui_settings", "save_file_catalog", {mode = row.mode}, function(data)
        assert(data.count == row.count and data.readCount == row.count and data.maxReadsPerPath == row.max,
            "存档列表排序重复读取了同一文件的元数据")
        assert(data.firstTicks == row.ticks and #data.names == #row.names,
            "存档列表元数据缺失或条目数量错误")
        for index, name in ipairs(row.names) do
            assert(data.names[index] == name, "存档列表没有按时间倒序及同时间名称顺序排列")
        end
    end, "存档目录一次采样后排序：" .. row.mode)
end

for _, row in ipairs({ { size = -1, expected = 0.5 }, { size = 0.5, expected = 0.5 }, { size = 1.25, expected = 1.25 }, { size = 8, expected = 2 }, { nan = true, expected = 1 }, { infinity = true, expected = 1 } }) do
    test.system("ui_settings", "mobile_size", row, function(data)
        assert(data.size == row.expected, "移动控件倍率没有按正式范围或异常默认值处理")
    end, "UI 独立控件倍率：" .. row.expected)
end

for _, row in ipairs({
    {mode = "inside", allowed = false, rootInside = true},
    {mode = "outside", allowed = true, rootInside = true},
    {mode = "edge_inside", allowed = false, rootInside = true},
    {mode = "edge_outside", allowed = true, rootInside = true},
    {mode = "fallback_inside", allowed = false, rootInside = true},
    {mode = "fallback_outside", allowed = true, rootInside = false},
    {mode = "camera_inside", allowed = false, rootInside = true},
    {mode = "camera_outside", allowed = true, rootInside = true},
    {mode = "nan", allowed = false},
    {mode = "infinity", allowed = false}
}) do
    test.system("ui_settings", "held_item_world_drop_position", {mode = row.mode}, function(data)
        assert(data.canDrop and data.canDropAtPoint == row.allowed,
            "窗口内部透明空白、外部空白或非法屏幕坐标的丢弃门禁错误")
        if row.rootInside ~= nil then
            assert(data.insidePanelRoot == row.rootInside, "窗口边界夹具没有正确区分全屏根和实际窗口")
        end
        assert(data.usesActualWindow == (row.mode ~= "fallback_inside" and row.mode ~= "fallback_outside"),
            "正式窗口矩形或缺失拖动面时的保守回退没有参与采样")
    end, "窗口外手持丢弃屏幕边界：" .. row.mode)
end

-- 原生 Show 生成全部选项，Lua 只验证正式组件算出的视口和滚动结果。
local function assert_dropdown_sample(sample, count)
    local visible = math.min(count, 5)
    assert(sample.optionCount == count and sample.activeItemCount == count, "下拉列表丢失或多生成了选项")
    assert(math.abs(sample.rowHeight - 60) < 0.01 and sample.allRowsSameHeight, "下拉条目实际高度没有统一到模板行高")
    assert(math.abs(sample.viewportHeight - visible * 60) < 0.01, "下拉列表没有完整显示最多五行")
    assert(math.abs(sample.contentHeight - count * 60) < 0.01, "下拉滚动内容高度没有保留全部选项")
    assert(sample.firstFullyVisible, "下拉列表首行被裁切或初始滚动位置错误")
    assert(sample.lastInitiallyVisible == (count <= 5), "下拉列表末行可见性不符合五行上限")
    assert(sample.lastVisibleAtBottom, "滚动到底部仍无法完整看到最后一项")
    assert(sample.layoutDisabled and sample.fitterDisabled, "下拉列表仍有第二个布局组件争写条目位置")
end

for _, kind in ipairs({ "tmp", "legacy" }) do
    for _, count in ipairs({ 1, 3, 5, 6, 10 }) do
        test.system("ui_settings", "dropdown_layout", { kind = kind, count = count }, function(data)
            assert(data.kind == kind and #data.samples == 1, "下拉采样类型或次数错误")
            assert_dropdown_sample(data.samples[1], count)
            assert(data.layoutComponentCount == 1 and data.originalSelectionPreserved, "下拉布局重复安装或污染了原界面焦点")
        end, "下拉完整行数与末项滚动：" .. kind .. "/" .. count)
    end

    test.system("ui_settings", "dropdown_layout", { kind = kind, counts = { 10, 1, 6, 3, 5 } }, function(data)
        local counts = { 10, 1, 6, 3, 5 }
        assert(#data.samples == #counts, "下拉选项变化后未完成全部重复展开")
        for index, count in ipairs(counts) do assert_dropdown_sample(data.samples[index], count) end
        assert(data.layoutComponentCount == 1 and data.originalSelectionPreserved, "重复展开安装多份布局或污染了原界面焦点")
    end, "下拉重复展开适应选项增减：" .. kind)
end

test.system("ui_settings", "panel_lifecycle", { alpha = 0.75 }, function(data)
    assert(data.initialAlpha == 0 and not data.initialInteraction, "面板关闭初始化仍可见或能接收输入")
    assert(data.openAlpha == 0.75 and data.openInteraction and data.openEvents == 1, "重复打开误发事件或丢失指定透明度")
    assert(data.closedAlpha == 0 and not data.closedInteraction and data.closeEvents == 1, "重复关闭误发事件或没有清理交互")
    assert(not data.inactiveBlocksGameplay, "停用测试面板仍锁定玩法")
end, "独立面板初始化、开关幂等与交互状态")

-- 无 Canvas 的自有面板通过正式开关与能力接口采样，不创建或覆盖 UIManager 单例。
local held_drop_panels = {
    {mode = "empty", canDrop = true, owner = false, label = "无打开面板"},
    {mode = "allowed", canDrop = true, owner = true, label = "授权制作或库存面板"},
    {mode = "denied", canDrop = false, owner = false, label = "默认模态保持拒绝"},
    {mode = "nonblocking_denied", canDrop = true, owner = false, label = "普通HUD不制造额外阻断"},
    {mode = "nonblocking_allowed", canDrop = true, owner = true, label = "非模态授权面板"},
    {mode = "stacked", canDrop = false, owner = true, label = "叠加未授权模态仍阻断"},
    {mode = "closed", canDrop = true, owner = false, label = "关闭后锁持有者失去资格"},
    {mode = "denied_closed", canDrop = true, owner = false, label = "拒绝面板关闭后不再阻断"},
    {mode = "disabled", canDrop = true, owner = false, label = "禁用组件后锁持有者失去资格"},
    {mode = "denied_disabled", canDrop = true, owner = false, label = "拒绝面板禁用后不再阻断"},
    {mode = "inactive", canDrop = true, owner = false, label = "停用节点后锁持有者失去资格"},
    {mode = "revoked", canDrop = false, owner = false, label = "撤销能力同时清理锁持有者"},
    {mode = "owner_changed", canDrop = true, owner = false, nextOwner = true, label = "更换持有者立即淘汰旧引用"},
    {mode = "destroyed_owner", canDrop = true, owner = false, label = "销毁持有者不能继续获得兼容资格"},
    {mode = "destroyed_panel", canDrop = true, owner = false, label = "销毁面板清除授权"},
    {mode = "no_owner", canDrop = true, owner = false, label = "无持有者能力不放行任意输入锁"},
    {mode = "reopened", canDrop = true, owner = true, label = "关闭再开重新获得资格"},
    {mode = "shared_owner", canDrop = true, owner = true, label = "共享持有者仍有另一授权面板"},
    {mode = "idempotent", canDrop = true, owner = true, label = "重复授权不重发交互通知"}
}
for _, row in ipairs(held_drop_panels) do
    test.system("ui_settings", "held_item_world_drop", {mode = row.mode}, function(data)
        assert(not data.defaultAllows, "新面板默认开放了世界丢弃")
        assert(data.canDrop == row.canDrop and data.ownerCompatible == row.owner,
            "面板开关、模态叠加或能力变化没有更新世界丢弃资格")
        assert(data.nextOwnerCompatible == (row.nextOwner == true)
            and not data.unknownOwnerCompatible and not data.nullOwnerCompatible,
            "未授权、旧引用或空输入锁持有者被放行")
        if row.mode == "revoked" then assert(data.ownerCleared, "撤销授权后残留锁持有者引用") end
        if row.mode == "reopened" then
            assert(data.closedCanDrop and not data.closedOwnerCompatible, "关闭到重开之间仍沿用旧授权")
        end
        if row.mode == "idempotent" then
            assert(data.idempotentNotificationDelta == -1 or data.idempotentNotificationDelta == 0,
                "同一能力和持有者重复设置仍触发全局刷新")
        end
    end, "窗口外手持丢弃资格：" .. row.label)
end

-- 独立内存 Provider 走正式注册与会话，只采样恢复调用及结果，不操作玩家窗口。
local function assert_control_calls(data, toggle, slider, dropdown, switch)
    assert(data.toggleCalls == toggle and data.sliderCalls == slider
        and data.dropdownCalls == dropdown and data.switchCalls == switch,
        "设置会话恢复了未改字段或漏掉已改字段")
end

for _, mode in ipairs({ "unchanged", "changed", "roundtrip", "mixed" }) do
    test.system("ui_settings", "edit_session_values", { mode = mode }, function(data)
        local dirty = mode == "changed" or mode == "mixed"
        assert(data.nestedSessionRejected, "重复开始覆盖了已有会话")
        assert(data.changed == dirty and data.attempted == dirty, "无变化关闭或改后改回仍执行恢复")
        assert(data.restored and data.error == nil and not data.toggle and data.slider == 0
            and data.dropdown == 0 and data.switch == 0, "关闭没有恢复打开时的基线")
        assert_control_calls(data, dirty and 1 or 0, mode == "changed" and 1 or 0,
            dirty and 1 or 0, mode == "changed" and 1 or 0)
        assert(data.secondDiscardSucceeded and not data.secondAttempted, "重复关闭再次触发恢复")
    end, "设置会话按变化字段恢复：" .. mode)
end

for _, modify in ipairs({ false, true }) do
    test.system("ui_settings", "edit_session_commit", { modifyAfterCommit = modify }, function(data)
        local count = modify and 1 or 0
        assert(data.committedProviders >= 1 and data.cleanAfterCommit, "保存未更新设置会话基线")
        assert(data.restored and data.attempted == modify and data.error == nil and data.toggle
            and data.slider == 2 and data.dropdown == 1 and data.switch == 1 and data.hidden == 7,
            "关闭没有恢复到最近保存的控件与补充状态，或保存后无修改仍执行恢复")
        assert_control_calls(data, count, count, count, count)
        assert(data.participantCalls == count, "保存后的补充状态恢复次数错误")
    end, "保存建立新基线后关闭：继续修改=" .. tostring(modify))
end

for _, mode in ipairs({ "unchanged", "changed", "untracked" }) do
    test.system("ui_settings", "edit_session_participants", { mode = mode }, function(data)
        local restore = mode ~= "unchanged"
        assert(data.changed == (mode == "changed") and data.attempted == restore,
            "补充状态变化检测或无检测器的保守恢复错误")
        assert(data.restored and data.error == nil and data.hidden == 3
            and data.participantCalls == (restore and 1 or 0), "补充状态恢复次数或结果错误")
        assert_control_calls(data, 0, 0, 0, 0)
    end, "补充状态按检测能力恢复：" .. mode)
end

for _, mode in ipairs({ "write", "read", "tracker" }) do
    test.system("ui_settings", "edit_session_errors", { mode = mode }, function(data)
        assert(not data.restored and data.attempted and data.sessionEnded and data.error ~= nil,
            "恢复失败没有报告尝试写入、收集错误或结束会话")
        assert(data.broken.slider == 0 and data.broken.switch == 0 and data.broken.switchCalls == 1,
            "前序字段异常阻止了后续字段恢复")
        assert(not data.survivor.toggle and data.survivor.toggleCalls == 1,
            "前序 Provider 异常阻止了后续 Provider 恢复")
        if mode == "write" then
            assert(string.find(data.error, "probe-toggle", 1, true)
                and string.find(data.error, "probe-dropdown", 1, true)
                and string.find(data.error, "probe-participant", 1, true), "未收集多处恢复失败")
            assert(data.broken.participantCalls == 1, "字段失败后遗漏补充状态恢复")
        elseif mode == "read" then
            assert(string.find(data.error, "probe-getter", 1, true)
                and data.broken.toggleCalls == 0 and data.broken.participantCalls == 1,
                "变化读取异常被吞掉或阻止补充状态恢复")
        else
            assert(string.find(data.error, "probe-tracker", 1, true)
                and data.broken.participantCalls == 0 and data.broken.hidden == 9,
                "补充状态检测异常没有收集或仍执行了未确认的恢复")
        end
    end, "设置恢复异常后继续收集与恢复：" .. mode)
end

-- 真实输入资产和内存存储覆盖默认绑定、已有覆盖两种打开基线。
for _, existing in ipairs({ false, true }) do
    for _, mode in ipairs({ "unchanged", "modified", "reverted", "committed" }) do
        test.system("ui_settings", "input_binding_session", { mode = mode, existingOverride = existing }, function(data)
            local dirty = mode == "modified"
            local edits = mode == "unchanged" and 0 or (mode == "reverted" and 2 or 1)
            assert(data.existingOverride == existing and data.mode == mode
                and data.hasChangesBeforeDiscard == dirty, "按键会话变化检测或基线场景错误")
            assert(data.finalJson == data.baselineJson and not data.hasChangesAfterDiscard
                and not data.sessionActiveAfterDiscard, "关闭没有恢复最近保存的按键覆盖或结束会话")
            assert(data.persistedJson == data.baselineJson, "放弃修改污染了最近保存的按键覆盖")
            assert(data.editNotificationCount == edits and data.discardNotificationCount == (dirty and 1 or 0)
                and data.repeatedDiscardNotificationCount == 0, "按键未改、改回或重复关闭仍广播重建")
            assert(data.loadCount == 1 and data.saveCount + data.clearCount == (mode == "committed" and 1 or 0)
                and data.discardStoreCallCount == 0 and data.repeatedDiscardStoreCallCount == 0,
                "放弃按键修改重复访问存储或保存未按正式提交路径执行")
        end, "按键会话跳过无变化恢复：" .. tostring(existing) .. "/" .. mode)
    end
end

for _, row in ipairs({
    { kind = "button", root = "ProbePanel", excluded = false },
    { kind = "scrollbar", root = "ProbePanel", excluded = true },
    { kind = "slider", root = "ProbePanel", excluded = true },
    { kind = "slider", root = "UI_AudioSettings", excluded = false },
    { kind = "button", root = "UI_HotBar", excluded = true },
    { kind = "slider", root = "UI_HotBar", excluded = true }
}) do
    test.system("ui_settings", "navigation_policy", row, function(data)
        assert(data.excluded == row.excluded, "正式手柄导航策略没有区分设置、显示控件与 HUD")
    end, "UI 手柄焦点策略：" .. row.root .. "/" .. row.kind)
end
