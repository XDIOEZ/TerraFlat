# 业务视图

## 库存、机器与装备

- `MachinePanelFactoryRegistry` 按LogicId租约注册；通用会话管生命周期/库存/权威请求，具体会话管专属控件。槽位按真实容量显隐、不按炉型ID分支，电磁炉复用面板隐藏燃料/点火并读实供功率、真实升温系数与容量；IInstanceUI.IsPanelOpen读真实面板，模块列表按创建/展开/操作刷新。
- 电池 `UI_Mechanical` 的 BatteryCharge/BatteryPercentage 由 MechanicalPanelSession读比例，竖Slider禁交互、下向上填、仅电池显示；布局同步 MechanicalContentBuilder.EnsureBatteryGauge。气罐 `UI_IndustrialFluid/UI_FluidTank` 的 GasCapacity/GasPercentage 按实压/安全上限填，超压填充封顶但百分比真实且红；组合罐读节点整组库存/容积、便携罐由MechanicalPanelSession注入，构建器 EnsureGasCapacityGauge同步。
- 热栏选中框归当前槽，用Common/Controls/UI_SelectBox与Inventory_HotBar.SelectBoxPrefab；附属提示ignoreLayout/无射线，Inventory.InitUI按真实ItemSlot_UI计槽，手持名读装备热栏。ItemSlot_UI.SetProcessingProgress仅写正式灰白层，燃料进度读真实吸收，Prefab与GameUIPrefabRebuilder同步。
- `InventoryItemTooltip` 鼠标悬停0.3非缩放秒，等待/显示均验来源面板/槽/物品，离开或重绑重计时；触屏空手长按0.5秒后固定并松手保留，窗外按下仅收起不截点击。固定期间持续验身份/手空/父面板可见，关闭/重绑/禁用/失焦清理，CanvasGroup隐藏也清。正式Prefab自带无射线独立Canvas，统一UIManager.ItemTooltipSortingOrder覆盖场景/常驻窗口。
- 手工/制作台用Gameplay/Crafting/UI_HandCraftTable/UI_MakerTable，1344×756、安全区后比例0.6/0.8，真实输入/输出/配方容量复用；Prefab、GameUIPrefabRebuilder、TodoCraftingLayoutPreview同步。工业流体/宇航服/气罐用MechanicalPanelView/PreservePrefabLayout，宇航服用SpacesuitPanelSession真实供氧槽。
- 装备只有 `FWUI_ConfigureEquipment` 通用入口；EquipmentConfigurationButton收集配置能力，一件直开、多件公共下拉、无则禁用。Mod_Equipment通过IEquipmentConfiguration分发并验当前装备身份，具体装备持页面，通用UI不识别具体装备。

## 飞船与角色HUD

- ShipPanelSession命令/按钮共用入口，有限反馈历史不抢用户滚动；FuelInteraction只绑通用模块、标题读定义，任务/教程入口用书籍/UI_ReadableBook。命令后按PreferredInputDevice.KeyboardMouse恢复焦点，支持手机外接键盘。
- `ShipGroundAssembly.RequestLaunch` 统一权威点火请求；机器删除至船快照到达期间保控制台，服务端成功与同次船体/乘员状态齐后才切船控/反馈。GM生成/补给走ShipFixedStructureBuilder.RequestBuild/RequestRefill，发包成功不等于业务成功。
- PlayerShipFlightHUD读SpaceSession只读快照/正式落地凭证，UI_ShipFlightHUD与构建器同步；Tracker管驾驶/退出/重进/完成短提示，正高度不舍入零，安全区内避坐标/Buff/真实摇杆。
- PlayerWorldCoordinateHUD地表读WorldLocalPresentation逻辑坐标，太空读乘员双精度宇宙位置（支撑船姿态+本地位置或FreeMotion），公里/米精度不随ViewOrigin变化；坐标宽560，温压/季节独立。温度采样不依赖移动、显示按最终精度去重；FoodUIModule仅本地档案玩家持HUD。
- PlayerBuffStatusHUD重建先清旧行，卸载撤绑定，不在Buff清理回调重建；安全区最多6行、溢出只视口滚动；图标同步读预载Sprite、缺图名称首字，底板/层数徽标独立。
- `UI_Death` 动态标题由Mod_PlayerDeathState.DeathMessage消费DeathCauseRecord，经DeathMessageCatalog与GameConfig/Combat/death-messages.json组合；不编缺失来源，禁静态Binder、换语言保Variant，多行布局与BuildDeathScreen同步。MOD行拖动归共同父节点、来源为文件夹身份，跨页复用保来源，预览无射线、关闭取消。

## 水容器

- WaterVesselPanel.Appearances按物品ID持剖面/Mask/水位区间/出口/MouthWidth，未匹配恢复默认；PNG-Y向下，水位/出口归一化Y向上。液面/液流读LiquidDefinition.viscosity与VisualState/LiquidStyle，连续波面固定步长，Graphic不结算液量。
- 倾倒按基准流速×嘴宽比×倾角，不按容量；90°取消保留上限但仍按流速递减。液层世界水平、网格覆盖旋转Mask，从下侧嘴沿出水；Mod_WaterVessel成功移除后才触发液流。
- WaterVesselPourGraphic在Mask外、切面子树后，前景窄液桥接罐内/嘴沿、主柱始于嘴沿。透明根无底板射线，罐体手势/拖放、按钮独立；VesselContentsView在Mask下克隆真实槽、空槽在占用槽下，物理只改姿态、关闭只销视觉；固体槽拒绝后仍可命中IInventoryDragDropTarget转液入口。
