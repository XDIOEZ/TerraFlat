---
name: flatworld-inventory-crafting
description: "Use when: 定位或修改 FlatWorld 的背包、槽位、快捷栏、手持、容器、工作台、制作配方、装备、食物、种子、植物生长、耕地或相关 Prefab/SO。关键词：Inventory、Mod_Inventory、Crafting、Mod_Equipment、Mod_Food。"
argument-hint: "背包、制作、装备或农业问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 背包、制作、装备与农业定位

> 最后核对：2026-08-08。

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
- 手柄基础入口：B 返回、十字键上打开装备、下打开手工制作、左右独立切换快捷栏；键盘 B 仍打开背包，快捷栏动作不得复用 `CtrlMouse` 相机缩放。
- 库存/装备/手工制作面板打开时通过 `BasePanel.Opened/Closed` 持有玩家输入锁；自身开关键仍允许关闭当前面板，其他面板锁定期间不得串开。
- 背包槽位的橙色焦点框保留用于手柄；键鼠模式的 W/A/S/D 必须只移动玩家，不能经 `FlatWorldUI/Navigate` 切换槽位焦点。
- 手持库存：`Inventory_Hand.cs`、`Mod_Hand.cs`。
- 容器/工作台：`Mod_Box.cs`、`Mod_MakeTable.cs`、`Inventory_WorkBench.cs`。
- 初始库存 SO：`Assets/4_ScriptObjects/4-6_InventoryInit/`。
- 背包面板 Prefab：`Assets/2_Prefabs/2-1_UI/InventoryUI/UI_Bag.prefab`；`整理` Button 必须预制，`InventorySortButton` 只负责查找、绑定和调用 `SortDefault()`。
- 槽位 Prefab：`Assets/2_Prefabs/2-1_UI/InventoryUI/UI_Slot.prefab`；制作产物预览必须预制 `Crafting Output Ghost` 与 `Crafting Output Reveal` 两个 Image，`CraftingOutputPreview` 只更新 Sprite、颜色、显隐和填充量。
- 上述视觉节点由 `Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.cs` 的菜单 `FlatWorld/UI/Rebuild Runtime Prefab UI` 固化；运行时禁止缺失时补建视觉节点。

## 制作与装备

- 配方权威目录：`Assets/StreamingAssets/GameConfig/Recipes/`；`recipe-manifest.json` 按顺序声明 8 个业务分包，启动时聚合后转换为 `RuntimeRecipe`。
- 固定分包：`crafting/{survival,tools,weapons,buildings}.json`、`cooking/{basic_food,advanced_food}.json`、`smelting/{ores,alloys}.json`。
- `crafting/buildings.json` 包含 `core:矿坑入口`：8 个 `Ore_Stone` + 中心 `Log`，产出 `MineEntrance_Summoner`。
- 配方 JSON 是唯一编辑源；直接维护 manifest 声明的 8 个业务分包。建筑生成器需变更产物 ID 时通过 `Assets/Editor/FlatWorld/ContentTools/Items/RecipeJsonEditorService.cs` 定向修改对应 JSON。
- 运行时模型与校验：`RecipeDto.cs`、`RuntimeRecipe.cs`、`RecipeRuntimeFactory.cs`、`RecipeCatalogLoader.cs`。
- 制作公共核心：`CraftingRecipeMatcher.cs` 负责有序/无序、ExactItem/Tag、镜像和紧凑网格匹配并返回精确槽位消费计划；`CraftingTransaction.cs` 在深拷贝快照上统一扣料和放置全部产物；`CraftingService.cs` 统一预览、提交、动作与成功事件；`CraftingResult.cs` 提供失败原因和入口能力。
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
- `Mod_PlantGrow` 已标记废弃，仅保留旧 MOD 二进制/脚本兼容；本体 Prefab 禁止挂载。`Mod_Production` 不再挂在 `AppleTree`，成熟产物只能通过 `Mod_Grow` 的一次性交互收获生成。
- `Item.ModuleLoad()` 会在自动修复模块前迁移旧农业数据：Apple 删除旧 `Mod_Seed` 数据，AppleTree 删除旧“生产模块”数据；禁止移除此迁移，否则旧区块重载会重新挂回第二播种入口或无限生产。
- 耕地补给：`Assets/5_Scripts/5-3_GamePlay/Items/Food/Mod_FarmlandSupply.cs`；当前挂在 `Fertilizer.prefab`，单次补充水分与肥力，资源已满或目标不是耕地时不消耗。
- 缺水、缺肥、耕地丢失、恢复成长、成熟和已收获只在状态变化或交互时反馈，禁止低频 Tick 持续刷日志。
- 自定义难度统一入口：`Mod_Food.ConsumeNutrition()` 处理饥饿消耗，`Mod_Stamina.AddStamina()` 按正负值处理耐力恢复/消耗，`Mod_Grow` 处理作物生长，`BerryBush` 处理野生浆果成长与产量，`Mod_Fuel.ConsumeFuel()` 处理燃料消耗。
- 农具/杂草表现：`Assets/5_Scripts/5-3_GamePlay/Items/Food/`。
- 耕地数据：`Assets/5_Scripts/5-1_Data/TileData/TileData_Farmland.cs`。
- 烹饪与熔炼配方同样维护在 `Assets/StreamingAssets/GameConfig/Recipes/cooking/`、`smelting/` JSON 分包中。

## 资源目录

- `Assets/2_Prefabs/Inventory/`
- `Assets/2_Prefabs/Equipment/`
- `Assets/2_Prefabs/Food/`
- `Assets/2_Prefabs/Plant/`
- `Assets/2_Prefabs/Seed/`
- `Assets/2_Prefabs/Tools/`
- 通用手持物/武器 Animator：`Assets/8_Animations/Item/Weapon/Weapon_Uni.controller`；动画状态名固定为 `Idle_0`、`Attack_1`、`Attack_2`，与物品 JSON 和 Prefab 的 `Mod_Weapon_AnimationAction` 配置一致。
- 旧 `Assets/GameConfig/` Excel/Legacy 配置与自动同步工具链已移除；禁止恢复 Excel→Prefab 或 Excel→JSON 双重真源。

## 易误判点

- `Assets/5_Scripts/5-3_GamePlay/Items/Equipment/Module_Equipment.cs` 已废弃，优先使用 `Mod_Equipment.cs`。
- 遗迹容器内容不使用空壳 `Mod_Box`，由 `StructureContainerContents` 配置并在结构物件 `Load()` 后写入 `Mod_Inventory`；固定槽位配置会完整覆盖目标库存，空配置可表示空箱子。
- Inventory 持有数据和 UI 生命周期，但实际运行更新仍受 Item/Module Tick 调度影响。
- `Inventory.EnsurePanelCreated()` 只允许真正的模态库存订阅 `Opened/Closed` 输入锁并在创建后归一为关闭态；常驻快捷栏与内部 `Inventory_Hand` 必须保持无模态输入锁，避免玩家出生后移动、奔跑和交互被常驻 HUD 阻塞。
- `ToggleActionName = B` 的库存仍由键盘 B 自身直接开关；手柄 B 走全局取消关闭库存，不能让两条路径重复消费，库存派生的右键菜单及其物品详情必须在库存关闭时一并销毁。
- 配方不再依赖 `CraftingRecipe` Addressables 标签；修改 JSON 后检查清单、对应分包、物品 ID 与 `GameRes` 配方字典。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-11：修正库存面板首次创建的输入锁契约：模态背包先关闭再由公开入口打开并触发锁事件，快捷栏/手部库存排除在锁与手柄模态焦点外；Golden Path 联合验证背包制作与 `ui.inventory-panel` 的锁定/释放。
- 2026-08-09：拆分库存槽位的键鼠点击与手柄确认入口；键鼠在手上已有物品时保留手部交换，手柄使用当前快捷栏槽位，避免两种交换状态互相占用。
- 2026-08-09：BerryBush 的新版生态掉落物挂到 `NaturalItems`，采摘不再通过旧 `ChunkMgr` 触发区块加载；临时浆果由自然物表现层在区块解绑时回收。
- 2026-08-09：手柄 B 默认改为返回，不再打开背包；键盘 B 保留背包开关，库存面板统一接入 `BasePanel` 的手柄取消关闭。
- 2026-08-09：快捷栏 HUD 槽位关闭 EventSystem 手柄导航，并跳过通用背包面板焦点准备；左摇杆移动不再改动当前快捷栏，十字键切换保持不变。
- 2026-08-09：键盘 B 开关库存不再让全局取消栈抢先消费；手柄 B 统一返回并通过 `BasePanel` 关闭打开的库存，同时回收该库存打开的右键菜单和物品详情。
- 2026-08-09：修正背包键鼠冲突：保留槽位橙色焦点框，但运行时 UI 导航完全删除 W/A/S/D，键盘移动不再切换选中槽位，手柄导航保持原行为。
- 2026-08-09：玩家背包左键改为写入快捷栏当前选中槽并立即同步手持实例，避免物品只进入不可见的 `Inventory_Hand` 缓冲槽；箱子、工作台等独立库存仍保留手部交换。
- 2026-08-09：库存槽位、动态模块按钮和物品上下文菜单接入手柄焦点、主要/次要操作、滚动跟随与嵌套返回，避免手柄在纯统计条和滚动条上浪费导航步数。
- 2026-08-08：背包槽位与制作产物预览统一通过 `GameRes.TryGetItemPresentation()` 获取显示贴图；JSON 物品不再从 `AllPrefabs` 的共享模板外壳读取错误图标。
## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/InventoryCrafting/InventoryCraftingSmokeTests.cs`；当前覆盖库存模块、装备、配方、制作公共核心、初始库存资源入口、快捷栏初始选框对齐与切换父节点稳定性，以及 `UI_Bag` 整理按钮和 `UI_Slot` 制作预览图层命名契约。
- 农业闭环测试：`Assets/GameTest/InventoryCrafting/AgricultureLoopTests.cs`，分类 `InventoryCrafting.Agriculture`；覆盖唯一播种入口、AppleTree 单一 `Mod_Grow`、无无限生产/旧成长模块、肥料补给、倍率单次结算、水肥边界和成长状态 MemoryPack 往返。
- 公共核心回归：`Assets/GameTest/InventoryCrafting/CraftingCoreTests.cs`，分类 `InventoryCrafting.Core`；覆盖失败不扣料、多输出空间不足不部分产出、输入槽回落、镜像 + Tag + 紧凑网格消费映射和无序配方多余输入拒绝。
- 真实单机链由 Golden Path 操作 `inventory.crafting` 覆盖：从正式目录执行 `core:打制石器`，把 `ChippedTool` 写入玩家背包并在退出前完整恢复背包；可与 `ui.inventory-panel` 联合选择。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；背包制作测试约定目录：`Assets/GameTest/InventoryCrafting/`；场景目录：`Assets/GameTest/Scenes/InventoryCrafting/`；冒烟分类：`InventoryCrafting.Smoke`。
- 新增背包、槽位、快捷栏、容器、装备、配方、食物或植物行为时必须增加系统测试；修复 Bug 时先增加回归测试。物品进入背包到使用或制作主流程变化时同步更新冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试物品必须使用隔离数据并验证数量守恒、引用清理和失败路径。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category InventoryCrafting.Smoke --category InventoryCrafting.Core --category InventoryCrafting.Agriculture`；无需视觉模型或测试工具卡片。涉及 Item/Module、存档、玩家输入或 UI 时追加对应分类；只有界面最终观感变化才做定向截图。
- 教程使用的拾取/制作成功锚点及稳定 ID 由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

移动 Inventory/Equipment/Food Prefab，修改配方 JSON/DTO/Handler、槽位模型、配方注册、装备入口、农业 TileData 或控件命名后，必须更新本 Skill。
