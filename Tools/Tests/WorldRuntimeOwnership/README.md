# 世界模型所有权离线回归

在项目根目录执行 `python Tools/Tests/WorldRuntimeOwnership/verify.py`。测试使用本机 Unity Bee 响应文件编译当前 `WorldModel` 源码到 `Temp/WorldRuntimeOwnershipValidation`，再由独立 .NET 进程验证外部持有的纯世界模型、表现租约、调度器取消与重接、旧结果释放和旧请求隔离。

该测试不启动 Unity，也不覆盖建筑/生物持久化、场景切换、真实表现或联机。
