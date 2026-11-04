-- 正式设置Provider持有领域校验，写入只在缓存变化时持久化并发出对应通知。
test.runtime("ui_settings", "local_float_preference_persistence", {}, function(data)
    local expected = { ["camera.lookahead"] = "camera", ["camera.lookaheadSmoothing"] = "camera",
        ["camera.lookaheadZoomInfluence"] = "camera", ["ui.scale"] = "ui",
        ["ui.hotbarBottomSpacing"] = "hotbar", ["ui.touchControlsOpacity"] = "touch",
        ["ui.pinchZoomSensitivity"] = "none", ["ui.leftControlZoneRatio"] = "mobile",
        ["ui.rightControlZoneRatio"] = "mobile" }
    assert(#data.preferences == 9, "本地浮点偏好采样不完整")
    for _, preference in ipairs(data.preferences) do
        local event = expected[preference.key]
        assert(event, "偏好缺失或重复：" .. preference.key)
        expected[preference.key] = nil
        assert(math.abs(preference.cached - preference.target) < .0001
            and math.abs(preference.stored - preference.target) < .0001,
            preference.key .. "没有同步缓存及持久值")
        for _, name in ipairs({ "camera", "ui", "hotbar", "touch", "mobile" }) do
            assert(preference.changedEvents[name] - preference.beforeEvents[name] == (name == event and 1 or 0),
                preference.key .. "通知次数或所属领域改变")
            assert(preference.noOpEvents[name] == preference.changedEvents[name], preference.key .. "无变化仍发通知")
        end
        assert(preference.noOpStored == -987654 and math.abs(preference.noOpCached - preference.target) < .0001,
            preference.key .. "相同缓存值仍覆写PlayerPrefs或从磁盘重读")
        assert(preference.preferenceRestored and math.abs(preference.restoredCache - preference.original) < .0001,
            preference.key .. "没有恢复用户偏好原值或缓存")
    end
    assert(next(expected) == nil, "遗漏本地浮点设置")
end, "九项本地浮点偏好的持久化、无变化跳过、通知归属及原值恢复")

-- 检查正式生存 HUD 的实际填充与数值文字在不同状态下均可区分。
test.runtime("ui_settings", "status_hud_readability", {}, function(data)
    local function linear(channel)
        return channel <= .04045 and channel / 12.92 or ((channel + .055) / 1.055) ^ 2.4
    end
    local function luminance(color)
        return .2126 * linear(color[1]) + .7152 * linear(color[2]) + .0722 * linear(color[3])
    end
    assert(data.panelVisible and #data.bars == 5 and data.sliderCount == 4, "生存状态条未合并为五项，或仍有旧营养滑条")
    assert(data.legendVisible == false, "食物营养图例行仍然显示")
    for _, bar in ipairs(data.bars) do
        local fill, text = luminance(bar.fill), luminance(bar.text)
        local contrast = (math.max(fill, text) + .05) / (math.min(fill, text) + .05)
        assert(bar.visible and bar.fill[4] > .99 and bar.text[4] > .99 and contrast >= 4,
            bar.name .. "状态条的填充遮住数值文字")
        if bar.name == "血量" then
            assert(bar.fill[1] > bar.fill[2] and bar.fill[1] > bar.fill[3], "血量条没有使用红色")
        elseif bar.name == "水" then
            assert(bar.fill[3] > bar.fill[2] and bar.fill[2] > bar.fill[1], "水量条没有使用蓝色")
        end
        if bar.name == "食物" then
            assert(#bar.segmentColors == 3, "食物条缺少三种营养颜色")
            for index, color in ipairs(bar.segmentColors) do
                assert((text + .05) / (luminance(color) + .05) >= 4 and color[4] > .99,
                    "食物分段遮住总量文字")
                local nextColor = bar.segmentColors[index % 3 + 1]
                assert(math.abs(color[1] - nextColor[1]) + math.abs(color[2] - nextColor[2]) +
                    math.abs(color[3] - nextColor[3]) > .2, "食物三段颜色无法区分")
            end
            local protein, fat, carbohydrates = bar.segmentColors[1], bar.segmentColors[2], bar.segmentColors[3]
            assert(protein[3] > protein[1] and protein[1] > protein[2], "蛋白质颜色未排在食物条最左侧")
            assert(fat[3] > fat[2] and fat[2] > fat[1], "脂肪颜色未留在食物条中间")
            assert(carbohydrates[1] > carbohydrates[2] and carbohydrates[2] > carbohydrates[3],
                "碳水颜色未排在食物条最右侧")
        end
    end
    local total, maximum = 0, 0
    for index = 1, 3 do
        total = total + data.nutrition[index]
        maximum = maximum + data.maxima[index]
    end
    local shownTotal, shownMaximum = string.match(data.foodText, "^(%d+)/(%d+)$")
    assert(shownTotal and math.abs(tonumber(shownTotal) - total) <= .501 and
        math.abs(tonumber(shownMaximum) - maximum) <= .501, "食物总量不是三项加算")
    for index = 1, 3 do
        local nutrientIndex = 4 - index
        local expected = maximum > 0 and data.nutrition[nutrientIndex] / maximum or 0
        assert(math.abs(data.fractions[index] - expected) < .0001,
            "食物段未按蛋白质、脂肪、碳水顺序显示总容量比例")
    end
end, "真实生存状态条文字与填充可读性")

-- 从正式 GameManager 入口打开真实设置窗口，用现有下拉、滑块和保存按钮完成事务。
test.runtime("ui_settings", "settings_locale_open", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.formalWindow and data.registered and data.open, "正式窗口没有创建、绑定或登记")
    local expectedPages = { AudioSettingsPanelLauncher = true, UISettingsPanelLauncher = true,
        LanguageSettingsPanelLauncher = true, CameraControlSettingsPanelLauncher = true,
        CoordinateDisplaySettingsPanelLauncher = true, RuntimeDebugSettingsPanelLauncher = true }
    assert(#data.commonPageControllers == 6, "设置面板没有装配完整的六个共用本地页")
    for _, page in ipairs(data.commonPageControllers) do
        assert(expectedPages[page.controller] and page.beforeCount == 1 and page.afterCount == 1
            and page.instanceRetained and page.expectedPageRoot, "共用设置页缺失、重复装配或绑定到错误页面")
        expectedPages[page.controller] = nil
    end
    assert(next(expectedPages) == nil, "共用设置页漏装配")
    local sidebar = data.sidebarVisuals
    assert(sidebar and #sidebar.tabs == 9, "设置左侧应只保留九个客户端分类")
    local requiredTabs = { ["镜头控制"] = true, ["显示设置"] = true, ["设置页签_画质"] = true,
        ["设置页签_语言"] = true, ["设置页签_调试"] = true }
    local selectedCount, displayIndex, graphicsIndex = 0, nil, nil
    for index, tab in ipairs(sidebar.tabs) do
        local selected = tab.name == "镜头控制"
        if selected then selectedCount = selectedCount + 1 end
        if tab.name == "显示设置" then displayIndex = index end
        if tab.name == "设置页签_画质" then graphicsIndex = index end
        assert(tab.name ~= "视觉特效", "视觉特效仍是独立分类")
        requiredTabs[tab.name] = nil
        local expectedBackground = selected and sidebar.activeBackground or sidebar.inactiveBackground
        local expectedCaption = selected and sidebar.activeCaption or sidebar.inactiveCaption
        for channel = 1, 4 do
            assert(math.abs(tab.background[channel] - expectedBackground[channel]) < 0.002
                and math.abs(tab.caption[channel] - expectedCaption[channel]) < 0.002,
                "设置左侧页签选中与未选中颜色不统一：" .. tab.name)
        end
    end
    assert(selectedCount == 1 and next(requiredTabs) == nil, "设置侧栏必须只有镜头页高亮，且包含画质、语言、调试页")
    assert(displayIndex and graphicsIndex == displayIndex + 1, "显示与画质页没有相邻排列")
    assert(sidebar.displayComplete and sidebar.graphicsComplete and sidebar.legacyVisualPageRemoved,
        "显示/画质没有集中对应控件，或仍残留旧视觉特效页")
    assert(data.blocking and data.managerBlocking and data.navigationPrepared and data.interactive, "打开设置后没有接管玩法交互或可取消导航")
    assert(data.debugWorldLabelsSuppressed, "打开正常设置窗口时，头顶 AI 调试标签仍会覆盖可交互控件")
    assert(data.expectedChinese == "保存" and data.observedChinese == data.expectedChinese, "正式中文字符串表或 TMP Binder 没有刷新")
    assert(data.expectedEnglish ~= nil and #data.expectedEnglish > 0 and data.expectedEnglish ~= data.expectedChinese, "英文正式表缺项或仍是中文回退")
    assert(data.chineseSwitch.completed and data.englishSwitch.completed and data.discardedChineseSwitch.completed, "正式语言事件或 TMP 刷新没有在有界等待内完成")
    for _, state in ipairs({ data.chineseSwitch, data.englishSwitch, data.discardedChineseSwitch }) do
        assert(#state.nativeOptionLabels == 2 and state.nativeOptionLabels[1] == "简体中文"
            and state.nativeOptionLabels[2] == "English", "语言选项名称不应随当前界面语言翻译")
    end
    assert(data.chineseSwitch.visibleLanguage == "简体中文" and data.englishSwitch.visibleLanguage == "English"
        and data.discardedChineseSwitch.visibleLanguage == "简体中文", "语言下拉当前显示名称未保留原生写法")
    assert(data.chineseSwitch.languageStatus == "游戏语言: 简体中文"
        and data.englishSwitch.languageStatus == "Language: English", "语言状态未正确组合本地化提示和原生名称")
    assert(data.englishSwitch.changeRequested and data.englishSwitch.languageEventReceived and data.englishSwitch.actualLocale == "en", "语言下拉没有通过正式语言事件完成英语切换")
    assert(data.observedEnglish == data.expectedEnglish and data.nodeNamePreserved, "语言下拉没有驱动真实文本或改了绑定节点身份")
    assert(data.sameBoundText and data.textInstanceId == data.currentTextInstanceId and data.binderInstanceId == data.currentBinderInstanceId, "语言切换替换了原 TMP 节点或翻译 Binder")
    assert(data.unsavedBeforeCommit and data.commitClearedChanges and data.changedAfterCommit, "保存按钮没有建立新的正式设置基线")
    assert(data.discardedToCommittedLocale and math.abs(data.discardedToCommittedVolume - 0.4) < 0.0001 and data.cleanAfterClose, "关闭没有放弃保存之后的语言与音量修改")
    assert(data.duplicateOpenEvents == 0 and data.unexpectedCloseEvents == 0, "重复打开触发了额外业务事件")
    assert(not data.invalidLocaleAccepted and data.currentLocale == "en", "非法语言改变了有效 Locale")
    assert(data.fallback == "Lua 缺项回退" and data.reopenedEnglishReady
        and data.reopenedEnglish == data.expectedEnglish, "缺项回退或关闭重开后的正式译文错误")
    assert(math.abs(data.currentVolume - 0.4) < 0.0001, "正式音量滑块写入或保存后的恢复丢失")
end, "真实设置控件、保存放弃、正式翻译表与 TMP 动态绑定")
-- 夹具显式关闭英语窗口；自动截图并行采集，失败时由本 Lua 的清理租约恢复。
test.runtime("ui_settings", "settings_locale_close", {}, function(data)
    assert(data.closed and data.panelUnregistered and data.fixtureReleased, "自有设置窗口没有关闭、注销或释放")
    assert(data.inputBlockingRestored, "窗口关闭后残留玩法输入阻断")
    assert(data.localeRestored and data.volumeRestored and data.preferencesRestored, "测试改变的语言、音量或 PlayerPrefs 原值未恢复")
end, "真实窗口清理和原语言、音量、偏好恢复")

test.runtime("ui_settings", "new_game_window_close", {}, function(data)
    assert(data.difficultyCloseStayedInNewGame, "关闭难度窗口误关新世界窗口，或没有回到名称页")
    assert(data.scope == "live-isolated-world" and data.worldPageOpened, "正式新世界面板没有进入世界设置页")
    assert(data.worldCloseText == data.expectedWorldCloseText and data.expectedWorldCloseText ~= "",
        "世界设置页右上按钮没有显示本地化的关闭")
    assert(data.worldCloseReturnedToIdentity and data.playerNameRetained and data.worldSeedRetained,
        "世界设置页右上关闭没有回到名称页、保留窗口或保留输入值")
    assert(data.bottomBackHiddenInWorld and data.bottomBackHiddenInIdentity,
        "底部返回名称设置按钮仍然显示")
    assert(data.identityBackText == data.expectedIdentityBackText and data.identityTopCloseClosesPanel,
        "名称页右上按钮文案错误，或点击后没有关闭新世界窗口")
    assert(data.panelUnregistered, "新世界关闭流程测试没有清理自有窗口")
end, "关闭难度浮层仍留在名称页；世界页右上关闭回名称页并保留参数，名称页右上关闭窗口")

test.runtime("ui_settings", "flick_settings_controls", {}, function(data)
    assert(data.active and data.scrollContainsOptions and data.wholeNumbers
        and data.speedMin == 25 and data.speedMax == 200 and data.distanceMin == 25 and data.distanceMax == 200,
        "游戏操作页缺少正式可滚动滑条/开关，或范围与整数步进错误")
    assert(data.initial.speed == 100 and data.initial.distance == 100 and data.initial.angle
        and data.initial.speedText == "100%" and data.initial.distanceText == "100%",
        "正式操作页没有回填两个默认百分比和方向开关")
    assert(data.applied.speedPercent == 25 and data.applied.distancePercent == 200 and not data.applied.angleLimit
        and data.editedControls.speedText == "25%" and data.editedControls.distanceText == "200%",
        "操作页真实控件未写入正式甩动偏好或百分比文字未更新")
    assert(data.dirtyBeforeSave and data.cleanAfterSave and data.dirtyAfterSave,
        "甩动控件没有接入保存按钮与后续未保存修改检测")
    assert(data.persisted.speedPercent == 25 and data.persisted.distancePercent == 200 and not data.persisted.angleLimit
        and data.afterClose.speedPercent == 25 and data.afterClose.distancePercent == 200 and not data.afterClose.angleLimit,
        "保存未写入偏好，或关闭没有恢复最近保存的三项值")
    assert(data.reopenedControls.speed == 25 and data.reopenedControls.distance == 200 and not data.reopenedControls.angle
        and data.reopenedControls.speedText == "25%" and data.reopenedControls.distanceText == "200%",
        "放弃修改后重开操作页仍显示未保存的旧控件值")
    assert(data.preferencesRestored and data.inputPreferenceRestored and data.panelUnregistered,
        "设置控件夹具没有恢复甩动/按键偏好原值或销毁自有窗口")
end, "游戏操作页甩动滑条、开关、保存放弃与重开回填")
