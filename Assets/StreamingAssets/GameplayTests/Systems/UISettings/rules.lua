-- 调用偏好正式净化规则，不写 PlayerPrefs 或改变当前 UI。
for _, row in ipairs({ { size = -1, expected = 0.5 }, { size = 0.5, expected = 0.5 }, { size = 1.25, expected = 1.25 }, { size = 8, expected = 2 }, { nan = true, expected = 1 }, { infinity = true, expected = 1 } }) do
    test.system("ui_settings", "mobile_size", row, function(data)
        assert(data.size == row.expected, "移动控件倍率没有按正式范围或异常默认值处理")
    end, "UI 独立控件倍率：" .. row.expected)
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
