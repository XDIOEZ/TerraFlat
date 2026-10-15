-- 由真实输入物品和正式配方产出产品，覆盖研磨、锯木、织布与锻压。
test.gm("mechanical_build")
test.gm("mechanical_start")
test.await_check("mechanical_output:loom:Cloth", 30, "棉花完成织布")
test.await_check("mechanical_output:sawmill:Plank", 30, "木头完成锯切")
test.await_check("mechanical_output:hammer:CopperPlate", 30, "铜锭完成机械锤加工")
test.await_check("mechanical_output:millstone:OrePowder_Copper", 30, "矿石完成机械研磨")
test.await_check("mechanical_pump_delivered", 5, "真实水车驱动气泵并将大气送入储罐")
test.check("mechanical_player_stationary", "机械工厂运行不应推动主角")
