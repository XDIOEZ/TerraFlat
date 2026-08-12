---
name: flatworld-inventory-crafting
description: "Use when: 定位或修改 FlatWorld 的背包、槽位、快捷栏、手持、容器、工作台、制作配方、装备、食物、种子、植物生长、耕地或相关 Prefab/SO。关键词：Inventory、Mod_Inventory、Crafting、Mod_Equipment、Mod_Food。"
---

# FlatWorld 背包、制作与农业

## 入口

- 库存：`Assets/5_Scripts/5-3_GamePlay/Items/Inventory/{Inventory,Mod_Inventory,Inventory_UI,Inventory_HotBar,ItemSlot_UI}.cs`
- 制作：`Assets/5_Scripts/5-3_GamePlay/Items/Crafting/`
- 配方真源：`Assets/StreamingAssets/GameConfig/Recipes/recipe-manifest.json` 及分包 JSON
- 配方可视化编辑：`Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`，Unity 菜单为 `FlatWorld/内容配置/内容工坊`
- 装备：`Items/Equipment/{Mod_Equipment,Equipment_SO,EquipmentInstance*,Module_Equipment_Store}.cs`
- 食物/农业：`Entities/Item/Mod_Food.cs`、种子/成长模块与 `Mod_Grow.AuthoritativeCrop.cs`
- Prefab：`Assets/2_Prefabs/{Inventory,Equipment,Food,Plant,Seed,Tools}/`

## 不变量

- 配方 JSON 是唯一真源；旧 Recipe/CookRecipe SO 只作 MOD 兼容，不恢复双重维护。
- 内容工坊的合成画布上限为 3×3；保存前必须使用运行时配方工厂校验整份启用目录，并保留已有配方的未知顶层字段。
- 所有制作入口调用 `CraftingService`；匹配由 `CraftingRecipeMatcher`，扣料/产出由 `CraftingTransaction` 原子提交。
- 多产物必须全部放下才提交；失败不扣料、不部分产出。`amount=0` 参与签名但不消耗。
- 配方动作在库存事务成功后执行；异常恢复快照。玩法进度信号只在最终成功后发布。
- 模态库存才获取输入锁；快捷栏和 `Inventory_Hand` 不锁玩家输入。
- 当前作物闭环为 `Seed_Apple → AppleTree → Apple + Seed_Apple`；`Mod_Grow` 是唯一成长状态机，倍率各乘一次。
- 废弃 `Module_Equipment.cs` 不再使用。

## 验证

- 覆盖满包、回滚、镜像/Tag/紧凑网格、快捷栏/手持同步、输入锁和作物存档往返。
- UI 契约联动 UI Skill；Item 生命周期联动 Item Skill；配方/农业存档联动 Data Skill。
- 默认不主动跑测试；需要时运行 `InventoryCrafting.Smoke`，专项用 `.Core`/`.Agriculture`。测试目录：`Assets/GameTest/InventoryCrafting/`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
