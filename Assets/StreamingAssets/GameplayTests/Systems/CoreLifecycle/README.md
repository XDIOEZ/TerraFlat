# 核心生命周期与资源会话

快速入口：`rules.lua`；操作：`core_lifecycle/resource_plan、resource_candidate`。真实入口：`gameplay.lua` / `core_lifecycle/live_entry`。

已编写的规则覆盖：正式 ResourceLoadPipeline 的依赖顺序、缺失依赖、循环依赖、进度不倒退、异常清理；ResourceReloadContext 的跨帧恢复、候选释放、失败不发布和完成后提交。真实场景观察正式 Ready、玩家生命周期、入世闸门以及脚下和可见窗口表现。

关键流程待补：菜单最小资源就绪的提前时机；退出期间资源释放；真实完整资源候选更新的发布和失败回退。入世测试只观察正式隔离建世界流程的最终结果，不伪造 Scene。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。
