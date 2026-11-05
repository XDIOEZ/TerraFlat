-- 掉落物实际沿动力输送带移动，不只检查机械转速或外观。
test.gm("mechanical_build")
test.gm("mechanical_start")
test.await_check("mechanical_running:conveyor_wood", 0.4, "木制输送带已取得有效转速", "conveyor-startup")
test.await_check("mechanical_running:conveyor_cloth", 0.4, "布制输送带已取得有效转速", "conveyor-startup")
test.gm("mechanical_conveyor_drop")
test.await_check("mechanical_conveyor_transports_item", 0.7, "真实掉落物被输送带推动")
