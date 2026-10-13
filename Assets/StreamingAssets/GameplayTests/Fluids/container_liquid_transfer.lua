-- 工业液体端口原子事务：调用正式气液接口，逐项断言写入本案例报告。
test.check("world_ready")
test.check("player_ready")
test.fluid("container_liquid_transfer", "工业液体端口原子事务")
