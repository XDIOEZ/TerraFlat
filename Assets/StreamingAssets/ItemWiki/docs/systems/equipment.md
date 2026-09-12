# 装备系统

## 系统定位

负责装备栏、装备实例、装备效果、装备 UI，以及装备给角色库存/防御/速度/水体保护等系统提供的增益。

## 当前机制

- 当前正式入口是 `Mod_Equipment`，它已经取代旧的 `Mod_Inventory + Module_Equipment` 双模块拆分方案。
- `Mod_Equipment` 同时实现库存、交互和实例 UI 契约，持有 `Inventory_Equipment`。
- 每个装备槽可以保存对应的 `EquipmentInstance` 列表，装备实例负责具体效果生命周期。
- 装备变化通过 EquipmentInventory 的数据变化事件驱动重新应用效果。
- 装备模块自己的存档包含装备库存数据与装备实例状态。
- 装备的收纳袋扩容通过 `EquipmentInstance_Bag` 动态挂接玩家行囊槽位；保存玩家行囊前会临时拆除扩展槽，保存后再恢复，避免重复写入。

## 当前装备效果类型

项目内现有实例包括：

- `EquipmentInstance_Bag`：额外收纳能力。
- `EquipmentInstance_Defense`：防御相关效果。
- `EquipmentInstance_Speed`：移动速度效果。
- `EquipmentInstance_WaterInsulation`：入水降温保护。
- `EquipmentInstance_Debug`：调试用途。

## 关键入口

- `Assets/5_Scripts/5-3_GamePlay/Items/Equipment/Mod_Equipment.cs`
- `EquipmentInstance.cs`
- `EquipmentInstance_*.cs`
- `Equipment_SO.cs`
- `Module_Equipment_Store.cs`
- 正式 UI：`Assets/2_Prefabs/2-1_UI/Gameplay/` 下的 `UI_Equipment.prefab`

## 生命周期

`Load → 初始化 EquipmentInventory → 绑定控制器和槽位事件 → 重建装备运行态 → 应用装备效果 → Save 保存实例状态 → Unload 解绑事件`

## 设计边界

- `Module_Equipment.cs` 属于废弃旧实现，不应重新成为正式入口。
- 装备加成应通过独立 EquipmentInstance 或稳定契约接入目标系统，不在装备 UI 中直接改玩家业务字段。
- 外部系统如体温只接受装备提供的保护值/修饰，不允许装备直接接管体温演算。

## 修改时联动

- 背包扩容：背包系统。
- 防御：战斗系统。
- 入水保温：生存/环境系统。
- UI 槽位与装备页面：UI 系统。

## 对应 Skill

`.agents/skills/flatworld-inventory-crafting/SKILL.md`
