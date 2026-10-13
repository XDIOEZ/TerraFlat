-- GM 设置倍率后，验证模块状态与真实控制链仍可移动。
test.check("player_ready")
test.gm("enable_admin")
test.gm("set_move_speed", "2")
test.check("gm_move_speed:2", "玩家速度倍率为两倍")
test.move(1, 0, 0.35)
test.check("player_moved", "双倍速度下真实玩家发生移动")
test.gm("set_move_speed", "1")
test.check("gm_move_speed:1", "速度倍率恢复正常")
