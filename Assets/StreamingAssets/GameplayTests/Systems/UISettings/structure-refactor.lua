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

test.system("ui_settings", "shared_progress_bars", {}, function(data)
    assert(#data.unshared == 0, "仍有进度条未引用公共预制体：" .. table.concat(data.unshared, ", "))
    assert(#data.brokenVisuals == 0, "进度条填充或方向错误：" .. table.concat(data.brokenVisuals, ", "))
    assert(#data.brokenBindings == 0, "业务面板的进度条引用丢失：" .. table.concat(data.brokenBindings, ", "))
    assert(data.interactiveSliderUnchanged, "可交互设置滑块被错误接入进度过渡")
    assert(#data.samples == 8, "进度条方向或填充类型采样不完整")
    for _, sample in ipairs(data.samples) do
        assert(sample.directionCorrect, "填充图片的方向与进度条方向不一致")
        assert(math.abs(sample.initial - .2) < .001 and math.abs(sample.target - .8) < .001,
            "初始显示或业务目标值异常")
        assert(sample.increasing > .2 and sample.increasing < .8,
            "增加数值时没有显示中间过渡")
        assert(sample.reversing > .1 and sample.reversing < sample.increasing,
            "过渡途中减少时没有从当前显示位置平滑改向")
        assert(math.abs(sample.completed - .1) < .001 and math.abs(sample.reopened - .7) < .001,
            "过渡结束或重新启用后的显示值错误")
        assert(sample.silentEvents == 0 and sample.changeEvents == 1,
            "过渡影响了 Slider 的静默赋值或变更事件")
    end
end, "公共进度条引用、四方向与增减过渡")
