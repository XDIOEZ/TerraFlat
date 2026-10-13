-- 全部实际电解配方产物比例：调用正式气液接口，逐项断言写入本案例报告。
test.check("world_ready")
test.check("player_ready")
test.fluid("device_electrolysis_recipes", "全部实际电解配方产物比例")
