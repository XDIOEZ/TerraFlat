# 自然物快照复制回归

运行 `python Tools/Tests/NaturalSnapshots/verify.py`，结果写入 `Temp/Tests/NaturalSnapshots/result.log`。

使用项目已编译且本轮未修改的 Data 工厂，检查身份、共享只读配置、独立模块/姿态/物质/物理状态、回池隔离、嵌套库存、MOD codec/内存工厂及持久快照往返，并对同一数据预热比较旧深拷贝与运行态工厂的分配。静态核对普通、分帧和 ECS 捕获入口及重新入世时恢复当前定义；不刷新或操作 Unity。

独立 CLR 数字不能代表真实场景帧率，也不验证 Unity 模块保存/卸载回调；编辑器空闲后还应运行 `ecology/natural_snapshot_copy` 及自然物加载、回收、保存恢复现场回归。
