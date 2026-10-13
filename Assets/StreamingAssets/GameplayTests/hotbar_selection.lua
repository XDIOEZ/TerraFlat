-- 真实快捷栏选择接口与当前选中槽一致。
test.check("player_ready")
test.select_hotbar(0)
test.check("hotbar_slot:0", "快捷栏选中第一格")
test.select_hotbar(1)
test.check("hotbar_slot:1", "快捷栏选中第二格")
test.select_hotbar(0)
test.check("hotbar_slot:0", "快捷栏切回第一格")
