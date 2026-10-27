-- 检查正式生存 HUD 的实际填充与数值文字在不同状态下均可区分。
test.runtime("ui_settings", "status_hud_readability", {}, function(data)
    local function linear(channel)
        return channel <= .04045 and channel / 12.92 or ((channel + .055) / 1.055) ^ 2.4
    end
    local function luminance(color)
        return .2126 * linear(color[1]) + .7152 * linear(color[2]) + .0722 * linear(color[3])
    end
    assert(data.panelVisible and #data.bars == 7, "本地玩家的生存状态条不完整")
    for _, bar in ipairs(data.bars) do
        local fill, text = luminance(bar.fill), luminance(bar.text)
        local contrast = (math.max(fill, text) + .05) / (math.min(fill, text) + .05)
        assert(bar.visible and bar.fill[4] > .99 and bar.text[4] > .99 and contrast >= 4,
            bar.name .. "状态条的填充遮住数值文字")
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
    assert(data.blocking and data.managerBlocking and data.navigationPrepared and data.interactive, "打开设置后没有接管玩法交互或可取消导航")
    assert(data.debugWorldLabelsSuppressed, "打开正常设置窗口时，头顶 AI 调试标签仍会覆盖可交互控件")
    assert(data.expectedChinese == "保存" and data.observedChinese == data.expectedChinese, "正式中文字符串表或 TMP Binder 没有刷新")
    assert(data.expectedEnglish ~= nil and #data.expectedEnglish > 0 and data.expectedEnglish ~= data.expectedChinese, "英文正式表缺项或仍是中文回退")
    assert(data.chineseSwitch.completed and data.englishSwitch.completed and data.discardedChineseSwitch.completed, "正式语言事件或 TMP 刷新没有在有界等待内完成")
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
