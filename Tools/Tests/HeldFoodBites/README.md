# 手持食物咬痕离线回归

运行 `python Tools/Tests/HeldFoodBites/verify.py`，直接编译正式 `FoodBiteGeometry` 并验证确定性、进度前缀、凹形轮廓、非最终可见区及超过32口的配置；使用 Windows SDK 的 FXC 编译 Shader 中的正式裁剪函数。产物仅写 Library，不操作运行中的 Unity。

值类型夹具和 HLSL 编译不代替 Unity 材质、生命周期与 GPU 画面验收。编辑器空闲后执行 `python Tools/GameplayTesting/run_suite.py --systems rendering`，对应 `rendering/held_food_bites` 另查完整食物不建表现资源、空 Sprite、翻转、外部 MPB 合成、原排序与回收。
