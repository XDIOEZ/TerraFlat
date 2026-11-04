test.system("ui_settings", "gm_inspector_ownership", {}, function(data)
    assert(data.separateText and data.separateBuffers, "悬停观察器拆分后串用了目标或查询/文本缓冲")
    assert(data.clearOnlyOwner, "清理一个观察器破坏了另一个观察器的目标和文本")
    assert(data.sharedFieldCache and data.formatUnchanged, "独立观察器没有复用字段缓存，或改变了原文本格式")
end, "GM悬停观察器独立状态与共享反射缓存")

test.system("ui_settings", "gm_catalog_layout", {}, function(data)
    assert(data.sameFrame and data.startClosed, "两种目录没有共用正式窗口布局或初始显隐发生变化")
    assert(data.independentInputs and data.amountDefaults and data.searchSize, "共用目录布局串用了搜索/数量控件或改变默认值")
    assert(data.sameGridAlignment and data.separateContents, "目录的网格对齐或内容归属发生变化")
end, "GM空投和生物目录共享布局但保留独立控件")

test.system("ui_settings", "shared_checkbox_prefabs", {}, function(data)
    assert(data.checkboxCount > 10 and #data.unshared == 0, "正式复选框未全部继承公共 UI_Toggle")
    assert(data.white and data.rootHit and not data.fillHit, "白色选中态或整行点击区域异常")
    for _, sample in ipairs(data.samples) do
        assert(math.abs(sample.width - sample.fillWidth - 8) < .01
            and math.abs(sample.height - sample.fillHeight - 8) < .01,
            "复选框缩放后选中填充没有保持四像素内边距")
    end
    assert(data.offAlpha == 0 and data.onAlpha == 1, "复选框开关状态未控制填充显隐")
    assert(data.silentEvents == 0 and data.changeEvents == 1, "复选框静默赋值或变更事件异常")
end, "全局共享复选框填充与原生状态契约")
