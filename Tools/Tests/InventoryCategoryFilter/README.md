# 库存分类筛选离线验证

执行 `python -X utf8 Tools/Tests/InventoryCategoryFilter/verify.py --build`。

脚本在 Temp 中编译生产 `InventoryCategoryFilter`，验证并集真值表、类别别名、大小写、未知类别、管道与工业设施配置；同时检查 Prefab 内部引用和中英文正式表。`--build` 额外编译 GamePlay 与编辑器程序集，临时补入播放期间尚未导入的新脚本，不修改 Unity 项目文件、不刷新、不暂停或退出用户游戏。

离线通过不等于真实 Unity 画面、事件生命周期、点击、拖拽或触屏已验收。真实投影与点击索引回归位于 UISettings/rules.lua 的 `inventory_filter_projection`，只在允许测试的编辑器状态执行。
