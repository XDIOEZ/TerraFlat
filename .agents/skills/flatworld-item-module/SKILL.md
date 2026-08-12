---
name: flatworld-item-module
description: "Use when: 定位或修改 FlatWorld 的 Item/Module 组合架构、实体创建销毁、对象池、模块加载保存、Tick 调度、运行时注册、空间索引或网络模块序列化。关键词：ItemMgr、Item、Module、ItemMods、ItemMaker。"
---

# FlatWorld Item / Module 系统定位

> 最后核对：2026-08-07。绝大多数玩法最终挂接到 Item 的 Module。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/Entities/Item/Item.cs`：Item 生命周期、模块加载保存与局部调度缓存。
2. `Assets/5_Scripts/5-3_GamePlay/Entities/Item/Module.cs`：模块基类、`TickMode`、`FixedTickInterval`。
3. `Assets/5_Scripts/5-3_GamePlay/Entities/Item/ItemMods.cs`：按名称/ID 的模块索引与调度失效。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/ItemMgr*.cs`：`ItemMgr.cs` 保留公共 API 与 Unity 生命周期；Spawning、Perception、Players、RandomDrop partial 分别承载实例生成销毁、感知空间索引、玩家加载和随机掉落。

## 核心链路
```text
ItemMaker / ItemMgr 实例化
→ Item.itemData
→ Item.ItemMods
→ Module.ModuleInit + Load
→ ItemMgr 分级调度 Item
→ Item.Tick 调度每个 Module.ModUpdate
→ Save / Despawn / Pool Reuse
```

## 关键文件与资源
- 抽象实体：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/Item.cs`。
- 通用实体：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/GameItem.cs`。
- 玩家实体：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/Player.cs`。
- 创建入口：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/ItemMaker.cs`。
- 数据基类：`Assets/5_Scripts/5-1_Data/ItemData/ItemData.cs`。
- 地图 ItemData：`Assets/5_Scripts/5-1_Data/ItemData/Data_TileMap.cs`；格子地形栈：`Assets/5_Scripts/5-1_Data/TileData/TileStackCell.cs`。

## 调度约束
- `Module.TickMode` 可为 `EveryFrame`、`FixedInterval`、`Disabled`。
- 未声明策略的旧模块默认 `EveryFrame`；未覆盖 `ModUpdate` 的模块可自动休眠。
- `ItemMgr` 使用 EveryFrame、Fast(0.05s)、Normal(0.1s)、Slow(0.25s) 的 8 桶错帧调度。
- `ItemMods` 增删模块、对象池复用和模块配置变化必须使调度缓存失效。

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：`ItemData` 新增统一堆叠身份接口，将 `ItemSpecialData` 的 `null` 与空字符串规范为相同无特殊数据，避免旧资源与 JSON 定义生成的同 ID 材料被拆成多个堆叠。
- 2026-08-11：普通资源掉落物的 JSON 视觉位置必须以实体原点为中心；煤炭、燧石、魔法石及关联旧 Prefab 已清除误迁移的场景坐标，避免生成后固定偏离实体位置。
- 2026-08-11：`ItemModule.Smoke` 增加 Meatrack、Scarecrow、WorkBench 外壳真实模块类型回归；Golden Path 会扫描全部 `Prefab` Addressables 的脚本依赖与 Missing Script，并在初始 Berry 掉落完成归属/动画后才进入区块休眠阶段。
- 2026-08-11：刀类 JSON 家族改用接线健康且结构兼容的 `Dagger_Copper` 共享外壳，石刀/骨刀/燧石刀继续由 JSON 覆盖数据、贴图和朝向；异步加载按 `sourcePrefab` 显式预加载，Editor 强制重导入后读 AssetDatabase，Player 继续使用 Addressables。
- 2026-08-10：事件波次生物与 GM 召唤共用 `ItemMgr` 直接生成→`Load()`→`IAIActor` 绑定校验→失败回收链，避免幽灵围攻生成半初始化实体。

## 易误判点
- 远程网络视觉副本不得注册进本地 Tick、AI 感知或本地存档索引。
- `Item.OnDestroy` 与主动 `PrepareForDespawn` 有防重复逻辑，不能在外部再次保存/销毁同一 Item。
- 新模块不仅要创建脚本，还要检查 Module Prefab、ModuleData、Addressables 标签和目标 Item Prefab 挂载。
- `Items/shells/` 不会被自动扫描；新增分包必须登记进 `item-manifest.json`。修改物品最终 `shellPrefab` 后，也必须把原始定义移到对应模板包，否则加载器会按 Manifest 的 `shellPrefab` 分类约束直接报错。

## 高耦合联动
只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；单个玩法模块的内部数值调整只加载该玩法 Skill。
| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| Item 创建/加载/保存/Despawn、对象池复用或 ModuleData | `flatworld-data-save`；涉及远端状态时再加载 `flatworld-networking` | 注册与注销各一次、池化无旧订阅、序列化往返不丢模块 | `DataSave.Smoke`；联机时追加 `Networking.Smoke` |

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/ItemModule/ItemModuleSmokeTests.cs`；当前只保留 Manifest 分包聚合/外壳分类与模块规范 ID/数据重绑定两个关键契约。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；Item/Module 测试约定目录：`Assets/GameTest/ItemModule/`；场景目录：`Assets/GameTest/Scenes/ItemModule/`；冒烟分类：`ItemModule.Smoke`。
- 新增实体创建销毁、模块加载保存、Tick 调度、对象池或运行时注册行为时必须增加系统测试；修复 Bug 时先增加回归测试。Item 完整生命周期变化时同步更新冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试结束必须验证注册表、调度器、空间索引和对象池不存在残留引用。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category ItemModule.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill
改变 Item/Module 生命周期、Tick 档位、池化规则、注册索引、Prefab 目录、物品 JSON 定义或网络边界后，必须更新本 Skill；若调整具体玩法模块，也同步更新该玩法 Skill 的近期变更。
