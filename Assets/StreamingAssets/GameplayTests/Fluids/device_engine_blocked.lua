-- 氢能发动机缺气无负载保护：调用正式气液接口，逐项断言写入本案例报告。
test.check("world_ready")
test.check("player_ready")
test.fluid("device_engine_blocked", "氢能发动机缺气无负载保护")
