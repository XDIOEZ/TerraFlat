---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物/怪物 AI、状态机、感知、目标选择、攻击、闲逛、AI 移动、行为树兼容、怪物生成器或 AI Prefab。关键词：AI_Base、Mod_ItemDetector、AI_StateMachineRunner、MonsterSpawnerManager。"
argument-hint: "AI、感知、移动或生成问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld AI 与生物生成定位

> 最后核对：2026-07-27。移动和可走性问题请同时加载 `flatworld-navigation`。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/AI/AI_Base.cs`：通用状态机、计时器、感知刷新、模块绑定。
2. `Assets/5_Scripts/5-3_GamePlay/AI/AI_StateMachineRunner.cs`：评估、切换、Tick 流程。
3. `Assets/5_Scripts/5-3_GamePlay/AI/Mod_ItemDetector.cs`：感知请求与进入/离开集合。
4. `Assets/5_Scripts/5-3_GamePlay/Manager/ItemMgr.cs`：空间哈希、批量感知 Job、Item 注册。

## 具体实现

- 攻击控制：`Assets/5_Scripts/5-3_GamePlay/AI/AI_AttackController.cs`。
- 闲逛工具：`Assets/5_Scripts/5-3_GamePlay/AI/AI_WanderUtility.cs`。
- 鸡：`AI_Chicken.cs`。
- 野猪：`AI_WildBoar.cs`。
- 狼：`AI_Wolf.cs`。
- Ghost：`AI_Ghost.cs`。
- AI 移动：`Assets/5_Scripts/5-3_GamePlay/Move/Mover_AI.cs`。
- 生物 Prefab：`Assets/2_Prefabs/Entity_AI/`。

## 生成系统

- 管理器：`Assets/5_Scripts/5-3_GamePlay/Manager/MonsterSpawnerManager.cs`。
- 配置类型：`Assets/5_Scripts/5-3_GamePlay/Spawner/SpawnerConfig.cs`。
- 存档：`Assets/5_Scripts/5-3_GamePlay/Map/Data/GameSaveData.MonsterSpawner.cs`、`MonsterSpawnerSaveData.cs`。
- 配置资产：`Assets/Resources/Config/SpawnerConfig*.asset`。

## 当前感知架构

```text
Mod_ItemDetector 提交请求
→ ItemMgr 收集相关 8×8 世界空间格候选
→ Burst Job 批量粗筛
→ 根 Collider2D.ClosestPoint 精确圆形确认
→ Detector 应用进入/离开结果
```

- 动态逐帧 Item 每帧刷新空间格，只有跨格时修改 HashSet。
- Chunk Add/Remove、Item Load、位置恢复和对象池复用都必须同步索引。
- `AI_Base` 使用 Item Guid/InstanceID 生成确定性刷新相位，避免同类 AI 同帧集中查询。
- 最近目标使用线性平方距离扫描，避免 LINQ 排序与临时集合。

## 新旧 AI 边界

- 新 AI：`Assets/5_Scripts/5-3_GamePlay/AI/` 的通用状态机体系。
- 旧 Kiwi 行为树：`Assets/4_ScriptObjects/4-7_BehaviourTrees/`、`Assets/TheKiwiCoder/`。
- 修改前必须确认目标 Prefab 实际挂载状态机模块还是旧行为树。

## 近期变更

- 2026-07-27：感知从 `Physics2D.OverlapCircleAll + LINQ` 切换为 `ItemMgr` 空间哈希 + 批量 Job + 精确 Collider 确认。
- 2026-07-27：`Mod_ItemDetector` 复用集合；鸡/野猪目标选择改为无分配线性扫描；旧 Kiwi 相关节点也移除热路径 LINQ。
- 2026-07-27：AI 检测刷新错峰，减少大量同类 AI 的同帧尖峰。

## 修改后维护本 Skill

改变 AI 状态、感知半径或标签、空间格、目标算法、移动组件、Spawner 配置/资源、Prefab 挂载或新旧行为树边界后，必须更新本 Skill；影响 Item 注册或导航时同步更新对应 Skill。
