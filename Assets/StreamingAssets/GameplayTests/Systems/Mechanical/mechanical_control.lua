-- 离合器断连与恢复必须改变用力器实际 RPM，而不是只验证按钮状态。
test.gm("mechanical_build")
test.gm("mechanical_start")
test.await_check("mechanical_clutch_closed", 0.3, "离合器断开前下游已运行")
test.gm("mechanical_clutch_off")
test.await_check("mechanical_clutch_open", 0.4, "离合器断开且下游停止")
test.gm("mechanical_clutch_on")
test.await_check("mechanical_clutch_closed", 0.4, "离合器重新连上且下游恢复")
