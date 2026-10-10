---
name: flatworld-game-events
description: "Use when: 定位或修改 FlatWorld 的全局游戏事件、事件 JSON、触发器、条件、行动、活动状态、冲突组、事件存档或扩展注册。关键词：GameEventManager、GameEventConfigLoader、GameEventExtensionRegistry、IGameEventActionHandler。"
---

# FlatWorld 游戏事件

## 入口

- 调度器：`Assets/5_Scripts/5-3_GamePlay/Core/GameEvents/GameEventManager.cs`
- 配置与契约：同目录 `{GameEventConfigLoader,GameEventConfigModels,GameEventContracts}.cs`
- 内置扩展：同目录 `{GameEventBuiltInTriggers,GameEventBuiltInActions,GameEventExtensionRegistry}.cs`
- JSON：`Assets/Resources/Config/GameEvents/Definitions/`
- Lua：`Assets/Resources/Config/GameEvents/Lua/*.lua.txt`；运行时、接口与适配器：同事件目录 `GameEventLua{Runtime,Context,Extensions,Data}.cs`，示例说明 `ModSDK/Examples/GameEventsLua/README.md`。
- 存档：`World/Map/Data/{GameSaveData.GameEvents,GameEventSaveData}.cs`

## 主链

`合并 JSON 与 Lua catalog → 共用校验和定义 → 触发器产生候选 → 条件/冲突组判定 → 权威端执行行动 → 保存状态并广播通知`

## 边界

- `event.id` 和 `action.id` 会进入存档，发布后不要随意改名。
- 只有状态权威端启动和恢复事件；客户端只消费同步结果或通知。
- 单个坏文件或坏事件应被隔离，不能阻断其他有效配置。
- 新行为实现并注册 Handler；不要把玩法分支堆进 `GameEventManager` 或改成专用 JSON 字段。
- Lua 模块返回 `catalog`（可选）及 `triggers/conditions/actions`；两种目录都通过 `type: lua` 的 `script/handler/args` 引用回调。加载声明与触发/条件只读，只有权威端行动 `begin/tick` 可通过受限接口启动或推进非 Lua 子行动。
- Lua 跨帧进度必须放在纯数据 `state`，不存 ctx、函数、userdata；空数组用 `event.array({})`。子行动固定 ID、类型和参数，Lua 显式调用 `RunAction` 推进；父行动结束或异常清理子行动，退出/切维度仅挂起临时效果并保留进度，恢复须包含已完成但仍有持续效果的子行动。
- 原生行动存在需要在世界退出时移除的临时效果时，实现 `IGameEventSuspendableAction`，配合 `Resume` 恢复；挂起不能直接调用正式 `End` 或删除进度。
- 有活动 Lua 行动时拒绝配置重载，保持原定义和虚拟机；先结束相关事件再重载。Lua 脚本平铺且名称唯一，坏模块隔离；本体脚本加载不代表 MOD 包已接入事件注册。
- 逐帧触发器复用已校验的类型参数；修改现有定义参数必须重新校验或替换参数 JObject。候选运行态只在状态变化时写回 JSON，缓存须识别外部 JSON 替换并随进度对象释放，不能延迟到退出才写回。
- 扩展查询先用已校验的类型键直接查表，未命中再统一 Trim/转小写；不能让每帧事件调度重复分配规范化字符串，外部原始输入仍须可用。
- 行动的完成、取消和世界退出路径都要清理运行时状态；天气、怪物、存档或联机变化同时使用对应 Skill。
- 生物事件行动的待生成数量只按实际成功数扣减；`MonsterSpawnerManager.SpawnEventCreatures` 每次最多创建一只，候选必须在所有活动镜头外且区块表现就绪，受共享出生间隔和候选检查预算约束。
- `creature.advance` 取得 Actor GUID 后通过 `AiRuntimeBackendService` 向实际 GameObject/ECS 后端下发命令；命令失败要回收该 Actor，并且不能把它计入事件已生成数。

## 验证

- 事件验收统一进入真实 Play Mode，实际满足触发条件并观察行动、冲突和存档状态；不再维护 `GameEvents.*` Test Runner 分类。
- JSON、编译和 Console 只作为实机运行前后的辅助诊断。

## Skill 维护原则

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
