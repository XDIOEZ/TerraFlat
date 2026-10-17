-- 调用偏好正式净化规则，不写 PlayerPrefs 或改变当前 UI。
for _, row in ipairs({ { size = -1, expected = 0.5 }, { size = 0.5, expected = 0.5 }, { size = 1.25, expected = 1.25 }, { size = 8, expected = 2 }, { nan = true, expected = 1 }, { infinity = true, expected = 1 } }) do
    test.system("ui_settings", "mobile_size", row, function(data)
        assert(data.size == row.expected, "移动控件倍率没有按正式范围或异常默认值处理")
    end, "UI 独立控件倍率：" .. row.expected)
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
