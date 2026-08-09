---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物/怪物 AI、状态机、感知、目标选择、攻击、闲逛、AI 移动、行为树兼容、怪物生成器或 AI Prefab。关键词：AI_Base、Mod_ItemDetector、AI_StateMachineRunner、MonsterSpawnerManager。"
argument-hint: "AI、感知、移动或生成问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld AI 与生物生成定位

> 最后核对：2026-08-09。移动和可走性问题请同时加载 `flatworld-navigation`。

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
- 狼：`AI_Wolf.cs`。
- Ghost：`AI_Ghost.cs`。
- AI 移动：`Assets/5_Scripts/5-3_GamePlay/Entities/Move/Mover_AI.cs`。
- 生物 Prefab：`Assets/2_Prefabs/Entity_AI/`。

## 生成系统

- 管理器：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/MonsterSpawnerManager.cs`。
- 配置类型：`Assets/5_Scripts/5-3_GamePlay/Entities/Spawner/SpawnerConfig.cs`。
- 存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/GameSaveData.MonsterSpawner.cs`、`MonsterSpawnerSaveData.cs`。
- 配置资产：`Assets/Resources/Config/SpawnerConfig*.asset`。
- 生成组：`SpawnerConfig.asset` 仅含 Chicken/WildBoar 资源动物；`SpawnerConfig_Wolves.asset` 仅含 Wolf 普通敌人；`SpawnerConfig_Ghost.asset` 为里程碑成长的夜间敌人。
- `SpawnEntry.Probability` 是归一化相对权重，并带生态成本与物种存活上限；配置同时提供组上限、玩家周边上限、预算恢复、群系、局部光照和远距离回收规则。
- `MonsterSpawnerManager` 只在 `GameNetwork.HasStateAuthority` 端生成，按 `TimeData.GetTotalGameTime()` 处理跨过的窗口；`LastProcessedTotalTime`、预算和补位债务进入存档。
- 当前加载种群由 `ItemMgr` 生命周期事件追踪；真实死亡通过 `DamageReceiver.DeathStarted` 产生补位，区块重载后的超额存量会按全局/组/物种/玩家周边上限裁剪。
- 难度的 `SpawnFrequencyMultiplier` 只改变每日窗口、里程碑速度和单次排队数量；`SpawnPopulationMultiplier` 统一缩放全局/组/物种/玩家周边上限、生态预算、恢复目标与终身生成上限。不得回写 `SpawnerConfig` 资产；倍率降低时先裁剪持久化待生成队列和预算。

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

- 新 AI：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/` 的通用状态机体系。
- 旧 Kiwi 行为树：`Assets/4_ScriptObjects/4-7_BehaviourTrees/`、`Assets/TheKiwiCoder/`。
- 修改前必须确认目标 Prefab 实际挂载状态机模块还是旧行为树。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-09：幽灵在感知范围内先锁定玩家再处理避光逻辑，追击时改用直接世界位移并跳过导航可走性/玩家黑暗检查；“光耀” Buff 仅在自身亮度严格大于 0.5 时维持，新增 AI.Smoke 阈值回归断言。
- 2026-08-09：修复现代狼与旧行为树狼的玩家感知层遮罩，统一包含 Player 层及狼根物体层；幽灵补充本地玩家 Transform 回退解析，仍保持只在完全黑暗中追击玩家，并新增 AI.Smoke 回归断言。
- 2026-08-09：野猪攻击改为横向半轴 1.6、竖向半轴 0.45 的椭圆判定；独立 `AttackTrigger_AI` 覆盖使用 `2.2×0.9`、圆角 `0.45` 的横向胶囊触发盒，通用模块和狼不受影响。
- 2026-08-09：GM 生物召唤必须直接走 `ItemMgr.InstantiateItem(...) → Item.Load()`，与 `MonsterSpawnerManager.SpawnMonster()` 保持同序；生成后以 `IAIActor.ActorItem` 复核绑定，失败通过 `ItemMgr.DespawnItem(saveData:false)` 回收，禁止遗留未初始化 AI。
- 2026-08-09：`AI_AttackController` 不再在攻击起手预先置 `IsAttacking=true`；改在 `DamageWindowStartDelay` 结束、实际伤害碰撞启用时同步置真，消除野猪 `Attack.anim` 首帧 0 与控制器 true 的冲突，首次与后续攻击共用同一事件时序。
- 2026-08-09：实体 AI 的运行时归属统一迁到 `ItemMgr` 的 `WorldModel.WorldAddress` 索引，并挂在场景级 `RuntimeEntities` 根节点；旧 `Mod_ItemChunkAssigner` 仅保留模块/存档兼容 ID，Ghost、Chicken、生成器和光照查询不得再读取旧 `Chunk/Map`。
- 2026-08-09：野猪 `Attack.anim` 的完整周期固定为 `attackDamageStartDelay + attackDamageWindow + attackCooldown`（当前为 `2.18s`），关闭 Clip 循环；局部前冲仍在 `0.06s` 命中起始、`0.18s` 窗口结束时回位，`AISmokeTests` 同时断言有效帧、完整周期与非循环。
- 2026-08-09：小鸡饥饿觅食优先查询 `ChunkMgr.TryFindRuntimeGrassNear()`，进食通过 `TryConsumeRuntimeGrass()` 原子消费 `ChunkTerrainData` 草层并由变更事件刷新渲染；旧 `Map` 草层仅作迁移回退，不再使用草 Item 实体。
- 2026-08-09：`AI_Wolf` 追击玩家时以持久化 Item Guid 保持同翼排序，优先留在自身左右翼的浅扇形槽位；仅将有限分离偏移写入缓存寻路目的地，保持 `Mover_AI → WorldNavigationAgent` 权威移动链路。槽位受攻击安全半径和左右攻击方向约束，未靠近自身槽位不会提前攻击，不可走时回退原直接追击。
- 2026-08-09：`MonsterSpawnerManager.RebuildTrackedPopulation()` 先执行 `ItemMgr.CleanupNullItems()`，`TrackItem()` 必须先以 Unity 空值语义拒绝已销毁或已处理的 Item，再访问组件；退出重进不得让旧 `GameItem` 中断 `Event_GameWorldEnter`。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/AI/AISmokeTests.cs`；当前覆盖状态机、感知、生物 Prefab、狼群追击站位的左右分线/攻击安全半径、战斗动物追击触发/感知半径、幽灵 0.5 光照伤害阈值、野猪横宽竖窄攻击椭圆、攻击窗口与动画曲线、Chicken 模板 Item/Module 字典键、生成组唯一物种归属、持久化 ID 与归一化权重分布。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；AI 测试约定目录：`Assets/GameTest/AI/`；场景目录：`Assets/GameTest/Scenes/AI/`；冒烟分类：`AI.Smoke`。
- 新增 AI 行为时必须增加系统测试；修复 Bug 时先增加可复现问题的回归测试。感知、目标选择、状态切换、攻击或闲逛主流程变化时同步更新 AI 冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试、放宽断言或改写输入来制造通过；随机行为必须固定种子或注入确定输入。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category AI.Smoke`；无需视觉模型或测试工具卡片。涉及导航、战斗、Item/Module 或生成器时追加对应分类；只有最终视觉观感改动才做定向截图。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变 AI 状态、感知半径或标签、空间格、目标算法、移动组件、Spawner 配置/资源、Prefab 挂载或新旧行为树边界后，必须更新本 Skill；影响 Item 注册或导航时同步更新对应 Skill。
