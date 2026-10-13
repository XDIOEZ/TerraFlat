-- GM 身份、无敌状态与实际入伤倍率分别断言。
test.check("world_ready")
test.gm("enable_invincibility")
test.check("gm_admin_enabled", "GM 管理员身份生效")
test.check("gm_invincibility_enabled", "无敌开关已打开")
test.check("gm_damage_blocked", "受击规则拒绝普通伤害")
test.gm("disable_invincibility")
test.check("gm_invincibility_disabled", "无敌开关已关闭")
test.check("gm_damage_normal", "普通受击倍率恢复")
