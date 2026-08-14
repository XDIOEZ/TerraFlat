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
- 存档：`World/Map/Data/{GameSaveData.GameEvents,GameEventSaveData}.cs`

## 主链

`按文件名合并 JSON → 校验并注册定义 → 触发器产生候选 → 条件/冲突组判定 → 权威端执行行动 → 保存状态并广播通知`

## 边界

- `event.id` 和 `action.id` 会进入存档，发布后不要随意改名。
- 只有状态权威端启动和恢复事件；客户端只消费同步结果或通知。
- 单个坏文件或坏事件应被隔离，不能阻断其他有效配置。
- 新行为实现并注册 Handler；不要把玩法分支堆进 `GameEventManager` 或改成专用 JSON 字段。
- 行动的完成、取消和世界退出路径都要清理运行时状态；天气、怪物、存档或联机变化同时使用对应 Skill。

## 验证

- 默认检查 JSON、静态诊断、Unity 编译和 Console。
- 仅用户明确要求时运行对应 `GameEvents.*` 分类；测试位于 `Assets/GameTest/GameEvents/`。

## Skill 维护原则

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
