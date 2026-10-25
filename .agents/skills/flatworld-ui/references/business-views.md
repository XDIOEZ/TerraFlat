# 业务视图

## 库存、机器与装备

- 矿车 `UI_Minecart/MinecartPanel` 左侧两个通用引擎槽、右侧18格货仓，绑定 `Mod_MinecartInventory` 的真实库存；按领域 `CanAccess` 与窗口开关持有 `RuntimeAccessGuard`，关闭立即撤销旧拖拽资格。F 上车开普通窗口，WASD不受阻、再F下车；页内连接/断开/乘坐只调用矿车公开接口。`UI_SteamEngine/SteamEnginePanel` 订阅手持蒸汽机 `OpenRequested`，只编辑自身开关和档位，切离手持即关闭。

- 通用背包/储物 `UI_Bag` 默认十列（1004×560，槽80、间距4）；快捷栏十格（982×104，槽82、间距10），数字键按 `1～9、0` 对应。同步 Prefab、`GameUIPrefabRebuilder`、`Mod_HotBar`、输入资产/生成包装与 `InputBindingService`；普通容器总格数仍由各自容量配置决定。

- 太空电梯 `UI_SpaceElevator` 只绑定当前端独立仓库，复用背包 `UI_Slot`，默认6×4并按实际容量扩展滚动内容；主背包仍开原窗口。会话只追加/移除自己的库存刷新监听，不调用会清空其它监听的 InitData/SyncData；关闭或离开终端即拒绝旧拖拽。打包发送到站自动入另一端仓库，满仓余货提示按 CargoDestination 显示，不能阻止客运或召回。

- 物品信息分两层：`IItemInfoProvider` 经 `ItemInfoQuery.Collect` 返回结构化秒数、模块实例槽、时间源与整堆/下一件语义，不依赖 UI，也不推进状态；基础参数与攻击力同样返回原始数值，伤害缺省参数只读已加载模板并按定义缓存；`IItemTooltipInfoProvider`/`ItemTooltipInfoFormatter` 只生成显示行。食物腐败/逐件融化读现有观察者状态，自然消失仅在状态副本上投影所属世界时钟；无时钟不猜当前玩家世界。新增能力注册数据提供者，文案在 UI 层注册；不要让其他系统解析显示字符串。`InventoryItemTooltip` 复用缓冲，每秒实时时间刷新但不以 UI 时间推进业务。回归为 `ui_settings/item_info_food、item_info_lifetime、item_info_extension、item_info_numeric`。

- 背包/容器 `UI_Bag` 搜索右侧复用 `UI_MultiSelectDropdown`：不同分类按 AND 交集，与名称搜索叠加；`InventoryCategoryFilter` 只读稳定标签。普通库存隐藏非命中槽，创造库存用 `InventoryVirtualizedSlotGrid` 的显示索引映射，不能改真实数据或交互索引；拖拽期间延迟刷新。弹层通过 BasePanel 取消契约优先关闭，勾选保持展开，清除分类不清文字；新视觉同步 `RuntimeUIPrefabBuilder.InventoryFilter`。离线验证 `Tools/Tests/InventoryCategoryFilter/verify.py`，正式回归在 UISettings 的 `inventory_category_filter/inventory_filter_projection`。

- `MachinePanelFactoryRegistry` 按LogicId租约注册；通用会话管生命周期/库存/权威请求，具体会话管专属控件。槽位按真实容量显隐、不按炉型ID分支，电磁炉复用面板隐藏燃料/点火，启用正式温控子树并读取权威目标／炉温／需求与实供功率；草稿经机器操作提交，Token 回执与目标状态都到齐才成功，传输未确认和状态同步超时分别反馈，关闭清理等待；不主动抢手机输入焦点。IInstanceUI.IsPanelOpen读真实面板，模块列表按创建/展开/操作刷新。
- 堆肥箱/晾肉架库存滚动条在滚动区内右侧占32，Viewport预留该宽度，Content横向跟随视口、纵向PreferredSize；熔炉三格输入需270高度。正式Prefab与 `GameUIPrefabRebuilder` 同步，资产边界回归为 `ui_settings/building_panel_layout`。
- 电池 `UI_Mechanical` 的 BatteryCharge/BatteryPercentage 由 MechanicalPanelSession读比例，竖Slider禁交互、下向上填、仅电池显示；布局同步 MechanicalContentBuilder.EnsureBatteryGauge。气罐 `UI_IndustrialFluid/UI_FluidTank` 的 GasCapacity/GasPercentage 按实压/安全上限填，超压填充封顶但百分比真实且红；组合罐读节点整组库存/容积、便携罐由MechanicalPanelSession注入，构建器 EnsureGasCapacityGauge同步。
- 铁/钢便携气罐共用 `UI_FluidTank`：标题在上、信息与容量在中、操作统一归 `FWUI_Footer`，放置/拆回共用左侧位置、充气在右侧；正式资产与 `RuntimeUIPrefabBuilder.Industrial.ConfigurePortableTankActions` 同步，放置与拆回复用 `BuildingPanelActions`。手动充气由 `FluidTankConfiguration.AllowManualAirFill` 声明，UI、权威事务和联机回执共用能力判断；目标取实际环境压力与1标准大气压的较小值。`MechanicalPanelSession` 可见时沿现有状态刷新更新按钮资格，不重绑按钮文字或重建布局。
- 热栏选中框归当前槽，用Common/Controls/UI_SelectBox与Inventory_HotBar.SelectBoxPrefab；附属提示ignoreLayout/无射线，Inventory.InitUI按真实ItemSlot_UI计槽，手持名读装备热栏。ItemSlot_UI.SetProcessingProgress仅写正式灰白层，燃料进度读真实吸收，Prefab与GameUIPrefabRebuilder同步。
- `InventoryItemTooltip` 鼠标悬停0.3非缩放秒，等待/显示均验来源面板/槽/物品，离开或重绑重计时；触屏空手长按0.5秒后固定并松手保留，窗外按下仅收起不截点击。固定期间持续验身份/手空/父面板可见，关闭/重绑/禁用/失焦清理，CanvasGroup隐藏也清。正式Prefab自带无射线独立Canvas，统一UIManager.ItemTooltipSortingOrder覆盖场景/常驻窗口。
- 手工/制作台用Gameplay/Crafting/UI_HandCraftTable/UI_MakerTable，1344×756、安全区后比例0.6/0.8，真实输入/输出/配方容量复用；Prefab、GameUIPrefabRebuilder、TodoCraftingLayoutPreview同步。工业流体/宇航服/气罐用MechanicalPanelView/PreservePrefabLayout，宇航服用SpacesuitPanelSession真实供氧槽。
- 装备只有 `FWUI_ConfigureEquipment` 通用入口；EquipmentConfigurationButton收集配置能力，一件直开、多件公共下拉、无则禁用。Mod_Equipment通过IEquipmentConfiguration分发并验当前装备身份，具体装备持页面，通用UI不识别具体装备。
- 装备页九槽共用 `EquipmentSlotCatalog`；眼部 `Eyes` 居中放在头像上方，头部与眼部分槽。正式 `UI_Equipment` 与 `BuildEquipmentWindow` 同步630×516内容、左右四排125间距，标签和槽均在内容范围内。

## 飞船与角色HUD

- ShipPanelSession命令/按钮共用入口，有限反馈历史不抢用户滚动；FuelInteraction只绑通用模块、标题读定义，任务/教程入口用书籍/UI_ReadableBook。命令后按PreferredInputDevice.KeyboardMouse恢复焦点，支持手机外接键盘。
- `ShipGroundAssembly.RequestLaunch` 统一权威点火请求；机器删除至船快照到达期间保控制台，服务端成功与同次船体/乘员状态齐后才切船控/反馈。GM生成/补给走ShipFixedStructureBuilder.RequestBuild/RequestRefill，发包成功不等于业务成功。
- PlayerShipFlightHUD读SpaceSession只读快照/正式落地凭证，UI_ShipFlightHUD与构建器同步；Tracker管驾驶/退出/重进/完成短提示，正高度不舍入零，安全区内避坐标/Buff/真实摇杆。
- PlayerWorldCoordinateHUD地表读WorldLocalPresentation逻辑坐标，太空读乘员双精度宇宙位置（支撑船姿态+本地位置或FreeMotion），公里/米精度不随ViewOrigin变化；坐标宽560，温压/季节独立。温度采样不依赖移动；气压按显示小数、安全等级与头盔密封状态复用文本，语言变更刷新翻译模板，规则 `ui_settings/pressure_hud_display_cache`；FoodUIModule仅本地档案玩家持HUD。
- PlayerBuffStatusHUD重建先清旧行，卸载撤绑定，不在Buff清理回调重建；安全区最多6行、溢出只视口滚动；图标同步读预载Sprite、缺图名称首字，底板/层数徽标独立。
- `UI_Death` 动态标题由Mod_PlayerDeathState.DeathMessage消费DeathCauseRecord，经DeathMessageCatalog与GameConfig/Combat/death-messages.json组合；不编缺失来源，禁静态Binder、换语言保Variant，多行布局与BuildDeathScreen同步。MOD行拖动归共同父节点、来源为文件夹身份，跨页复用保来源，预览无射线、关闭取消。

## 水容器

- WaterVesselPanel.Appearances按载体物品ID持剖面/Mask/水位区间/出口/MouthWidth和可选StorageBounds，落地外观按SummonerPrefabId回查，未匹配恢复默认；PNG-Y向下，水位/出口归一化Y向上。液面/液流读LiquidDefinition.viscosity与VisualState/LiquidStyle，连续波面固定步长，Graphic不结算液量。
- 倾倒按基准流速×嘴宽比×倾角，不按容量；90°取消保留上限但仍按流速递减。液层世界水平、网格覆盖旋转Mask，从下侧嘴沿出水；Mod_WaterVessel成功移除后才触发液流。
- WaterVesselPourGraphic在Mask外、切面子树后，前景窄液桥接罐内/嘴沿、主柱始于嘴沿。透明根无底板射线，罐体手势/拖放、按钮独立；VesselContentsView在Mask下克隆真实槽、空槽在占用槽下，物理只改姿态、关闭只销视觉；固体槽拒绝后仍可命中IInventoryDragDropTarget转液入口。
- 倒置后真实固体抵嘴沿，在物理枚举结束后提交正式出料；临时合并投料图标不扣量，出料失败留原槽。窄内腔按实际宽度排列静止物品，避免初始三列重叠。

- 砧使用独立 `UI_Anvil`（金属锤/材料各一格、产物一格），标题从建筑对应载体的正式名称读取，拆除位于开始制作左侧并复用 BuildingPanelActions 事务。制作槽增补/重排必须归 `CraftingContent`，不能挂全屏根；布局源、正式 Prefab 和 TodoCraftingLayoutPreview 同步。
