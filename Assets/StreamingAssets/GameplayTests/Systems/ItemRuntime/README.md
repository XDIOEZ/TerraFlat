# 物品模块运行时

快速入口：`rules.lua`；操作：`item_runtime/module_structure、clone_runtime`。

已编写的规则覆盖：ModuleDataCollection 的结构版本、启停与移除退订；ItemInstanceDataFactory 的库存、模块和标签副本隔离。

关键流程待补：真实 Item 的 Load/Unload/池复用；运行代际失效；模块 Tick 调度；世界完整物品与轻量掉落物转换。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。
