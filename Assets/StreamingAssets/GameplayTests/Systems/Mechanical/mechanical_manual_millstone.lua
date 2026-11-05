-- 石磨除了受轴驱动外，还能不依赖外部动力通过正式手推供能。
test.gm("mechanical_build")
test.gm("mechanical_start")
test.await_check("mechanical_running:millstone", 0.3, "断开前石磨由外部动力驱动")
test.gm("mechanical_stone_disconnect")
test.await_check("mechanical_stone_external_off", 0.3, "外部手摇停转后石磨应停止")
test.gm("mechanical_stone_push")
test.await_check("mechanical_stone_selfpowered", 0.3, "石磨靠自身手推获得独立动力")
