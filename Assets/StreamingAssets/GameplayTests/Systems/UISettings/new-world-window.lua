-- 新世界覆盖层独立验收，避免被无关HUD或设置失败阻断。
test.runtime("ui_settings", "new_game_window_close", {}, function(data)
    assert(data.difficultyCloseStayedInNewGame, "关闭难度窗口误关新世界窗口，或没有回到名称页")
    assert(data.scope == "live-isolated-world" and data.worldPageVisible,
        "正式世界设置覆盖层没有打开")
    assert(data.worldCloseVisible and data.worldCloseText == data.expectedWorldCloseText and data.expectedWorldCloseText ~= "",
        "世界设置页右上按钮没有显示本地化的关闭")
    assert(data.panelOpenAfterWorldClose and data.worldVisibleAfterWorldClose == false and data.identityVisibleAfterWorldClose,
        "世界设置页右上关闭没有回到名称页或保留窗口")
    assert(data.playerNameAfterWorldClose == "ui_close_probe_player" and data.worldSeedAfterWorldClose == "ui_close_probe_seed",
        "关闭世界设置覆盖层后名称或种子输入丢失")
    assert(data.worldVisibleAfterReopen and data.worldSeedAfterReopen == "ui_close_probe_seed",
        "重新打开世界设置覆盖层失败或种子输入丢失")
    assert(data.panelOpenAfterBack and data.worldVisibleAfterBack == false and data.identityVisibleAfterBack and
        data.playerNameAfterBack == "ui_close_probe_player" and data.worldSeedAfterBack == "ui_close_probe_seed",
        "主窗口返回按钮没有关闭世界覆盖层或保留输入")
    assert(data.bottomBackHiddenInWorld and data.bottomBackHiddenInIdentity,
        "底部返回名称设置按钮仍然显示")
    assert(data.identityBackText == data.expectedIdentityBackText and data.identityTopCloseClosesPanel,
        "名称页右上按钮文案错误，或点击后没有关闭新世界窗口")
    assert(data.panelUnregistered, "新世界关闭流程测试没有清理自有窗口")
end, "关闭难度浮层仍留在名称页；世界页右上关闭回名称页并保留参数，名称页右上关闭窗口")
