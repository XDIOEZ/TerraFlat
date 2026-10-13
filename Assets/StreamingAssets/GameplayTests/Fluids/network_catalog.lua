-- 本体工业气液目录完整性：调用正式气液接口，逐项断言写入本案例报告。
test.check("world_ready")
test.check("player_ready")
test.fluid("network_catalog", "本体工业气液目录完整性")
