-- 事件行动由真实调度器执行，气泡使用当前主角正式 Presenter，不替换显示器。
test.check("world_ready", "真实隔离世界就绪")
test.check("player_ready", "真实主角就绪")
test.runtime("game_events", "signal_lifecycle", {}, function(data)
    assert(data.template.id == "survival_day_10_long_rain" and data.template.enabled and
        data.template.oncePerWorld and data.template.durationDays > 0 and data.template.triggerType == "day.schedule",
        "夹具没有借用正式目录中已启用的一次性事件模板")
    assert(data.ordinaryTrigger and data.activeAfterStart, "正式调度器没有启动自有事件")
    assert(not data.forcedDuplicate and not data.repeatTrigger and data.activeCount == 1, "活动事件被重复登记")
    assert(data.sourceWorld == data.currentWorld, "事件归属没有使用当前真实世界")
    assert(#data.actions == 1 and data.actions[1].Started and data.actions[1].Completed, "正式行动没有完成一次执行")
    assert(data.actions[1].LastError == nil or data.actions[1].LastError == "", "正式事件行动留下错误")
    assert(#data.signals == 1 and data.signals[1].signal == "lua.narrative", "正式 signal.emit 行动重复通知或没有通知")
    assert(data.signals[1].payload.source == "actual-event-action" and data.signals[1].payload.amount == 7, "事件行动的原始载荷没有完整送达")
    assert(data.cancelled and not data.activeAfterCancel and not data.cancelAgain, "取消没有清除实际活动事件或重复取消被接受")
    assert(data.startedNotifications == 1 and data.endedNotifications == 1, "正式开始和结束通知不成对")
    assert(data.stagedTriggerCount == 0 and data.storedTriggerCount == 1, "实际触发没有在隔离进度中唯一记账")
    assert(not data.postCancelRepeatTrigger and data.signalsAfterRejected == 1 and
        data.storedTriggerCountAfterRejected == 1, "取消后普通重触发绕过了 oncePerWorld 或留下拒绝副作用")
    assert(data.forcedAfterCancel and data.forcedActive and data.forcedSourceWorld == data.currentWorld and
        data.forcedPayload.__gmForce == true, "正式 GM 强制重触发没有绕过一次性限制或标记真实来源")
    assert(#data.forcedActions == 1 and data.forcedActions[1].Started and data.forcedActions[1].Completed and
        (data.forcedActions[1].LastError == nil or data.forcedActions[1].LastError == ""),
        "GM 重触发没有执行一次正式信号行动")
    assert(#data.forcedSignals == 2 and data.forcedSignals[2].signal == "lua.narrative" and
        data.forcedSignals[2].payload.source == "actual-event-action" and data.forcedSignals[2].payload.amount == 7,
        "GM 强制重触发没有仅追加一次完整原始载荷")
    assert(data.forcedCancel and not data.forcedActiveAfterCancel and not data.forcedCancelAgain and
        data.startedNotificationsAfterForce == 2 and data.endedNotificationsAfterForce == 2 and
        data.storedTriggerCountAfterForce == 2, "两次正式触发、取消、通知与进度数量不一致")
    assert(data.selfEventCleaned and data.definitionRestored and data.progressRestored and
        data.restoredTriggerCount == data.originalTriggerCount, "事件自有行动配置、进度或活动状态没有完整恢复")
end, "真实事件调度、行动通知与取消")
test.runtime("dialogue", "bubble_priority", {}, function(data)
    assert(data.accepted and data.visibleAfterFirst and data.equalAccepted and data.visible, "正式主角气泡拒绝了合法发言")
    assert(not data.lowerAccepted and not data.blankAccepted and data.priorityAfterLower == 30, "低优先级或空台词覆盖了当前气泡")
    assert(data.requestNotifications == 2 and data.shownNotifications == 2, "拒绝的请求错误触发显示通知")
    assert(data.actualText == "Lua现场：任务进度和事件通知已送达。" and data.viewActive and data.labelActive and data.alpha == 1, "只有调度成功，实际屏幕气泡没有承载正确文字")
    assert(not data.interactable and not data.blocksRaycasts, "非交互气泡占用了玩家输入")
end, "实际主角气泡优先级与非交互显示")
test.screenshot("正式主角屏幕气泡显示与地表现场")
test.runtime("dialogue", "hide_bubble", {}, function(data)
    assert(data.visibleBefore and not data.visibleAfter, "自有发言没有通过正式 Presenter 清理")
end, "截图后清理自有气泡")
