# 后台生成调度回归

运行 `python Tools/Tests/BackgroundGenerationScheduling/verify.py`，在独立 .NET 进程编译正式调度器并执行与 Lua 共用的探针：验证正式需求优先、同区域提升/复用、共享并发额度、动态降额、取消、世界退出和失败后的重试。工具用最小类型桩替代 Unity 运行时，不替代 Unity 编译或实际水文回归。
