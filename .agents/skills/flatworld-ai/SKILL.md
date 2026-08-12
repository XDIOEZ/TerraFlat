---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物/怪物 AI、状态机、感知、目标选择、攻击、闲逛、AI 移动、行为树兼容、怪物生成器或 AI Prefab。关键词：AI_Base、Mod_ItemDetector、AI_StateMachineRunner、MonsterSpawnerManager。"
---

# FlatWorld AI

## 入口

- 状态机：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/{AI_Base,AI_StateMachineRunner}.cs`
- 感知：同目录 `Mod_ItemDetector.cs`；空间索引与批处理在 `Core/Manager/ItemMgr.cs`
- 攻击/闲逛：`AI_AttackController.cs`、`AI_WanderUtility.cs`
- 生成：`Core/Manager/MonsterSpawnerManager.cs`、`Entities/Spawner/SpawnerConfig.cs`、`Resources/Config/SpawnerConfig*.asset`
- 存档：`World/Map/Data/{GameSaveData.MonsterSpawner,MonsterSpawnerSaveData}.cs`
- Actor 定义：`Assets/StreamingAssets/GameConfig/Actors/{actor-manifest.json,definitions/core-actors.json}`；加载器在 `Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs`

## 不变量

- 感知链为 Detector 请求 → ItemMgr 空间格粗筛 → Collider2D 精确确认 → 应用进入/离开结果。
- 现代 AI 位于 `Entities/AI/`；修改 Prefab 前确认其使用状态机还是旧 Kiwi 行为树。
- `Chicken_Tree`、`WildBoar_Tree` 是历史 Kiwi 兼容 Prefab，不实现 `IAIActor`，不加入正式 Actor JSON/MOD 继承目录。
- 狼只使用 `Assets/2_Prefabs/Entity_AI/Wolf.prefab`；不要恢复已删除的 `Wolf_Tree.prefab`。
- 正式 Chicken/WildBoar/Wolf/Ghost 由 Actor JSON 提供名称、视觉和模块参数；Prefab 只保留组件结构、事件引用与回退值。
- Actor 外壳、Sprite、Animator 使用 `flatworld.actor.*` Addressables 地址；运行时不得读取 `sourcePrefab`。
- `UnboundedDailyGrowth` 会跳过生态预算与存活上限；修改生成条件时保留其独立语义。
- 移动/可走性改动联动 `flatworld-navigation`；伤害联动 `flatworld-combat`；注册/存档联动 Item/Data Skill。

## 工作流与验证

1. 从目标 Prefab 的实际模块进入，不按类名猜运行链。
2. 随机行为使用固定种子或可注入输入；Bug 修复保留确定性回归。
3. 默认做静态诊断、编译和 Console 检查；达到测试门槛或用户要求时按 `flatworld-test-automation` 运行 `AI.Smoke`。
4. 测试入口：`Assets/GameTest/AI/AISmokeTests.cs`；真实世界行为可放入 Golden Path。

路径、Prefab 挂载、感知/目标算法、Spawner 配置或测试入口变化时更新本 Skill；近期变更最多 5 条。

## 近期变更

- 2026-08-12：Chicken、WildBoar、Wolf、Ghost 接入独立 Actor JSON 目录、稳定 Addressables 地址和 MOD Actor 继承入口。
