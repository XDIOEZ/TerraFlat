-- 通过 GM 的正式 Buff API 施加和移除夜视，验证状态管理器。
test.check("player_ready")
test.gm("apply_self_buff", "core:night_vision")
test.check("gm_buff_active:core:night_vision", "夜视 Buff 已被登记")
test.wait(0.2)
test.check("gm_buff_active:core:night_vision", "夜视 Buff 仍然生效")
test.gm("remove_self_buff", "core:night_vision")
test.check("gm_buff_absent:core:night_vision", "移除 Buff 后状态已清空")
