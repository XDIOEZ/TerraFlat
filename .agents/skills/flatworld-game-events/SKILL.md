---
name: flatworld-game-events
description: "FlatWorld 全局事件目录、触发/条件/行动、Lua 扩展、冲突调度与事件进度。"
---

# FlatWorld 游戏事件

## 入口与数据流

`Assets/5_Scripts/5-3_GamePlay/Core/GameEvents/`：`GameEventManager` 调度，`GameEventConfig{Loader,Models}` 加载/定义，`GameEventContracts` 契约，`GameEventBuiltIn{Triggers,Actions}` 与 `GameEventExtensionRegistry` 扩展，`GameEventLua{Runtime,Context,Extensions,Data}` Lua 桥接。

- JSON/Lua：`Assets/Resources/Config/GameEvents/{Definitions,Lua}/`，脚本为 `.lua.txt`；示例：`ModSDK/Examples/GameEventsLua/README.md`。
- 存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/{GameSaveData.GameEvents,GameEventSaveData}.cs`。
- 主链：JSON/Lua catalog 合并与校验 → 触发候选 → 条件/冲突组 → 权威行动 → 状态保存与通知。客户端消费同步结果；坏文件/事件独立隔离。

## 运行契约

- `event.id/action.id` 是进度身份；触发、条件、行动通过 Handler 注册，调度器不承载具体玩法分支。
- Lua 模块返回可选 `catalog` 及 `triggers/conditions/actions`，定义以 `type: lua` 的 `script/handler/args` 引用。声明、触发与条件只读，权威行动的 `begin/tick` 经受限接口推进子行动。
- 跨帧 `state` 只含纯数据，空数组为 `event.array({})`；子行动 ID/类型/参数固定，由 `RunAction` 显式推进。父行动结束/异常清理子行动；世界退出/切维度挂起临时效果并保留进度，恢复包括已完成但仍有持续效果的子行动。原生临时效果由 `IGameEventSuspendableAction` 的挂起/Resume 管理，挂起与正式 End 分离。
- 活动 Lua 行动期间配置和 VM 保持不变，事件结束后可重载；脚本平铺且名称唯一，MOD 包注册与本体脚本加载独立。
- 高频调度复用校验后的类型/参数，未命中的外部键再规范化；运行态仅在变化时写回 JSON，缓存识别外部对象替换且随进度释放。
- 生物待生成数按实际成功扣减，`SpawnEventCreatures` 每次最多一只，并受活动镜头外、区块就绪、共享间隔和候选预算约束。`creature.advance` 经 Actor GUID 和 `AiRuntimeBackendService` 向实际后端下命令，失败回收且不计成功。
