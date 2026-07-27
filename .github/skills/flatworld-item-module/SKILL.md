---
name: flatworld-item-module
description: "Use when: 定位或修改 FlatWorld 的 Item/Module 组合架构、实体创建销毁、对象池、模块加载保存、Tick 调度、运行时注册、空间索引或网络模块序列化。关键词：ItemMgr、Item、Module、ItemMods、ItemMaker。"
argument-hint: "Item、Module、Tick 或对象池问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld Item / Module 系统定位

> 最后核对：2026-07-27。绝大多数玩法最终挂接到 Item 的 Module。

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

- 2026-07-27：Item/Module 已采用声明式分级调度；堆肥/晾肉/生产 0.5s，库存/生长/种子/温度/GPS 0.25s，食物/熔炉/打火工具 0.1s，门/动画接收器/体力 UI 可休眠。
- 2026-07-27：`ItemMgr` 的感知空间索引同时服务 AI；修改实体注册、移动同步或对象池时必须检查 AI Skill。

## 易误判点

- 远程网络视觉副本不得注册进本地 Tick、AI 感知或本地存档索引。
- `Item.OnDestroy` 与主动 `PrepareForDespawn` 有防重复逻辑，不能在外部再次保存/销毁同一 Item。
- 新模块不仅要创建脚本，还要检查 Module Prefab、ModuleData、Addressables 标签和目标 Item Prefab 挂载。

## 修改后维护本 Skill

改变 Item/Module 生命周期、Tick 档位、池化规则、注册索引、Prefab 目录或网络边界后，必须更新本 Skill；若调整具体玩法模块，也同步更新该玩法 Skill 的近期变更。
