# 物品模块运行时

快速入口：`rules.lua`；操作：`item_runtime/module_structure、clone_runtime`。

已编写的规则覆盖：ModuleDataCollection 的结构版本、启停与移除退订；ItemInstanceDataFactory 的库存、模块和标签副本隔离。

尚待补充：强制同一池对象复用、换代后的旧引用拒绝、模块 Tick 频率、世界完整物品与轻量掉落物转换；正式加载、卸载和再次加载见下方共享现场。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。

## PlayMode 共享物品链路

入口：[物品、MOD与网络副本测试集](../ItemRuntime/gameplay-suite.json)，Lua业务断言在 [共享脚本](../ItemRuntime/gameplay.lua)。三个系统按同一实际实例链路组合，目录表映射同一案例，不重复执行。

已编写：正式 GameRes→ItemMgr 加载实际燧石小刀→跨帧存活→ModuleSave→卸载登记事件→再次加载新代际；检查稳定模块身份、索引清理、数据独立。

覆盖边界：没有强制保证拿回同一个池对象，检查的是卸载与再次加载契约；调度频率、轻量掉落转换、运行时换定义仍未覆盖。

本轮按用户要求仅静态编写，未编译、未执行或验收。
