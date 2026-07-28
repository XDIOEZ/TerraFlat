---
name: flatworld-inventory-crafting
description: "Use when: 定位或修改 FlatWorld 的背包、槽位、快捷栏、手持、容器、工作台、制作配方、装备、食物、种子、植物生长、耕地或相关 Prefab/SO。关键词：Inventory、Mod_Inventory、Crafting、Mod_Equipment、Mod_Food。"
argument-hint: "背包、制作、装备或农业问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 背包、制作、装备与农业定位

> 最后核对：2026-07-28。

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

- 配方权威目录：`Assets/StreamingAssets/GameConfig/Recipes/`；`recipe-manifest.json` 按顺序声明 8 个业务分包，启动时聚合后转换为 `RuntimeRecipe`。
- 固定分包：`crafting/{survival,tools,weapons,buildings}.json`、`cooking/{basic_food,advanced_food}.json`、`smelting/{ores,alloys}.json`。
- 配方 Excel：`Assets/GameConfig/Excel/RecipeConfig.xlsx`；`Recipes.Package` 决定配方导出分包，编辑器同步入口为 `Assets/Editor/FlatWorld/ExcelConfig/RecipeExcelSyncService.cs`。
- 运行时模型与校验：`RecipeDto.cs`、`RuntimeRecipe.cs`、`RecipeRuntimeFactory.cs`、`RecipeCatalogLoader.cs`。
- 动作执行：JSON 只保存 `action.type + 参数`，由 `RecipeActionRunner` 的 C# Handler 执行；当前支持 `change_durability`。
- 输入 `amount = 0` 表示参与配方签名匹配但不消耗，主要用于工具/催化材料；空槽同样为 0，但不填写 `itemId`/`tag`。
- 配方运行时注册：`GameRes.recipeById` 按 ID 查询，`GameRes.recipeDict` 保留旧输入签名查询兼容。
- 旧 `Recipe`/`CookRecipe` SO 只作为迁移和旧 MOD AssetBundle 兼容桥，不再是本体运行时配方来源。
- Player Prefab 实际手工制作入口是 `Assets/5_Scripts/5-3_GamePlay/Inventory/Mod_HandCraftTable.cs`；旧 `Mod_HandMade` 仅保留兼容。两者都必须在产物写入、扣料、动作和 UI 刷新全部成功后发布 `GameplayProgressEvents.CraftSucceeded(actor, stableId)`。
- 玩家背包只在自己的 Bag 最终打开后发布 `InventoryOpened`；`ItemPicker` 只在 `TryAddItem` 成功并完成状态更新后发布 `PickupSucceeded`。箱子、工作台、非玩家容器与失败入包不得推进教程。
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
- 表格配置：`Assets/GameConfig/Excel/`；配方表为 `RecipeConfig.xlsx`，保存后会校验并按 `Package` 导出多个 JSON。

## 易误判点

- `Assets/5_Scripts/5-3_GamePlay/Equipment/Module_Equipment.cs` 已废弃，优先使用 `Mod_Equipment.cs`。
- Inventory 持有数据和 UI 生命周期，但实际运行更新仍受 Item/Module Tick 调度影响。
- 配方不再依赖 `CraftingRecipe` Addressables 标签；修改 Excel 后检查清单、对应分包 JSON、物品 ID 与 `GameRes` 配方字典。

## 近期变更

- 2026-07-28：本体配方由单个 `recipes.json` 改为 `recipe-manifest.json + 8 个业务分包`；运行时仍统一注册，Excel 新增 `Package` 列控制落盘位置。
- 2026-07-28：背包打开、成功拾取和成功制作接入通用 `GameplayProgressEvents`，供 JSON 自言自语教程消费；玩法层不引用 Guide 程序集。
- 2026-07-28：本体配方由 ScriptableObject/Addressables 改为 JSON 数据驱动；增加纯运行时模型、动作 Handler、Excel 四表编辑导出和旧 SO 一次性迁移入口。
- 2026-07-27：库存类模块已纳入低频 Tick；修改库存数据更新频率时先检查 Item/Module Skill 的调度约束。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/InventoryCrafting/InventoryCraftingSmokeTests.cs`；当前基础覆盖库存模块、装备、配方和初始库存资源入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；背包制作测试约定目录：`Assets/GameTest/InventoryCrafting/`；场景目录：`Assets/GameTest/Scenes/InventoryCrafting/`；冒烟分类：`InventoryCrafting.Smoke`。
- 新增背包、槽位、快捷栏、容器、装备、配方、食物或植物行为时必须增加系统测试；修复 Bug 时先增加回归测试。物品进入背包到使用或制作主流程变化时同步更新冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试物品必须使用隔离数据并验证数量守恒、引用清理和失败路径。
- 完成修改后检查 Unity 编译和 Console，再运行 `InventoryCrafting.Smoke`；涉及 Item/Module、存档、玩家输入或 UI 时同步运行对应系统测试。
- 教程使用的拾取/制作成功锚点及稳定 ID 由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

移动 Inventory/Equipment/Food Prefab，修改配方 JSON/Excel/DTO/Handler、槽位模型、配方注册、装备入口、农业 TileData 或控件命名后，必须更新本 Skill。
