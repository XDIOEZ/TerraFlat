# 背包、快捷栏与手持系统

## 系统定位

负责玩家行囊、快捷栏、手部携带槽、容器、槽位交互、拾取、丢弃以及物品在库存之间的事务移动。

## 当前机制

- Inventory 是库存业务主体，UI 只是其表现。
- 玩家背包同时受**总重量**和**总体积**约束；玩家创建模板当前默认 `maxCarryWeight=60`、`maxCarryVolume=90`。
- 可堆叠物品允许继续堆叠；不可堆叠物品仍必须独占槽位。是否可堆叠由 ItemData 身份/规则判定。
- 快捷栏保存角色当前装备/选中物；`Inventory_Hand` 是交互携带槽，二者不能混为同一状态。
- PC 点击与拖放以整组事务为主；滚轮处理逐件。
- 移动端轻触、长按、拖拽有独立手势语义，最终仍复用库存事务。
- 长按把手中整组放入空槽/同类槽时使用统一时间阈值，进度显示在唯一 `UI_Hand` 手部槽位上，走满立即提交，不等待松手。
- 世界丢弃统一经过 `Module_DiscardItem`，避免不同入口各自改槽位数量。

## 关键入口

- `Assets/5_Scripts/5-3_GamePlay/Items/Inventory/Inventory.cs`
- `Mod_Inventory.cs`
- `Inventory_HotBar.cs`
- `ItemSlot_UI.cs`
- `Inventory_Hand`
- `PlayerCarryCapacityUtility.cs`

## 事务原则

- 移动、合并、交换必须先校验来源和目标双方规则，再原子提交。
- 异类交换不能临时经过手部库存中转。
- 需要从“物品实际所在库存”消耗材料时使用 `InventoryContextResolver`。
- 直接修改 `Stack.Amount` 会绕过 UI、事件和存档同步，禁止作为业务入口。

## UI 边界

- `UI_Hand` 只是顶层携带视觉与手部槽位表现，不应拦截目标槽射线。
- 通用 `UI_Slot.prefab` 不承载手部专属长按进度表现。
- 模态库存才锁玩法输入；快捷栏和 Hand 不锁。

## 修改时联动

- 配方扣料/产出：制作系统。
- 装备扩展背包：装备系统。
- 容量/重量：Player 创建模板、Item weight/volume。
- 移动端手势：UI 与 Android Input。

## 对应 Skill

`.agents/skills/flatworld-inventory-crafting/SKILL.md`
