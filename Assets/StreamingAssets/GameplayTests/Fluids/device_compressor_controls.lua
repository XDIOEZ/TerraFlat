-- 压缩目标压力合法性：调用正式气液接口，逐项断言写入本案例报告。
test.check("world_ready")
test.check("player_ready")
test.fluid("device_compressor_controls", "压缩目标压力合法性")
