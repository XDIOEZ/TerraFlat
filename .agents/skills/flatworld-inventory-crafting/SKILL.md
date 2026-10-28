---
name: flatworld-inventory-crafting
description: "FlatWorld 库存、槽位交互、制作事务、装备、食物、液体容器和农业的数据与运行规则。"
---

# 库存、制作与农业

代码根为 `Assets/5_Scripts/5-3_GamePlay/`：

| 入口 | 职责 |
| --- | --- |
| `Items/Inventory/` | Inventory、Mod_Inventory、Mod_HotBar、ItemSlot_UI、InventoryContextResolver、InventoryModuleDataScheduler |
| `Items/Crafting/` | CraftingService、CraftingRecipeMatcher、CraftingTransaction、CraftingStationController |
| `Items/Equipment/`、`Items/Food/` | Mod_Equipment、装配槽、Mod_WaterVessel 与液体端口 |
| `Entities/Item/Modules/Player/Food/`、`Entities/Item/Modules/Growth/` | Mod_Food、Mod_Grow.AuthoritativeCrop |
| `World/Planting/` | Mod_Plantable、FarmlandSystem |

权威库存为 `Assets/5_Scripts/5-1_Data/CoreData/Inventory_Data*.cs`；Prefab 在 `Assets/2_Prefabs/{Inventory,Equipment,Food,Plant,Seed,Tools}/`。固定/多料配方读 `Assets/StreamingAssets/GameConfig/Recipes/recipe-manifest.json`，单料加工读物品 processing，内容工坊在 `Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`。落地设施归 World/Machines，手持载体仍为 Item。

## 核心边界

- 本体初始库存由玩家 JSON 模板配置；旧测试库存资产已清理，`InventoryInit` Addressables 组允许为空，仍保留内容包模板入口。
- 所有扣量、转移、制作经正式事务；预检与提交共用容量/接收策略，多产物全部可交付才扣料。UI 槽只绑定真实库存，不拥有数据。
- `Inventory` 按 Panel/Interaction/ContentInjection 分文件，固定与虚拟槽共用输入绑定；`InventoryDragTransaction` 持有来源事务，`InventorySlotPointerQuery` 每次指针回调只扫描一次并复用命中缓冲。回归 `inventory/slot_binding_reuse、pointer_query_reuse`，拖拽现场仍走 Inventory gameplay。
- 专用容器通过 `Inventory_Data.SetItemAcceptancePolicy` 装配非序列化接收规则，数量转移、拖拽交换和整仓入库共用；临时面板用 `Inventory.RuntimeAccessGuard` 核对提交时的会话资格，过期拖拽拒绝，内部资源消费不受界面开关影响。
- InventoryContextResolver 按 ItemData 引用/Guid 找所属库存；快捷栏按真实槽索引刷新。联机消费由服务器取真实槽，客户端 pending/ack 期间不得用旧库存覆写结果。
- 背包分类由 Data/CoreData/InventoryItemCategoryCatalog 固定为建筑装饰、自然植物、功能设施、工具用品、武器装备、食物饮品、材料零件、交通运输；筛选和分类排序共用唯一主分类。内容复用 `InventoryCategory.*` 八种标签，不按材料、工业分支新增页签；无显式标签按语义推断，未识别归材料。改动时更新 `Tools/Tests/InventoryCategoryFilter/expected-categories.json` 并验证全 Manifest。
- 重量kg、库存体积L、Stackable 独立；配置m³需换算。携带量对实际主背包/快捷栏/手工输入输出按引用去重，关闭面板不减载荷。
- 共用 `UI_Bag` 的容器标题绑定 `FWUI_标题`，通过 `LocalizedTextRuntime.SetProvider` 读取所属物品的 `RuntimeItemDefinition.DisplayName`；玩家行囊保留原名，不改作存档和端口键的 `Data.Name`。
- 制作经 Matcher→Controller→CraftingService→CraftingTransaction；Save 不解绑，Unload 才清监听。显示、输入与数据生命周期分别管理。
- `FireMakingInventoryPanelModule` 共管钻木器与打火石的库存面板和交互；钻木加热、打火概率、保存格式及建筑资格分别由子模块负责。
- 液体保留真实组分/尾量/预约，世界液层用整数级；播种、收获、装备供氧等通过各自公开接口提交，不把来源特例塞进通用模块。
- 成长、燃料和制作只读取 `GameRuleConfiguration` 已发布的业务配置；`CraftingCapabilities.ApplyOutputMultiplier` 控制是否接受通用增产，业务不识别难度或其他修正来源。

## 按需资料

只读取当前主题，不批量加载：

- [库存权威、容量、拾取与手持](references/inventory-transactions.md)
- [拖拽、甩动与触屏交互](references/slot-interaction.md)
- [配方、手工加工与冷调度](references/crafting-processing.md)
- [装备、食物与液体容器](references/equipment-food-vessels.md)
- [播种、作物与土壤](references/agriculture.md)
