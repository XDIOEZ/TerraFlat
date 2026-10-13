-- 掉落物实际沿动力输送带移动，不只检查机械转速或外观。
test.gm("mechanical_build")
test.gm("mechanical_start")
test.wait(0.4)
test.check("mechanical_running:conveyor_wood", "木制输送带已取得有效转速")
test.check("mechanical_running:conveyor_cloth", "布制输送带已取得有效转速")
test.gm("mechanical_conveyor_drop")
test.wait(0.7)
test.check("mechanical_conveyor_transports_item", "真实掉落物被输送带推动")
