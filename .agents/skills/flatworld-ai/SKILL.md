---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物/怪物 AI、状态机、感知、目标选择、攻击、闲逛、AI 移动、行为树兼容、怪物生成器或 AI Prefab。关键词：AI_Base、Mod_ItemDetector、AI_StateMachineRunner、MonsterSpawnerManager。"
---

# FlatWorld AI 与生物生成定位

> 最后核对：2026-08-10。移动和可走性问题请同时加载 `flatworld-navigation`。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/Entities/AI/AI_Base.cs`：通用状态机、计时器、感知刷新、模块绑定。
2. `Assets/5_Scripts/5-3_GamePlay/Entities/AI/AI_StateMachineRunner.cs`：评估、切换、Tick 流程。
3. `Assets/5_Scripts/5-3_GamePlay/Entities/AI/Mod_ItemDetector.cs`：感知请求与进入/离开集合。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/ItemMgr.cs`：空间哈希、批量感知 Job、Item 注册。

## 具体实现
- 攻击控制：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/AI_AttackController.cs`。
- 闲逛工具：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/AI_WanderUtility.cs`。
- 鸡：`AI_Chicken.cs`。
- 野猪：`AI_WildBoar.cs`。

## 生成系统
- 管理器：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/MonsterSpawnerManager.cs`。
- 配置类型：`Assets/5_Scripts/5-3_GamePlay/Entities/Spawner/SpawnerConfig.cs`。
- 存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/GameSaveData.MonsterSpawner.cs`、`MonsterSpawnerSaveData.cs`。
- 配置资产：`Assets/Resources/Config/SpawnerConfig*.asset`。

## 当前感知架构
```text
Mod_ItemDetector 提交请求
→ ItemMgr 收集相关 8×8 世界空间格候选
→ Burst Job 批量粗筛
→ 根 Collider2D.ClosestPoint 精确圆形确认
→ Detector 应用进入/离开结果
```

## 新旧 AI 边界
- 新 AI：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/` 的通用状态机体系。
- 旧 Kiwi 行为树：`Assets/4_ScriptObjects/4-7_BehaviourTrees/`、`Assets/TheKiwiCoder/`。
- 狼只保留现代状态机 Prefab：`Assets/2_Prefabs/Entity_AI/Wolf.prefab`；旧 `Wolf_Tree.prefab` 已删除，不得重新加入 Addressables 或数据表。
- 修改前必须确认目标 Prefab 实际挂载状态机模块还是旧行为树。

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：修复拉取整合后 `MonsterSpawnerManager.CanSpawnEntry` 缺少 `SpawnerConfig` 作用域导致的编译错误；配置由 `DetermineAvailableEntry` 显式传入，保留 `UnboundedDailyGrowth` 跳过生态预算的语义。
- 2026-08-12：幽灵启用 `UnboundedDailyGrowth`，第 N 晚排入 N 只幽灵，跨日时补排遗漏天数的总数；该模式忽略生态预算、物种、生成组、玩家周边与全局存活上限，并保持只在完全黑暗时分帧生成。
- 2026-08-12：狼的 `TickChase()` 在提交寻路目标前持续 `FaceTarget()` 面向当前威胁，修复追击玩家时保留旧朝向、用背部倒着奔跑的问题；不改变通用转身模块和其他动物。
- 2026-08-12：狼的主动战斗感知距离扩大为原来的 2 倍：`alertDetectDistance 10→20`、`chaseTriggerDistance 14→28`、`chaseLossDistance 22→44`；保持近战 `attackTriggerDistance=1.4`，并同步更新 `AI.Smoke` Prefab 断言。
- 2026-08-12：删除不再使用的旧行为树狼 `Wolf_Tree.prefab` 及其 `.meta`，同步移除 Addressables、食物数据表、AI 感知测试和导航 Prefab 列表引用；狼仅保留现代 `Wolf.prefab`。

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/AI/AISmokeTests.cs`；当前覆盖状态机、感知、生物 Prefab、狼群追击站位的左右分线/攻击安全半径、狼玩家/同伴感知范围、幽灵接触伤害碰撞尺寸、战斗动物追击触发/感知半径、幽灵 0.5 光照伤害阈值、AI 攻击首击重叠扫描范围、野猪横宽竖窄攻击椭圆、攻击窗口与动画曲线、Chicken 模板 Item/Module 字典键、生成组唯一物种归属、持久化 ID 与归一化权重分布。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；AI 测试约定目录：`Assets/GameTest/AI/`；场景目录：`Assets/GameTest/Scenes/AI/`；冒烟分类：`AI.Smoke`。
- 新增 AI 行为时必须增加系统测试；修复 Bug 时先增加可复现问题的回归测试。感知、目标选择、状态切换、攻击或闲逛主流程变化时同步更新 AI 冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试、放宽断言或改写输入来制造通过；随机行为必须固定种子或注入确定输入。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category AI.Smoke`；无需视觉模型或测试工具卡片。涉及导航、战斗、Item/Module 或生成器时追加对应分类；只有最终视觉观感改动才做定向截图。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill
改变 AI 状态、感知半径或标签、空间格、目标算法、移动组件、Spawner 配置/资源、Prefab 挂载或新旧行为树边界后，必须更新本 Skill；影响 Item 注册或导航时同步更新对应 Skill。
