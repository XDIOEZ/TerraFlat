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
- 移动营养消耗：`Entities/Move/Mover.cs` 与 `Mod_Food` 分别维护营养、水分的移动倍率。
- Prefab：`Assets/2_Prefabs/{Inventory,Equipment,Food,Plant,Seed,Tools}/`

## 不变量

- 配方 JSON 是唯一真源；旧 Recipe/CookRecipe SO 只作 MOD 兼容，不恢复双重维护。
- 内容工坊的合成画布上限为 3×3；保存前必须使用运行时配方工厂校验整份启用目录，并保留已有配方的未知顶层字段。
- 所有制作入口调用 `CraftingService`；匹配由 `CraftingRecipeMatcher`，扣料/产出由 `CraftingTransaction` 原子提交。
- 手工合成 `Mod_HandMade` 与工作台 `Inventory_WorkBench` 均使用 `RecipeType.Crafting`；配方 JSON 没有独立工作站字段，需要两者通用时只配置 `recipeType: "crafting"`。
- 多产物必须全部放下才提交；失败不扣料、不部分产出。体积大于 1 的非堆叠产物每个单位必须独占一个容量足够的空槽，不能把 `amount > 1` 整组塞进单槽。`amount=0` 参与签名但不消耗。
- 同一输入同时命中有序图案与无序材料配方时，必须优先选择有序图案；无序配方允许同类材料集中在大堆叠中，若先按材料项数量排序会让建筑等宽泛配方截获更具体的制作结果。无序配方的 Exact/Tag 候选可能重叠，扣料计划必须按全部需求做全局容量分配，禁止逐项贪心消耗；需要同类材料分别占据多个格子时必须使用 `ordered`，不能用重复的无序输入表达格子占用。
- 配方动作在库存事务成功后执行；异常恢复快照。玩法进度信号只在最终成功后发布。
- 制作输入变化、事务扣料和面板初始化都会被动刷新预览；此时 `RecipeNotFound` 是合法的“当前无配方”状态，应清空预览且不输出 Warning。只有用户主动提交前检查失败，或库存、产物等结构性异常，才输出制作诊断。
- 制作模块的 `Save()` 只负责持久化，不能解绑输入、输出、按钮或交互监听；这些运行时事件统一在 `Unload()` 中成对清理，由 Item 退出、移除模块与回池生命周期调用，否则自动保存会让预览与制作按钮永久失效。
- 模态库存才获取输入锁；快捷栏和 `Inventory_Hand` 不锁玩家输入。
- 槽位鼠标与触屏拖放必须复用 `ItemSlot_UI.OnMouseDragBegin` / `OnMouseDragDrop` 的定向事务：短按在抬起时提交一次，物品槽越过系统拖拽阈值后先拿起到玩家手部槽，再在目标槽位抬起放下；未命中任何 `ItemSlot_UI` 时保留整组在 `Inventory_Hand`，后续点击按轻触方向处理：连续拿取方向下同类已有物品从槽位取一件，放置方向下空槽/同类槽向目标放一件，异类槽交换，长按空槽或同类槽则一次性放下手上整组；同类物品合并到目标槽，空槽放入，异类交换，容量不足保留手部余量，空槽起手才转交父级 `ScrollRect`，同时保留长按菜单。
- 手机快捷栏轻触必须走独立 `OnTouchTap` 语义，只切换当前选中格或按单件规则取放；触屏拖放必须走 `OnMouseDragDrop` 定向事务，桌面键鼠行为不得改变。
- 跟随指针的 `UI_Hand` 是纯视觉层：Canvas 排序固定高于快捷栏模态层（1001 > 1000），且 CanvasGroup/子图形不得拦截目标槽位射线；世界手持物挂在快捷栏节点及其子节点末端。
- 快捷栏选中框属于当前槽位背景层，切换时必须重新挂到目标槽位并置为首个兄弟；数量文本和物品图标保持在其上方，不能依赖独立 Canvas 的任意 `sortingOrder`。
- 玩家行囊的键鼠点击无条件使用 `Inventory_Hand`，不能因携带槽为空而回退当前快捷栏；快捷栏选中槽只参与手柄确认与角色当前装备，不参与 PC 背包交换。
- 快捷栏收到 Mobile `RightClick` 时必须允许当前手持物执行 `Act`，不能因触点位于手机“使用”按钮上而被 `IsPointerOverUI()` 拦截；键鼠右键仍保留 UI 遮挡检查。
- 快捷栏生成的手持物只注册到玩家 `Mod_FocusPoint`；左右翻身角由该模块读取 `Mod_TurnBack.CurrentTurnAngleY` 后与 Z 轴瞄准一次性合成，不能再把手持物根节点注册进 `controlledTransforms_Direction`。
- 丢弃统一经过 `Module_DiscardItem.DropItemByCount`；扣减 `ItemSlot.Amount` 后除触发槽位事件外，还必须按快捷栏槽位索引显式刷新 UI，兼容手机入口没有 `ItemSlot_UI` 引用的情况。
- 快捷栏拖拽到非 UI 区域后的整组丢弃由 `ItemSlot_UI` 世界长按回调转发到 `Module_DiscardItem`，落点使用触点屏幕坐标；UI 槽位长按放置路径保持独立。
- 快捷栏物品拖入 `Inventory_Hand` 后，移动端摇杆必须让出当前触摸所有权，避免长按世界丢弃时浮动摇杆抢占操作。
- 手机端已经拿起物品后的再次长按丢弃由 `MobileHeldItemDropSurface` 统一转发到 `Module_DiscardItem`；中间空白触控面只在 `Inventory_Hand` 有物品时参与射线，`ItemSlot_UI` 的拖拽射线必须继续把该组件视为世界落点。
- `Mod_Plantable` 只通过 `IPlantableCrop` 初始化幼苗并判断地块占用；作物定义只配置 `cropItemId`，统一 `PlantingSummoner` 负责预览，禁止写死依赖某个成长模块或复用 `Mod_Building` 链路。
- 普通农作物使用 `CropShell + Mod_Crop + Mod_CropYield + Mod_CropVisual`：`Mod_Crop` 只保存两阶段权威状态并调度 `ICropHarvestAction`，产物表和其他收获副作用必须拆成独立动作模块。
- 世界植株与收获物必须保留独立 Item ID；种下时把植株重置为幼苗，成熟交互后由动作生成食物/种子并销毁植株，不能把世界植株直接改成食物实例。
- `Mod_Grow` 继续承担树木与自然植物成长，并实现 `IPlantableCrop` 接入同一播种入口；水肥、天气与 `CropGrowthMultiplier` 在权威成长模块中各结算一次。
- 使用 `_BodyClip` 裁剪作物精灵时，必须给 `Mod_CropVisual` 绑定支持该属性的 `Sprite-Lit-Master` 材质；通用 `Prop` 外壳默认材质不提供 BodyClip。
- 废弃 `Module_Equipment.cs` 不再使用。
- `Mod_Food` 的被动生命联动必须读取 `Mod_PlayerDeathState`；玩家濒死或 `DamageReceiver.Hp <= 0` 时停止回血与生存伤害，避免死亡状态被抬成极低正数。
- `Mod_Food.HealthState` 的回血判定只看蛋白质；`HealInterval/HealAmount` 大于 0 时按间隔一次性回血，动物继续使用 `HealNeedRatio`，玩家创建模板通过 `proteinHealThreshold` 配置绝对蛋白质门槛。

## 验证

- 覆盖满包、回滚、镜像/Tag/紧凑网格、快捷栏/手持同步、输入锁和作物存档往返。
- UI 契约联动 UI Skill；Item 生命周期联动 Item Skill；配方/农业存档联动 Data Skill。
- JSON 物品迁移时可以暂不填写 `visual` 图标；库存槽位的统一显示入口必须回退到 shell Prefab 的 `SpriteRenderer`，否则已有物品会在快捷栏中变成空槽。
- 默认不主动跑测试；需要时运行 `InventoryCrafting.Smoke`，专项用 `.Core`/`.Agriculture`。测试目录：`Assets/GameTest/InventoryCrafting/`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
