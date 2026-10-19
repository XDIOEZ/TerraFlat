-- 只采样水体设置元数据，不读取当前选项、不写偏好或切换现场材质。
test.system("ui_settings", "water_visual_provider", {}, function(data)
    assert(data.providerId == "water-visuals" and data.switchCount == 0,
        "水体风格没有保留稳定 Provider ID，或仍暴露按钮切换")
    assert(#data.dropdowns == 1, "水体风格应统一暴露一个下拉列表")
    local dropdown = data.dropdowns[1]
    assert(dropdown.key == "waterVisuals.style" and dropdown.controlType == "Dropdown",
        "水体风格稳定设置键或下拉控件类型错误")
    assert(type(dropdown.label) == "string" and dropdown.label:match("%S"),
        "水体下拉列表缺少可见名称")
    local ids = {"stylized", "realistic", "unity-stylized-water", "low-spec"}
    assert(#dropdown.options == #ids and data.lowSpecIndex == 3,
        "水体风格应包含四项，轻量风格须使用索引 3")
    local seen = {}
    for index, expected in ipairs(ids) do
        local option = dropdown.options[index]
        assert(option.id == expected and not seen[option.id],
            "水体风格选项稳定 ID、顺序或唯一性错误")
        assert(type(option.label) == "string" and option.label:match("%S"),
            "水体风格选项缺少可见名称")
        seen[option.id] = true
    end
end, "水体风格统一下拉、四种稳定选项与轻量风格索引")

-- 用正式诊断接口检查计数与代际，不执行玩法或写入玩家偏好。
test.system("ui_settings", "performance_debug", {}, function(data)
    assert(data.disabled.calls == 0 and data.disabled.work == 0 and not data.disabled.hasGauge,
        "关闭性能统计时仍收集次数、工作量或规模")
    assert(data.aggregated.calls == 3 and data.aggregated.work == 14,
        "嵌套、异常退出或批次追加的次数与工作量没有真实聚合")
    assert(data.aggregated.gauge == 2 and data.aggregated.peakGauge == 5,
        "当前规模与峰值规模混成了累计工作量")
    assert(data.aggregated.totalMs >= data.aggregated.maxMs and data.aggregated.maxMs >= 0,
        "包含式计时聚合非法")
    assert(data.cleared.calls == 0 and data.cleared.work == 0 and not data.cleared.hasGauge,
        "清空前开始的作用域污染了清空后的数据")
    assert(data.afterClear.calls == 1 and data.afterClear.work == 1,
        "清空统计错误关闭了当前开关或丢失点位")
    assert(data.resetDisabled and data.staleReset.calls == 0 and data.staleReset.work == 0 and not data.staleReset.hasGauge,
        "会话重置没有关闭开关或接收了旧作用域")
    assert(data.registrationRetained and data.background.calls == 2 and data.background.work == 12,
        "重置丢失静态点位或后台计数不完整")
end, "性能诊断关闭旁路、嵌套异常聚合、当前与峰值规模、跨清空与重置的旧作用域和后台计数")

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

test.system("ui_settings", "tent_veil", {}, function(data)
    assert(data.red == 0 and data.green == 0 and data.blue == 0 and data.alpha == 1 and data.panelAlpha == 1,
        "正式睡眠遮罩没有配置为完全不透明的纯黑")
    assert(data.plainImage and data.fullScreen, "睡眠遮罩仍依赖带透明边缘的图片，或没有覆盖全屏")
    assert(data.animationCount == 0, "睡眠遮罩仍叠加通用动画，与帐篷淡出争写")
end, "睡眠正式遮罩纯黑、全屏与动画所有权")

test.system("ui_settings", "panel_lifecycle", { alpha = 0.75 }, function(data)
    assert(data.initialAlpha == 0 and not data.initialInteraction, "面板关闭初始化仍可见或能接收输入")
    assert(data.openAlpha == 0.75 and data.openInteraction and data.openEvents == 1, "重复打开误发事件或丢失指定透明度")
    assert(data.closedAlpha == 0 and not data.closedInteraction and data.closeEvents == 1, "重复关闭误发事件或没有清理交互")
    assert(not data.inactiveBlocksGameplay, "停用测试面板仍锁定玩法")
end, "独立面板初始化、开关幂等与交互状态")

-- 无 Canvas 的自有面板通过正式开关与能力接口采样，不创建或覆盖 UIManager 单例。
test.system("ui_settings", "held_item_world_drop", {mode = "gameplay_windows"}, function(data)
    assert(#data.windows == 3 and data.allOpen, "背包、手工与工作台窗口不能同时保持打开")
    for _, window in ipairs(data.windows) do
        assert(window.open and not window.blocking and not window.hasLockOwner,
            "通用玩法窗口仍阻断输入或登记了额外锁持有者")
        assert(window.navigation and window.cancelTarget and window.allowsWorldDrop,
            "通用玩法窗口丢失导航、返回关闭或窗口外丢弃能力")
    end
    assert(data.openEvents == 3 and data.closeEvents == 1, "重复配置或重复开关误发事件")
    assert(data.canDropWithWindows and data.otherWindowsStayOpen and data.closedWindowStopsCancel
        and data.reopenedWithoutBlocking, "关闭或重开一个窗口影响了其它窗口或重新锁定玩法")
    assert(data.modalBlocksGameplay and data.modalBlocksDrop and data.modalCloseRestoresWindows,
        "模态界面未保持独立阻断，或关闭模态后未恢复普通窗口")
end, "背包、手工与工作台共用非模态窗口规则")

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
-- 三个甩动控件通过正式 Provider 与设置会话写入，采样结束恢复玩家偏好。
for _, row in ipairs({
    {speedPercent = 25, distancePercent = 200, speed = 25, distance = 200},
    {speedPercent = 10, distancePercent = 999, speed = 25, distance = 200},
    {speedPercent = 73.6, distancePercent = 124.4, speed = 74, distance = 124}
}) do
    test.system("ui_settings", "flick_settings_provider", row, function(data)
        assert(data.providerId == "inventory_flick" and data.registered and data.sliderCount == 2
            and data.toggleCount == 1, "甩动 Provider 未正确注册两个滑条和一个开关")
        assert(data.speed.key == "inventory_flick.speedPercent" and data.distance.key == "inventory_flick.distancePercent"
            and data.angleKey == "inventory_flick.angleLimit" and data.angleDefault,
            "甩动设置的稳定键或默认角度开关错误")
        for _, slider in ipairs({data.speed, data.distance}) do
            assert(slider.kind == "Slider" and slider.category == "controls" and slider.value == 100
                and slider.min == 25 and slider.max == 200 and slider.step == 1,
                "甩动滑条默认、范围、步长或操作页元数据错误")
        end
        assert(data.afterSpeed.speedPercent == row.speed and data.afterSpeed.distancePercent == 100
            and data.afterSpeed.angleLimit, "速度滑条影响了距离或角度偏好")
        assert(data.afterDistance.speedPercent == row.speed and data.afterDistance.distancePercent == row.distance
            and data.afterDistance.angleLimit, "距离滑条未独立应用或改变角度偏好")
        assert(data.afterAngle.speedPercent == row.speed and data.afterAngle.distancePercent == row.distance
            and not data.afterAngle.angleLimit and data.persisted.speedPercent == row.speed
            and data.persisted.distancePercent == row.distance and not data.persisted.angleLimit,
            "关闭角度限制改变了其他门槛或没有写入正式持久化偏好")
        assert(data.reset.speedPercent == 100 and data.reset.distancePercent == 100 and data.reset.angleLimit
            and data.preferencesRestored, "恢复默认或测试偏好清理没有完成")
    end, "甩动 Provider 三项独立写入：" .. row.speedPercent .. "/" .. row.distancePercent)
end

for _, mode in ipairs({"unchanged", "roundtrip", "cancel", "commit", "reset"}) do
    test.system("ui_settings", "flick_settings_session", {mode = mode}, function(data)
        local dirty = mode == "cancel" or mode == "commit" or mode == "reset"
        local saved = mode == "commit" or mode == "reset"
        local speed = saved and 25 or 100
        local distance = saved and 200 or 100
        assert(data.changed == dirty and data.attempted == dirty and data.discarded and data.error == nil,
            "甩动设置会话没有区分无变化、改回、保存、放弃或恢复默认")
        assert(data.afterDiscard.speedPercent == speed and data.afterDiscard.distancePercent == distance
            and data.afterDiscard.angleLimit == not saved, "放弃修改没有恢复最近保存的三项甩动偏好")
        assert(data.persisted.speedPercent == speed and data.persisted.distancePercent == distance
            and data.persisted.angleLimit == not saved, "设置会话回滚没有同步正式持久偏好")
        assert(data.secondDiscard and not data.secondAttempted and data.preferencesRestored,
            "重复放弃仍写入，或夹具未恢复玩家原始偏好")
        if mode == "reset" then
            assert(data.beforeDiscard.speedPercent == 100 and data.beforeDiscard.distancePercent == 100
                and data.beforeDiscard.angleLimit, "恢复默认没有把三个控件还原为 100%、100%、开启")
        end
    end, "甩动设置保存与放弃：" .. mode)
end

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

-- 高度尺的数值与位置直接采样正式展示规则，严格正高度不能提前舍入为 0。
for _, row in ipairs({
    {height = 0.00000001, text = "<0.01"},
    {height = 0.0049, text = "<0.01"}, {height = 0.01, text = "0.01"},
    {height = 0.1, text = "0.10"}, {height = 1, text = "1.0"}, {height = 10, text = "10"},
    {height = 0, touchdown = true, text = "0"},
    {height = -1, invalid = true, text = "--"},
    {heightMode = "nan", invalid = true, text = "--"},
    {heightMode = "positive_infinity", invalid = true, text = "--"},
    {heightMode = "negative_infinity", invalid = true, text = "--"}
}) do
    test.system("ui_settings", "ship_flight_hud_height", row, function(data)
        assert(data.usable == not row.invalid and data.number == row.text, "高度读数提前归零或无效数值没有安全处理")
        assert(data.normalized >= 0 and data.normalized <= 1, "高度指示超出固定刻度或出现非有限位置")
        if row.touchdown then
            assert(data.normalized == 0, "正式触地后的 0 没有位于刻度底部")
        elseif not row.invalid then
            assert(data.normalized > 0, "未经落地凭证确认，高度指示提前压在 0 刻度")
        end
    end, "起降高度尺数字边界：" .. (row.heightMode or tostring(row.height)) .. "/" .. tostring(row.touchdown))
end

test.system("ui_settings", "ship_flight_hud_sequence", {steps = {
    {time = 0, driving = true, height = 0, verticalSpeed = 0},
    {time = 1, driving = true, height = 0.00000001, verticalSpeed = 2},
    {time = 2, driving = true, height = 5, verticalSpeed = -1}
}}, function(data)
    local s = data.samples
    assert(s[1].visible and s[1].groundContact and s[1].number == "0" and s[1].normalized == 0 and
        not s[1].touchdown and not s[1].nearGround, "起飞地面初态没有显示真实 0，或被误报为新落地完成")
    assert(s[2].visible and not s[2].groundContact and s[2].number == "<0.01" and s[2].normalized > 0,
        "离地后的严格正高度仍停留在 0")
    assert(s[3].visible and s[3].nearGround and not s[3].touchdown, "升空失速下降时没有显示近地提醒")
end, "起飞初始 0、实际离地及升空失速下坠的真实读数")

test.system("ui_settings", "ship_flight_hud_sequence", {steps = {
    {time = 0, driving = true, phase = "SurfaceDescending", height = 4},
    {time = 1, manualExitShip = "hud-ship", driving = true, phase = "SurfaceDescending", height = 4},
    {time = 1000, driving = true, phase = "SurfaceDescending", height = 3},
    {time = 1001, phase = "Landed", ship = "", receipt = {event = "cancelled-flight-landing"}},
    {time = 1002, driving = true, height = 1, verticalSpeed = 2}
}}, function(data)
    local s = data.samples
    assert(s[1].visible and not s[2].visible and not s[3].visible and not s[4].visible and not s[4].touchdown,
        "主动退出后，延迟驾驶快照或紧随其后的落地凭证重新打开了 HUD")
    assert(s[5].visible and not s[5].touchdown, "退出状态确认后重新驾驶仍被旧取消状态屏蔽")
end, "联网主动退出后旧驾驶快照与落地凭证不重放，重新驾驶可恢复")

test.system("ui_settings", "ship_flight_hud_sequence", {steps = {
    {time = 0, driving = true, phase = "SurfaceDescending", height = 4},
    {time = 1, manualExitShip = "hud-ship", driving = true, phase = "SurfaceDescending", height = 4},
    {time = 2, manualEnterShip = "other-ship", driving = true, phase = "SurfaceDescending", height = 3},
    {time = 3, manualEnterShip = "hud-ship", driving = true, phase = "SurfaceDescending", height = 3},
    {time = 4, driving = true, phase = "SurfaceDescending", height = 2},
    {time = 5, phase = "Landed", ship = "", receipt = {event = "resumed-flight-landing"}}
}}, function(data)
    local s = data.samples
    assert(s[1].visible and not s[2].visible and not s[3].visible,
        "主动退出后，其他船的重新驾驶通知错误解除了同船旧快照屏蔽")
    assert(s[4].visible and s[5].visible and not s[4].touchdown and s[5].height == 2,
        "明确重新驾驶本船后没有立即接受驾驶快照，或仍依赖退出快照/超时才能恢复")
    assert(s[6].visible and s[6].touchdown, "恢复本次驾驶后仍拒绝真实的新落地凭证")
end, "主动退出后明确重新驾驶本船立即恢复，其他船通知不能解锁")

test.system("ui_settings", "ship_flight_hud_sequence", {steps = {
    {time = 0, phase = "Landed", receipt = {event = "old-landing"}},
    {time = 1, driving = true, height = 1, verticalSpeed = 2},
    {time = 2, driving = true, height = 25, verticalSpeed = 2},
    {time = 3, driving = true, phase = "SurfaceDescending", height = 20},
    {time = 4, driving = true, phase = "SurfaceDescending", height = 10},
    {time = 5, driving = true, phase = "SurfaceDescending", height = 0.0049},
    {time = 6, driving = true, phase = "SurfaceDescending", height = 0},
    {time = 6.5, phase = "Landed", landed = true, height = 0},
    {time = 7, phase = "Landed", ship = "", receipt = {event = "new-landing"}},
    {time = 8, phase = "Landed", ship = ""},
    {time = 9.1, phase = "Landed", ship = ""},
    {time = 10, phase = "Landed", ship = ""}
}}, function(data)
    local s = data.samples
    assert(#s == 12 and not s[1].visible, "首次观察重放了历史落地提示")
    assert(s[2].visible and s[3].visible and not s[2].touchdown and not s[3].nearGround,
        "起飞时没有显示高度尺或错误显示近地下降警告")
    assert(s[2].maximum == 10 and s[3].maximum == 50 and s[4].maximum == 50 and s[7].maximum == 50,
        "同一次起降的量程没有扩容或在下降时不断收缩")
    assert(s[3].normalized == 0.5 and s[4].normalized == 0.4 and s[5].normalized == 0.2,
        "固定上高下低的刻度没有随着下降向下移动")
    assert(not s[4].nearGround and s[5].nearGround and s[6].nearGround,
        "下降进入低空时没有按正式阈值提示")
    assert(s[6].number == "<0.01" and s[7].number == "<0.01" and not s[7].touchdown and s[7].normalized > 0,
        "高度很小或刚到 0 时提前宣告地面建筑已经完成移交")
    assert(s[8].visible and s[8].groundContact and not s[8].touchdown and s[8].number == "0" and s[8].normalized == 0,
        "真实触地等待建筑移交时没有显示 0，或提前提示飞船已经固定")
    assert(s[9].visible and s[9].touchdown and s[9].height == 0 and s[9].normalized == 0 and s[9].number == "0",
        "清船后的同船新落地凭证没有显示底部 0 与已固定提示")
    assert(s[10].visible and s[10].touchdown and not s[11].visible and not s[12].visible,
        "真实落地提示没有短暂保留后隐藏，或重复凭证重新触发提示")
end, "起飞到下降、同船正式落地、短暂 0 提示与超时隐藏的完整展示状态")

for _, row in ipairs({
    {name = "退出驾驶", interruption = {time = 1, phase = "SurfaceDescending", height = 4}},
    {name = "死亡", interruption = {time = 1, phase = "Landed", alive = false, receipt = {event = "dead-landing"}}},
    {name = "进入太空", interruption = {time = 1, phase = "Cruising", driving = true, height = 200}},
    {name = "跨世界", interruption = {time = 1, phase = "Landed", world = "other-world"}},
    {name = "组件重新启用", interruption = {time = 1, phase = "Landed", reset = true}}
}) do
    test.system("ui_settings", "ship_flight_hud_sequence", {steps = {
        {time = 0, driving = true, phase = "SurfaceDescending", height = 4},
        row.interruption,
        {time = 2, phase = "Landed", ship = "", receipt = {event = "after-interruption"}},
        {time = 3, phase = "Landed", ship = ""}
    }}, function(data)
        local s = data.samples
        assert(s[1].visible and s[1].nearGround and not s[2].visible,
            "退出、死亡、太空或展示重建后仍残留驾驶高度尺")
        assert(not s[2].touchdown and not s[3].visible and not s[4].visible,
            "驾驶跟踪失效后，迟到凭证被误报为本次正式着陆")
    end, "高度 HUD 中断不冒充触地：" .. row.name)
end

for _, row in ipairs({
    {name = "旧事件", receipt = {event = "old-landing"}},
    {name = "其他飞船", receipt = {event = "new-landing", ship = "other-ship"}},
    {name = "其他世界", receipt = {event = "new-landing", world = "other-world"}},
    {name = "缺少事件身份", receipt = {event = ""}}
}) do
    test.system("ui_settings", "ship_flight_hud_sequence", {steps = {
        {time = 0, driving = true, phase = "SurfaceDescending", height = 4, receipt = {event = "old-landing"}},
        {time = 1, phase = "Landed", ship = "", receipt = row.receipt}
    }}, function(data)
        assert(data.samples[1].visible and not data.samples[2].touchdown and not data.samples[2].visible,
            "旧事件、非本船或本世界之外的凭证被误报为地面固定")
    end, "高度 HUD 落地凭证拒绝：" .. row.name)
end

test.system("ui_settings", "ship_flight_hud_sequence", {steps = {
    {time = 0, driving = true, phase = "SurfaceDescending", height = 4},
    {time = 1, phase = "Floating", ship = "", receipt = {event = "water-landing", water = true}}
}}, function(data)
    assert(data.samples[1].visible and data.samples[2].visible and data.samples[2].touchdown and data.samples[2].number == "0",
        "海面完成同样的普通建筑移交后没有显示正式落地提示")
end, "海面成功移交普通建筑后的正式落地提示")

for _, heightMode in ipairs({"nan", "positive_infinity", "negative_infinity"}) do
    test.system("ui_settings", "ship_flight_hud_sequence", {steps = {
        {time = 0, driving = true, phase = "SurfaceDescending", height = 4},
        {time = 1, driving = true, phase = "SurfaceDescending", heightMode = heightMode}
    }}, function(data)
        assert(data.samples[1].visible and not data.samples[2].visible and not data.samples[2].touchdown,
            "非有限高度没有隐藏读数或被误报为已触地")
    end, "高度 HUD 拒绝非有限高度：" .. heightMode)
end

for _, water in ipairs({false, true}) do
    test.system("ui_settings", "ship_flight_hud_receipt_memory", {
        ship = "hud-memory-ship", event = "hud-memory-landing", world = "hud-memory-world", water = water, time = 123.5
    }, function(data)
        assert(data.shipCount == 0 and data.flightCount == 0 and data.passengerCount == 1 and
            data.profileId == "hud-memory-fixture", "清理飞船记录后，正式会话没有保留独立乘员")
        assert(data.valid and data.shipId == "hud-memory-ship" and data.eventId == "hud-memory-landing" and
            data.worldKey == "hud-memory-world" and data.water == water and data.time == 123.5,
            "真实会话 JSON 往返丢失落地凭证的船、事件、世界或落地终态")
        assert(data.stableRoundtrip, "落地凭证在第二次会话快照中发生变化")
    end, "清船后的正式乘员落地凭证 JSON 往返：海面=" .. tostring(water))
end
test.system("ui_settings", "virtualized_inventory", {}, function(data)
    assert(data.poolCount == 12, "虚拟库存没有按视口和缓冲行限制槽位池")
    local expected = {
        {first = 3, count = 12, retained = 9, entering = 3},
        {first = 3, count = 12, retained = 12, entering = 0},
        {first = 0, count = 12, retained = 9, entering = 3},
        {first = 27, count = 12, retained = 0, entering = 12},
        {first = 36, count = 5, retained = 3, entering = 2},
        {first = 0, count = 12, retained = 0, entering = 12},
        {first = 3, count = 12, retained = 9, entering = 3}
    }
    assert(#data.samples == #expected, "虚拟库存滚动采样缺失")
    for index, sample in ipairs(data.samples) do
        local want = expected[index]
        assert(sample.poolCount == 12 and sample.active == want.count and #sample.indices == want.count,
            "滚动扩建槽位池或末尾没有正确隐藏空余槽")
        assert(sample.retained == want.retained and sample.entering == want.entering,
            "滚动一行重绑了仍可见的槽位，或跳转、回滚丢失槽位")
        for offset, slot in ipairs(sample.indices) do
            assert(slot == want.first + offset - 1, "虚拟库存显示索引缺失或重复")
        end
        assert(sample.mappingValid and sample.routed, "虚拟槽位位置、库存映射或点击数据索引错误")
        assert(sample.callbacksStable, "滚动或末尾重新显示时重复创建数据、点击或拖拽回调")
    end
    assert(data.clearedIndex == 12 and data.invalidUnchanged,
        "复用后的清空回调仍指向旧格，或无效索引破坏了绑定")
    assert(data.forcedRefreshStable and data.mapCleared and data.layoutRestored,
        "强制刷新重建了回调，或解绑没有清理映射并恢复布局")
    assert(data.expandedPoolCount == 18 and data.expandedMappingValid,
        "更大视口重新绑定后没有扩充槽位池或丢失数据映射")
end, "创造背包滚动：保留重叠槽位、复用交互回调与末尾回滚")
