-- 超压固定受损成员：调用正式气液接口，逐项断言写入本案例报告。
test.check("world_ready")
test.check("player_ready")
test.fluid("tank_overpressure", "超压固定受损成员")
