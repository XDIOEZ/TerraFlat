-- 玩家移动用例从自己的起始位置检查移动结果。
test.check("world_ready", "游戏世界已经进入")
test.check("player_ready", "测试玩家已经就绪")
test.log("开始测试真实玩家的基础移动。")
test.move(1, 0, 0.5)
test.check("player_moved", "真实玩家可以正常移动")
test.move(-1, 0, 0.5)
test.wait(0.5)
test.log("玩家基础移动测试结束。")
