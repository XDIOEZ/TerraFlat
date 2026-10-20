-- 在隔离测试世界用正式控制与快捷栏入口完成关键玩家流程。
test.check("world_ready", "世界已进入")
test.check("terrain_ready", "玩家脚下地形就绪")
test.runtime("player", "interaction_input_reenable", {}, function(data)
    assert(data.initialStarts == 1 and data.initialEnds == 1, "首次 F 按下或松开没有走正式交互")
    assert(data.disabledStarts == data.initialStarts, "交互模块停用后仍然接收 F")
    assert(#data.rounds == 3, "缺少多次停用恢复采样")
    for _, round in ipairs(data.rounds) do
        assert(round.starts == 1 and round.ends == 1 and not round.held,
            "恢复交互模块后 F 失效、重复触发或松开后仍保持交互")
    end
end, "交互模块反复停用恢复后 F 按下和松开均只触发一次")
test.gm("enable_admin")
test.gm("set_move_speed", "2")
test.check("gm_move_speed:2", "实际玩家移动倍率为二")
test.select_hotbar(0)
test.check("hotbar_slot:0", "快捷栏选中第一格")
test.select_hotbar(1)
test.check("hotbar_slot:1", "快捷栏切换第二格")
test.move(1, 0, .5)
test.move(0, 1, .5)
test.check("player_moved", "正式输入使真实玩家产生位移")
test.gm("set_move_speed", "1")
test.check("gm_move_speed:1", "移动倍率恢复")
