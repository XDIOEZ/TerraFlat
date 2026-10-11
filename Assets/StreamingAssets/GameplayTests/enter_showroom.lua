-- 最基础的入场流程，后续可以直接追加通用测试命令。
test.check("world_ready", "游戏世界正常进入")
test.check("player_ready", "默认模板玩家正常创建")
test.check("terrain_ready", "玩家脚下地面正常加载")
test.check("showroom_complete", "所有物品已经按棋盘摆好")

test.log("默认世界和物品展示场已准备好，开始移动测试。")
test.move(1, 0, 0.5)
test.check("player_moved", "真实玩家可以正常移动")
test.move(-1, 0, 0.5)
test.wait(0.5)
test.log("基础入场测试流程结束。")
