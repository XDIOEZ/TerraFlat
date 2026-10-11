-- 只检查默认世界入场和物品陈列，移动测试由独立用例负责。
test.check("world_ready", "游戏世界正常进入")
test.check("player_ready", "默认模板玩家正常创建")
test.check("terrain_ready", "玩家脚下地面正常加载")
test.check("showroom_complete", "所有物品已经按棋盘摆好")

test.log("默认世界和物品陈列厅已准备好，入场测试结束。")
