---
name: flatworld-inventory-crafting
description: "Use when: 定位或修改 FlatWorld 的背包、槽位、快捷栏、手持、容器、工作台、制作配方、装备、食物、种子、植物生长、耕地或相关 Prefab/SO。关键词：Inventory、Mod_Inventory、Crafting、Mod_Equipment、Mod_Food。"
argument-hint: "背包、制作、装备或农业问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 背包、制作、装备与农业定位

> 最后核对：2026-07-27。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Inventory/Inventory.cs`：库存数据更新、输入绑定、面板创建与容器交互。
2. `Assets/5_Scripts/5-3_GamePlay/Inventory/Mod_Inventory.cs`：Item 上的库存模块。
3. `Assets/5_Scripts/5-3_GamePlay/Crafting/`：配方动作和制作规则。
4. `Assets/5_Scripts/5-3_GamePlay/Equipment/Mod_Equipment.cs`：当前装备系统入口。

## 背包与 UI

- 基础数据：`Assets/5_Scripts/5-1_Data/CoreData/Inventory_Data.cs`、`ItemSlot.cs`、`ItemStack.cs`。
- 槽位 UI：`Assets/5_Scripts/5-3_GamePlay/Inventory/ItemSlot_UI.cs`。
- 背包 UI：`Inventory_UI.cs`。
- 快捷栏：`Inventory_HotBar.cs`。
- 手持库存：`Inventory_Hand.cs`、`Mod_Hand.cs`。
- 容器/工作台：`Mod_Box.cs`、`Mod_MakeTable.cs`、`Inventory_WorkBench.cs`。
- 初始库存 SO：`Assets/4_ScriptObjects/4-6_InventoryInit/`。

## 制作与装备

- 制作动作抽象：`Assets/5_Scripts/5-3_GamePlay/Crafting/CraftingAction.cs`。
- 配方运行时注册：`Assets/5_Scripts/5-3_GamePlay/Manager/GameRes.cs` 的 `recipeDict`。
- 装备定义：`Assets/5_Scripts/5-3_GamePlay/Equipment/Equipment_SO.cs`。
- 装备效果实例：`Assets/5_Scripts/5-3_GamePlay/Equipment/EquipmentInstance*.cs`。
- 装备存储模块：`Assets/5_Scripts/5-3_GamePlay/Equipment/Module_Equipment_Store.cs`。

## 食物与农业

- 食物模块：`Assets/5_Scripts/5-3_GamePlay/Item/Mod_Food.cs`。
- 种子/生长：`Mod_Seed.cs`、`Mod_Grow.cs`、`Mod_PlantGrow.cs`。
- 农具/杂草表现：`Assets/5_Scripts/5-3_GamePlay/Food/`。
- 耕地数据：`Assets/5_Scripts/5-1_Data/TileData/TileData_Farmland.cs`。
- 烹饪 SO：`Assets/4_ScriptObjects/4-5_Cook/`。

## 资源目录

- `Assets/2_Prefabs/Inventory/`
- `Assets/2_Prefabs/Equipment/`
- `Assets/2_Prefabs/Food/`
- `Assets/2_Prefabs/Plant/`
- `Assets/2_Prefabs/Seed/`
- `Assets/2_Prefabs/Tools/`
- 表格配置：`Assets/GameConfig/Excel/`

## 易误判点

- `Assets/5_Scripts/5-3_GamePlay/Equipment/Module_Equipment.cs` 已废弃，优先使用 `Mod_Equipment.cs`。
- Inventory 持有数据和 UI 生命周期，但实际运行更新仍受 Item/Module Tick 调度影响。
- 配方或物品资源移动后要同时检查 Addressables 标签和 `GameRes` 字典键。

## 近期变更

- 2026-07-27：库存类模块已纳入低频 Tick；修改库存数据更新频率时先检查 Item/Module Skill 的调度约束。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/InventoryCrafting/InventoryCraftingSmokeTests.cs`；当前基础覆盖库存模块、装备、配方和初始库存资源入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；背包制作测试约定目录：`Assets/GameTest/InventoryCrafting/`；场景目录：`Assets/GameTest/Scenes/InventoryCrafting/`；冒烟分类：`InventoryCrafting.Smoke`。
- 新增背包、槽位、快捷栏、容器、装备、配方、食物或植物行为时必须增加系统测试；修复 Bug 时先增加回归测试。物品进入背包到使用或制作主流程变化时同步更新冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试物品必须使用隔离数据并验证数量守恒、引用清理和失败路径。
- 完成修改后检查 Unity 编译和 Console，再运行 `InventoryCrafting.Smoke`；涉及 Item/Module、存档、玩家输入或 UI 时同步运行对应系统测试。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

移动 Inventory/Equipment/Food Prefab、制作或初始库存 SO，修改槽位模型、配方注册、装备入口、农业 TileData 或控件命名后，必须更新本 Skill。
