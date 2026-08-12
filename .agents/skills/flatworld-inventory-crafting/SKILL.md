---
name: flatworld-inventory-crafting
description: "Use when: 定位或修改 FlatWorld 的背包、槽位、快捷栏、手持、容器、工作台、制作配方、装备、食物、种子、植物生长、耕地或相关 Prefab/SO。关键词：Inventory、Mod_Inventory、Crafting、Mod_Equipment、Mod_Food。"
---

# FlatWorld 背包、制作、装备与农业定位

> 最后核对：2026-08-11。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/Items/Inventory/Inventory.cs`：库存数据更新、输入绑定、面板创建与容器交互。
2. `Assets/5_Scripts/5-3_GamePlay/Items/Inventory/Mod_Inventory.cs`：Item 上的库存模块。
3. `Assets/5_Scripts/5-3_GamePlay/Items/Crafting/`：配方动作和制作规则。
4. `Assets/5_Scripts/5-3_GamePlay/Items/Equipment/Mod_Equipment.cs`：当前装备系统入口。

## 背包与 UI
- 基础数据：`Assets/5_Scripts/5-1_Data/CoreData/Inventory_Data.cs`、`ItemSlot.cs`、`ItemStack.cs`。
- 槽位 UI：`Assets/5_Scripts/5-3_GamePlay/Items/Inventory/ItemSlot_UI.cs`。
- 背包 UI：`Inventory_UI.cs`。
- 快捷栏：`Inventory_HotBar.cs`。

## 制作与装备
- 配方权威目录：`Assets/StreamingAssets/GameConfig/Recipes/`；`recipe-manifest.json` 按顺序声明 8 个业务分包，启动时聚合后转换为 `RuntimeRecipe`。
- 固定分包：`crafting/{survival,tools,weapons,buildings}.json`、`cooking/{basic_food,advanced_food}.json`、`smelting/{ores,alloys}.json`。
- `crafting/buildings.json` 包含 `core:矿坑入口`：8 个 `Ore_Stone` + 中心 `Log`，产出 `MineEntrance_Summoner`。
- 配方 JSON 是唯一编辑源；直接维护 manifest 声明的 8 个业务分包。建筑生成器需变更产物 ID 时通过 `Assets/Editor/FlatWorld/ContentTools/Items/RecipeJsonEditorService.cs` 定向修改对应 JSON。
- 运行时模型与校验：`RecipeDto.cs`、`RuntimeRecipe.cs`、`RecipeRuntimeFactory.cs`、`RecipeCatalogLoader.cs`。
- 制作公共核心：`CraftingRecipeMatcher.cs` 负责有序/无序、ExactItem/Tag、镜像和紧凑网格匹配并返回精确槽位消费计划；`CraftingTransaction.cs` 在深拷贝快照上统一扣料和放置全部产物，并通过 `TryCreateGrant` 为任务等奖励提供“不扣料、全放入才提交”的原子发放；`CraftingService.cs` 统一预览、提交、动作与成功事件；`CraftingResult.cs` 提供失败原因和入口能力。
- 动作执行：JSON 只保存 `action.type + 参数`，由 `RecipeActionRunner` 的 C# Handler 执行；当前支持 `change_durability`。
- 输入 `amount = 0` 表示参与配方签名匹配但不消耗，主要用于工具/催化材料；空槽同样为 0，但不填写 `itemId`/`tag`。
- 配方运行时注册：`GameRes.recipeById` 按 ID 查询，`GameRes.recipeDict` 保留旧输入签名查询兼容。
- 旧 `Recipe`/`CookRecipe` 类型只作为旧 MOD AssetBundle 兼容桥；本体旧配方 SO 目录 `Assets/4_ScriptObjects/4-4_Composite/`、`Assets/4_ScriptObjects/4-5_Cook/` 已删除，禁止恢复为双重真源。
- 四个制作入口 `Mod_HandMade`、`Mod_HandCraftTable`、`Mod_MakeTable`、`Inventory_WorkBench` 只保留点击进度、库存引用、`CraftingCapabilities` 和成功表现，制作与预览必须调用 `CraftingService`，禁止恢复入口私有匹配、容量、扣料或产出算法。
- `CraftingService.TryPrepareOutputs()` 是通用制作产量难度入口；两套熔炉兼容实现 `Mod_Furnace` 与 `Inventory_Furnace` 也必须同步应用制作产量和熔炼速度倍率，避免不同入口结果不一致。
- `Mod_HandCraftTable` 固定 2x2，并允许产物在输出槽不足时写入扣料后释放的输入槽；大工作台允许在正方形输入网格中匹配最小包围区域。
- 制作容量预检禁止直接依赖 `Inventory_Data.TryAddItem(..., false)`，因为其成功语义允许部分容纳；多输出必须在同一快照中全部放置成功后才可提交，失败不得扣料或留下部分产物。
- 配方动作成功后再完成库存通知并发布 `GameplayProgressEvents.CraftSucceeded(actor, stableId)`；动作异常必须恢复事务快照，进度事件监听器异常只记录日志，不反向撤销已完成制作。
- 玩家背包只在自己的 Bag 最终打开后发布 `InventoryOpened`；`ItemPicker` 只在 `TryAddItem` 成功并完成状态更新后发布 `PickupSucceeded`。箱子、工作台、非玩家容器与失败入包不得推进教程。
- 装备定义：`Assets/5_Scripts/5-3_GamePlay/Items/Equipment/Equipment_SO.cs`。
- 装备效果实例：`Assets/5_Scripts/5-3_GamePlay/Items/Equipment/EquipmentInstance*.cs`。
- 装备存储模块：`Assets/5_Scripts/5-3_GamePlay/Items/Equipment/Module_Equipment_Store.cs`。

## 食物与农业
- 食物模块：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/Mod_Food.cs`。
- 首种权威作物固定为 `Seed_Apple → AppleTree → Apple + Seed_Apple`：`Seed_Apple` 是唯一播种入口，`Apple` 只作为食物；成熟交互固定返还 1 颗种子，食物产量可受世界掉落倍率影响，因此循环可持续但不会指数增殖。
- `Mod_Seed` 只负责耕地/水肥/同格占用校验、扣除种子和直接创建 `AppleTree`；旧存档中的落地种子会把原进度迁移给 `Mod_Grow`，不再自行结算成长。
- `Mod_Grow` 是唯一成长状态机；扩展实现位于 `Mod_Grow.AuthoritativeCrop.cs`，在单点公式中各乘一次耕地、水肥、天气和 `CropGrowthMultiplier`，保存种植格、进度、阶段、成熟、已收获、反馈状态和自然环境初始化状态。

## 资源目录
- `Assets/2_Prefabs/Inventory/`
- `Assets/2_Prefabs/Equipment/`
- `Assets/2_Prefabs/Food/`
- `Assets/2_Prefabs/Plant/`
- `Assets/2_Prefabs/Seed/`
- `Assets/2_Prefabs/Tools/`

## 易误判点
- `Assets/5_Scripts/5-3_GamePlay/Items/Equipment/Module_Equipment.cs` 已废弃，优先使用 `Mod_Equipment.cs`。
- 遗迹容器内容不使用空壳 `Mod_Box`，由 `StructureContainerContents` 配置并在结构物件 `Load()` 后写入 `Mod_Inventory`；固定槽位配置会完整覆盖目标库存，空配置可表示空箱子。
- Inventory 持有数据和 UI 生命周期，但实际运行更新仍受 Item/Module Tick 调度影响。
- `Inventory.EnsurePanelCreated()` 只允许真正的模态库存订阅 `Opened/Closed` 输入锁并在创建后归一为关闭态；常驻快捷栏与内部 `Inventory_Hand` 必须保持无模态输入锁，避免玩家出生后移动、奔跑和交互被常驻 HUD 阻塞。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-11：`CraftingTransaction.TryCreateGrant` 开放通用原子物品发放：所有奖励先在同一库存快照完整放置，空间不足不提交任何物品；任务 `item.grant` 复用此入口并在完成态前提交。
- 2026-08-11：修正库存面板首次创建的输入锁契约：模态背包先关闭再由公开入口打开并触发锁事件，快捷栏/手部库存排除在锁与手柄模态焦点外；Golden Path 联合验证背包制作与 `ui.inventory-panel` 的锁定/释放。
- 2026-08-09：拆分库存槽位的键鼠点击与手柄确认入口；键鼠在手上已有物品时保留手部交换，手柄使用当前快捷栏槽位，避免两种交换状态互相占用。
- 2026-08-09：BerryBush 的新版生态掉落物挂到 `NaturalItems`，采摘不再通过旧 `ChunkMgr` 触发区块加载；临时浆果由自然物表现层在区块解绑时回收。
- 2026-08-09：手柄 B 默认改为返回，不再打开背包；键盘 B 保留背包开关，库存面板统一接入 `BasePanel` 的手柄取消关闭。
- 2026-08-09：快捷栏 HUD 槽位关闭 EventSystem 手柄导航，并跳过通用背包面板焦点准备；左摇杆移动不再改动当前快捷栏，十字键切换保持不变。
- 2026-08-09：键盘 B 开关库存不再让全局取消栈抢先消费；手柄 B 统一返回并通过 `BasePanel` 关闭打开的库存，同时回收该库存打开的右键菜单和物品详情。
- 2026-08-09：修正背包键鼠冲突：保留槽位橙色焦点框，但运行时 UI 导航完全删除 W/A/S/D，键盘移动不再切换选中槽位，手柄导航保持原行为。
- 2026-08-09：玩家背包左键改为写入快捷栏当前选中槽并立即同步手持实例，避免物品只进入不可见的 `Inventory_Hand` 缓冲槽；箱子、工作台等独立库存仍保留手部交换。
- 2026-08-09：库存槽位、动态模块按钮和物品上下文菜单接入手柄焦点、主要/次要操作、滚动跟随与嵌套返回，避免手柄在纯统计条和滚动条上浪费导航步数。
## 修改后自动测试
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：淡水长按饮用通过玩家 `Mod_Food.Data.nutrition.Water` 权威值每秒增加 25 并触发 `DataUpdate` 刷新水分条；该入口不创建食物物品、不触发 `ConsumeCompleted`，最大值继续由 `Nutrition.Max_Water` 限制。
- 2026-08-12：统一库存堆叠身份判定：同 ID 材料的 `ItemSpecialData` 为 `null` 或空字符串时视为相同，修复旧 Prefab/存档木棍与新版 JSON 木棍无法合并，并同步覆盖拾取、拖拽、转移、整理和制作产物入库。
- 2026-08-12：手工制作面板在 2x2 配方匹配后显示输出物半透明虚影与“可以开始制作”提示，制作按钮仅在可制作时启用；逐次点击继续由下往上填充实体产物图。
- 2026-08-11：快捷栏手持物可在根节点配置 `HandAimOrientation` 修正美术主轴；火把使用 -90°，让火苗端随鼠标方向旋转且不改变落地姿态。
- 2026-08-11：修正库存面板首次创建的输入锁契约：模态背包先关闭再由公开入口打开并触发锁事件，快捷栏/手部库存排除在锁与手柄模态焦点外；Golden Path 联合验证背包制作与 `ui.inventory-panel` 的锁定/释放。

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/InventoryCrafting/InventoryCraftingSmokeTests.cs`；当前覆盖库存模块、装备、配方、制作公共核心、初始库存资源入口、快捷栏初始选框对齐与切换父节点稳定性，以及 `UI_Bag` 整理按钮和 `UI_Slot` 制作预览图层命名契约。
- 农业闭环测试：`Assets/GameTest/InventoryCrafting/AgricultureLoopTests.cs`，分类 `InventoryCrafting.Agriculture`；覆盖唯一播种入口、AppleTree 单一 `Mod_Grow`、无无限生产/旧成长模块、肥料补给、倍率单次结算、水肥边界和成长状态 MemoryPack 往返。
- 公共核心回归：`Assets/GameTest/InventoryCrafting/CraftingCoreTests.cs`，分类 `InventoryCrafting.Core`；覆盖失败不扣料、多输出空间不足不部分产出、输入槽回落、镜像 + Tag + 紧凑网格消费映射和无序配方多余输入拒绝。
- 真实单机链由 Golden Path 操作 `inventory.crafting` 覆盖：从正式目录执行 `core:打制石器`，把 `ChippedTool` 写入玩家背包并在退出前完整恢复背包；与 `quest.progression` 联合时还验证正式制作信号和原子任务奖励。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；背包制作测试约定目录：`Assets/GameTest/InventoryCrafting/`；场景目录：`Assets/GameTest/Scenes/InventoryCrafting/`；冒烟分类：`InventoryCrafting.Smoke`。
- 新增背包、槽位、快捷栏、容器、装备、配方、食物或植物行为时必须增加系统测试；修复 Bug 时先增加回归测试。物品进入背包到使用或制作主流程变化时同步更新冒烟场景。

## 修改后维护本 Skill
移动 Inventory/Equipment/Food Prefab，修改配方 JSON/DTO/Handler、槽位模型、配方注册、装备入口、农业 TileData 或控件命名后，必须更新本 Skill。
