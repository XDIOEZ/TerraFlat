---
name: flatworld-item-module
description: "Use when: 定位或修改 FlatWorld 的 Item/Module 组合架构、实体创建销毁、对象池、模块加载保存、Tick 调度、运行时注册、空间索引或网络模块序列化。关键词：ItemMgr、Item、Module、ItemMods、ItemMaker。"
argument-hint: "Item、Module、Tick 或对象池问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld Item / Module 系统定位

> 最后核对：2026-07-30。绝大多数玩法最终挂接到 Item 的 Module。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Item/Item.cs`：Item 生命周期、模块加载保存与局部调度缓存。
2. `Assets/5_Scripts/5-3_GamePlay/Item/Module.cs`：模块基类、`TickMode`、`FixedTickInterval`。
3. `Assets/5_Scripts/5-3_GamePlay/Item/ItemMods.cs`：按名称/ID 的模块索引与调度失效。
4. `Assets/5_Scripts/5-3_GamePlay/Manager/ItemMgr.cs`：全局注册、对象池、分桶 Tick、玩家索引、AI 感知空间索引。

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

- 抽象实体：`Assets/5_Scripts/5-3_GamePlay/Item/Item.cs`。
- 通用实体：`Assets/5_Scripts/5-3_GamePlay/Item/GameItem.cs`。
- 玩家实体：`Assets/5_Scripts/5-3_GamePlay/Item/Player.cs`。
- 创建入口：`Assets/5_Scripts/5-3_GamePlay/Item/ItemMaker.cs`。
- 数据基类：`Assets/5_Scripts/5-1_Data/ItemData/ItemData.cs`。
- 联网序列化：`Assets/5_Scripts/5-3_GamePlay/Item/ItemNetworkStateSerialization.cs`。
- 远端模块边界：`Assets/5_Scripts/5-3_GamePlay/Item/IRemoteNetworkModule.cs`。
- Item Prefab：`Assets/2_Prefabs/Item/`。
- Module Prefab：`Assets/2_Prefabs/Module/`。

## 调度约束

- `Module.TickMode` 可为 `EveryFrame`、`FixedInterval`、`Disabled`。
- 未声明策略的旧模块默认 `EveryFrame`；未覆盖 `ModUpdate` 的模块可自动休眠。
- `ItemMgr` 使用 EveryFrame、Fast(0.05s)、Normal(0.1s)、Slow(0.25s) 的 8 桶错帧调度。
- `ItemMods` 增删模块、对象池复用和模块配置变化必须使调度缓存失效。
- FixedInterval 模块接收真实累计 `deltaTime`，不要假设每次调用等于固定间隔。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-07-30：农业模块边界收敛；`Mod_Seed` 的低频 Tick 仅迁移旧落地种子，`Mod_Grow` 低频 Tick 成为唯一作物成长与成熟状态机，`Mod_FarmlandSupply` 为休眠模块且仅响应物品使用事件；`Item.ModuleLoad()` 会先清理 Apple/AppleTree 的废弃农业模块数据再执行缺失模块自动修复。
- 2026-07-30：删除无引用且未完成的 `Mod_HealthPoints`，禁止通过通用 Module 重新建立与 `DamageReceiver` 并行的生命值状态。
- 2026-07-29：统一内容校验器建立 Prefab 名与 `ItemData.IDName` 注册快照，报告重复/覆盖键、模块数据空值与 ID、模块 Prefab 可解析性、`ModuleDataDic` 键值一致性、Missing Script/序列化丢失引用及显示名/描述污染。
- 2026-07-27：Item/Module 已采用声明式分级调度；堆肥/晾肉/生产 0.5s，库存/生长/种子/温度/GPS 0.25s，食物/熔炉/打火工具 0.1s，门/动画接收器/体力 UI 可休眠。
- 2026-07-27：`ItemMgr` 的感知空间索引同时服务 AI；修改实体注册、移动同步或对象池时必须检查 AI Skill。

## 易误判点

- 远程网络视觉副本不得注册进本地 Tick、AI 感知或本地存档索引。
- `Item.OnDestroy` 与主动 `PrepareForDespawn` 有防重复逻辑，不能在外部再次保存/销毁同一 Item。
- 新模块不仅要创建脚本，还要检查 Module Prefab、ModuleData、Addressables 标签和目标 Item Prefab 挂载。
- 旧 `Item/Mod_HealthPoints.cs` 已确认无代码或资源引用并删除；实体生命值不是通用 Item Module 扩展点，统一由战斗系统 `DamageReceiver` 管理。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/ItemModule/ItemModuleSmokeTests.cs`；当前基础覆盖ItemMgr、Item、Module 与 Item/Module Prefab 入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；Item/Module 测试约定目录：`Assets/GameTest/ItemModule/`；场景目录：`Assets/GameTest/Scenes/ItemModule/`；冒烟分类：`ItemModule.Smoke`。
- 新增实体创建销毁、模块加载保存、Tick 调度、对象池或运行时注册行为时必须增加系统测试；修复 Bug 时先增加回归测试。Item 完整生命周期变化时同步更新冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试结束必须验证注册表、调度器、空间索引和对象池不存在残留引用。
- 完成修改后检查 Unity 编译和 Console，再运行 `ItemModule.Smoke`；涉及存档、背包、战斗、地图或联机序列化时同步运行对应系统测试。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变 Item/Module 生命周期、Tick 档位、池化规则、注册索引、Prefab 目录或网络边界后，必须更新本 Skill；若调整具体玩法模块，也同步更新该玩法 Skill 的近期变更。
