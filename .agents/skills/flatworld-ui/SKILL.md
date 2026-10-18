---
name: flatworld-ui
description: "FlatWorld uGUI 面板生命周期、Prefab/公共控件、主题、设置 Provider、输入焦点与游戏界面表现。"
---

# FlatWorld UI

## 入口

- `Assets/5_Scripts/5-5_UI/`：`Core/{UIManager,BasePanel,RuntimeUIPrefabKeys}.cs`，`Common/{Controls,Presentation,Animation}/`，`Input/`。
- `Assets/5_Scripts/5-3_GamePlay/`：主菜单 `Core/Lifecycle/GameManager.UI*.cs`，业务界面 `Presentation/UI/`，存档入口 `SaveDataManager_UI.cs`。
- 视觉资产：`Assets/2_Prefabs/2-1_UI/{Common,Gameplay,MainMenu,Settings}/`；根 `Assets/Resources/UI/UIRoot.prefab`；构建器 `Assets/Editor/FlatWorld/PrefabBuilders/UI/`。
- 共享配置：`Assets/Resources/GameConfig/UI/ui-theme.json`，`Assets/Resources/Config/UIAnimations.json`；设置契约 `Assets/5_Scripts/5-1_Data/Settings/SettingsContracts.cs`。

## 视觉与资源所有权

- 静态 UI 只保留字段、操作、实际状态和必要规则，不添加重复标题、英文装饰眉题、开发实现说明或空泛常驻提示。

- 主菜单为视觉基准：纯色灰阶 Image、近白文字、低对比边界，暖黄用于少量焦点/关键操作；面板从根到内容/按钮逐级提亮，普通边框约 2 参考像素、文字描边约 1 像素。物品图标/内容语义色独立于 Chrome 主题；信息 HUD 为角落锚点与透明容器。
- `FlatWorldUIThemeConfigCatalog → FlatWorldUITheme → BasePanel/构建器` 共用 ui-theme.json。公共控件为真正嵌套 Prefab，`ReusableUIControl` 持有内部结构/字体/Sprite/几何；使用处持有文案、事件、数值、选项与外部布局，页签由 `ReusableUITabVisual.SetSelected` 改状态。
- 正式 Prefab 持有完整视觉与序列化引用，领域控制器持有实例/业务，UIManager 管生命周期。控件节点名和 RuntimeUIPrefabKeys 为绑定身份，资产处于 Addressables `Prefab` 标签范围；UI 子目录继承根 UI.asmdef，脚本/资源移动保留 meta/GUID。
- 构建器先停用带 Awake 的根、填好引用再激活，保存时应用共享控件/主题。主题与公共控件迁移均为显式菜单操作；公共资产补建不覆盖人工编辑，迁移只处理自身所有的覆盖。
- 普通面板首个视觉底板为 `UI_PanelBackground`，根 Image 保留透明射线职责；HUD、加载幕、主菜单与无底板专属面板各自持有外观。确认/改名弹窗仅显示悬浮窗口，阻断层为透明 Image。
- Prefab 根 CanvasGroup.alpha=1，初始关闭由注册前 `InitClosed()` 建立；独立 Canvas 根 localScale=1。启动资源加载面板由 WorldManager.prefab 直接引用，独立于尚未初始化的资源字典。

## 生命周期、输入与排序

- 同一 PanelRoot 以兄弟顺序置顶/置底，sortingOrder 仅管理独立 Canvas。模态持有玩法锁和顶层手柄焦点，关闭/失败释放；常驻 HUD 输入透明，可收展 HUD 仅开关按钮接收射线。
- `UIManager.CreatePanelFromGameObject` 默认挂当前场景的 `SceneUI/SafeAreaRoot`，主菜单及其弹窗随场景卸载销毁；`CreatePersistentPanelFromGameObject` 显式挂常驻 `PanelRoot/SafeAreaRoot`，供玩家 HUD、库存、设置与死亡界面使用。共享库存按所属对象分流；附属弹窗/虚拟键盘用 `GetPanelRootFor` 继承拥有者归属，全屏覆盖层只提排序不改归属。跨星区保留玩家 UI，玩家重建仍按模块生命周期清理。
- 主背包 `UI_Bag` 可并行，参与置顶/导航/取消链但不持玩法锁；库存/加工/容器背景输入透明，只有槽位、按钮与拖放/手势区命中，`UIWindowDragSurface` 命中标题/窄边框。快捷栏随 PanelRoot 排序；手机模态拖放由独立 Canvas 临时提升。
- 库存/制作窗口通过 `BasePanel.SetHeldItemWorldDropAllowed` 声明窗口外手持丢弃能力并登记输入锁 Owner；`UIManager` 同轮面板缓存汇总所有阻断窗口、兼容锁与实际窗口矩形（优先 `UIWindowDragSurface.WindowRect`）。任一未授权窗口/未知锁继续阻断，实际窗口内部透明空白也不能作为世界落点；仅迁移手机丢弃面，不解锁其它玩法控件。
- 手机菜单/返回位于独立常驻层，返回顺序为顶层可取消面板→抽屉→设置。主菜单 CancelShortcutOverride 打开退出确认；嵌套危险确认先消费取消。存档删除统一由 `SaveDataManager_UI` 确认层提交并锁定目标身份。
- 文本通过 FlatWorldUI 表与 GetUiText/GetUiFormat，节点名不翻译；手柄焦点限定顶层面板，TMP 输入确认后进入虚拟键盘。鼠标按下焦点与列表业务选中状态分离，完整点击才提交选择。
- 面板可能先于模块销毁，Unity `!= null` 判断存活；即使面板已销毁仍释放锁/快捷转移引用。场景卸载清理失效注册并刷新交互缓存；输入查询覆盖当前场景与常驻根。资源重载清空两类根下的运行时 UI 与注册缓存，保留 UIRoot/SafeAreaRoot，再由当前 GameRes/Player 重建。
- 库存、快捷栏与手部面板随所属模块卸载调用 `Inventory.ReleaseRuntimePanel`，同时注销 `UIManager` 注册；仅解除输入与数据绑定不会自动销毁跨场景面板。
- 手工制作与玩家设置面板也由各自 `OnUnload` 注销并销毁；`UIManager.DestroyPanel` 在延迟 Destroy 前立即停用对象，避免新玩家同帧被旧面板拦截射线。
- `UI_WorldLoading` 为不透明黑幕与独立内容 CanvasGroup：进世界前覆盖并绘制一帧，区块表现就绪后内容→黑幕淡出→释放输入；同场景重生为单段淡出。
- 角色世界状态条使用高于 Water 的 Sorting Layer；直接挂普通 Transform 的 RectTransform 偏移以 anchoredPosition 为准。
- 玩家体力条由 `Mod_Stamina` 按真实快捷栏矩形的上沿动态避让，不改通用 `UI_Slider` Prefab；定位后调用 `HotbarItemNameHUD.PositionAboveHud`，让名称底板按体力条实际上沿留出 12 个快捷栏逻辑像素；手机安全区、快捷栏缩放和底部间距改变后保持不重叠。

## 布局、动画与公共控件

- UIRoot 使用 UIScaleController 与 1920×1080 基准；UIUserSettings 为缩放默认/范围权威。SafeAreaRoot 只约束交互，FullScreenRectController 反向扩展视觉背景，CanvasScaler 不再乘安全区比例。
- 安全区先确认属于当前屏幕，再归一化并在 Canvas 绘制前补更新；`SafeAreaScaleGroup` 对固定卡片只缩不放，presentationScale 在安全区适配后生效。快捷栏负底部间距保留为偏好，最终矩形避让强制手势区。
- 主菜单模态共用 SharedModalCardSize；MainMenuResponsiveLayout 以原位置和安全区共同限幅品牌/菜单/设置入口。移动端交互高度≥60、正文≥17 逻辑像素；内容增长由 ScrollRect 承载。
- 列表使用 `ItemStepScrollRect`，滚轮按 scrollDeltaPerTick 还原刻度，再按条目/网格行移动；单段长文本使用 fixedWheelStep。显式迁移只换 MonoScript GUID，保留组件 fileID/引用。
- 存档列表用 `SaveSelectionFileCatalog` 每档案采样一次退出时间后内存排序，面板显式打开负责刷新，Start 只作未刷新的兜底；首行同步准备，其余条目由 `SaveDataManager_UI.Update` 每帧最多四行、约 2ms 补足，关闭暂停补建，完整身份列表不裁剪，批选与改名恢复不依赖已生成行数。
- 新游戏窗口由正式根 `UI_NewGame` 和 `UI_NewGameWorldContent/UI_NewGameDifficultyContent` 三个 Prefab 组成，与 `NewGamePrefabBuilder` 同步维护；隐藏页按需实例化一次，创建世界请求直接读取尚未打开页面的默认配置，不为取值生成页面。
- 标准 TMP/Legacy 下拉模板由 `DropdownListLayout` 在每次展开前按实际行高准备，最多显示 5 行，少于等于 5 项全部显示；`BasePanel` 收集组件时接入，公共构建器同步。条目与 Content 排布归原生 Dropdown，模板关闭竞争的 LayoutGroup/ContentSizeFitter，Viewport 不保留垂直内缩，避免末行被裁。
- `BasePanel → BaseUIAnimation → UIAnimationManager → JSON` 单向依赖，每面板最多一个动画组件，Opened/Closed 是同步业务事件。正式面板以 `ScaleUIAnimation/panel.scale` 绕 MotionRoot 可见中心缩放，动画不争写布局/安全区目标。
- 动画配置保存 Duration/相对 Offset/Scale/Ease，CanvasScaler 管分辨率；全局播放倍率只作用本系统根 Tween，热重载保留当前行程快照，反向开关保留进度，结束/禁用恢复姿态。
- 槽间快速转移图标由 `InventoryQuickTransferAnimation` 消费已提交事务，独立顶层 Canvas、无射线、非缩放时间飞行 0.24 秒，鼠标和触屏甩动从反馈中的松手屏幕坐标接续、Shift 快速转移从源格出发；两端必须是真实可见且绑定未换代的槽位，父级缩放通过实际图标世界角点换算，关闭/卸载取消，最多并发 32 个；不延迟库存事务或禁用输入。
- 交互滑块与只读进度条为不同资产；滑块根为透明命中区，Background/Fill/Handle 持有外观，进度条禁交互/导航。动态 Content 引用来自所属容器，EventSystem 反馈持有唯一非缩放 Tween。

## 设置数据

- Data 契约仅含元数据/读写能力；功能管理器实现 `ISettingsProvider` 及 Toggle/Slider/Dropdown/Switch 能力，在自身生命周期注册。UI 按稳定 ProviderId/Key 绑定；选项身份为 SettingOption.Id，写入返回校验结果。
- 游戏操作页的甩动速度/距离滑条与方向限制开关由 `InputBindingPanelLauncher` 绑定 `InventoryFlickUserSettings` Provider（`inventory_flick`）；百分比滑条范围25～200、步长1、默认100，方向默认开启，显示/保存/放弃/恢复默认复用现有会话。固定操作项放在滚动 Content 的首项 `操作选项`，动态按键行从其后复用，不回收固定控件。
- `SettingsEditSessionController` 持有保存基线；非控件偏好由 `ISettingsEditSessionParticipant` 快照/还原，按键绑定单独编辑会话。Provider 不创建视觉，分页器收集子页 `ISettingsPageLifecycle`。
- 关闭编辑会话只恢复偏离基线的控件值；扩展状态通过 `ISettingsEditSessionChangeTracker` 跳过无变化恢复，未提供检测的 Participant 保留恢复。注册表返回是否尝试恢复，关闭控制器仅在可能写回偏好时保存；按键放弃仅在实际覆盖改变时重载并通知，避免无修改关闭重复切画质、写盘和重建输入。
- 模拟半径为设备级 PlayerPrefs 偏好，三半径整组校验严格递增，Item/AIECS 共用；镜头缩放独立。视觉特效默认/范围来自 default-rendering.json，玩家值由 Provider 保存；太阳投影关闭不关闭脚底阴影。
- 水体风格由 `WaterVisualSettings` Provider 暴露风格化/写实/Unity 风格水三项，稳定 ID 为 `stylized/realistic/unity-stylized-water`；正式 `UI_VisualEffectsSettings` 与构建器同步三个 200×64 按钮，保存/放弃复用设置会话。
- 帧率由 `FrameRateUserSettings` 统一持有，默认60帧；显示页共用预设/不限帧与1～1000整数输入，`FrameRateSettingsPanelController` 只绑定控件。预设与自定义值均加入设置会话；切画质后重新应用，移动启动不再覆盖上限。手机不限帧使用屏幕刷新率，避免平台-1默认降为30帧。
- 主菜单/游戏内共用 NewWorldUserSettings。新世界窗口每次打开回填默认，名称/世界设置切页保留输入；返回先关难度层、再名称页、最后窗口。难度共用 GameDifficultyCatalog 的 0～20 级，摘要为战斗/生存/世界/生产。

## 业务视图契约

- 死亡界面 `UI_Death` 的主标题由 `Mod_PlayerDeathState.DeathMessage` 消费权威 `DeathCauseRecord`，通过 `DeathMessageCatalog` 与 `GameConfig/Combat/death-messages.json` 组合死因；来源缺失不编造名称。动态标题关闭静态 Binder，语言切换保留 Variant；正式 Prefab 与 `GameUIPrefabRebuilder.BuildDeathScreen` 同步多行自动字号布局。
- `MachinePanelFactoryRegistry` 按 LogicId 注册租约；通用会话管生命周期、库存绑定、权威请求，具体会话管专属控件。`IInstanceUI.IsPanelOpen` 来自真实面板，模块列表按创建/展开/操作事件刷新。
- 电池在 `UI_Mechanical.prefab` 内持有 `BatteryCharge/BatteryPercentage` 正式引用，竖向 Slider 禁交互/导航、从下向上填充；`MechanicalPanelSession` 接收储能比例读取函数，仅电池显示并在状态刷新时同步，正文右侧留出控件空间。修改外观同步 `MechanicalContentBuilder.EnsureBatteryGauge`。
- 气罐在 `UI_IndustrialFluid/UI_FluidTank.prefab` 持有 `GasCapacity/GasPercentage`，按实际气压/安全上限自下而上填充；超压填充封顶、百分比保留真实值并标红。工业页仅储罐显示，组合罐在节点作用域读取整组库存与容积；便携罐通过 `MechanicalPanelSession` 注入读数。布局同步 `RuntimeUIPrefabBuilder.EnsureGasCapacityGauge`。
- 热栏外观由 `Common/Controls/UI_SelectBox.prefab` 与 Inventory_HotBar.SelectBoxPrefab 持有；选中框归属当前槽位。附属提示 ignoreLayout 且输入透明，Inventory.InitUI 按真实 ItemSlot_UI 计槽；手持名称读取装备热栏物品。
- `ItemSlot_UI.SetProcessingProgress` 只更新正式 Prefab 中的槽内灰白进度层，底部 Pivot 配合纵向缩放，纯色无 Sprite 时不使用 Filled；燃料面板读取炉体真实吸收进度，熔炉/篝火资源与 `GameUIPrefabRebuilder` 同步维护。
- `InventoryItemTooltip` 由鼠标悬浮与触屏空手长按共用；触屏显示后固定位置并在松手后保留，窗外按下只收起、不截断原点击。固定期间逐帧核验槽位/物品身份、手部为空与父面板可见，面板关闭、槽位重绑/禁用或失焦清理；父级 CanvasGroup 仅隐藏时也不能残留。
- 普通合成使用 `Gameplay/Crafting/UI_HandCraftTable/UI_MakerTable.prefab`，参考尺寸 1344×756；安全区适配后的表现比例为手工制作 0.6、制作台 0.8，Prefab、GameUIPrefabRebuilder 和 TodoCraftingLayoutPreview 同步维护；正式输入/输出/配方模板按真实容量复用。工业流体/宇航服/气罐使用 MechanicalPanelView 与 PreservePrefabLayout，宇航服入口共用 SpacesuitPanelSession 的真实供氧槽。
- 装备页只保留 `FWUI_ConfigureEquipment` 通用入口，由 `EquipmentConfigurationButton` 收集可配置装备；一件直接打开，多件用公共下拉选择，没有则禁用。`Mod_Equipment` 只通过 `IEquipmentConfiguration` 分发并复核当前装备身份，具体装备效果拥有配置页面；不在装备 UI 或工业面板重建中添加具体装备分支。
- 飞船 ShipPanelSession 的命令行/按钮共用执行入口，反馈历史有上限，查看历史不被消息置底。FuelInteraction 仅绑定通用模块，标题来自定义。任务/教程阅读入口为书籍/UI_ReadableBook。
- 飞船起降高度尺由 PlayerShipFlightHUD 读取 SpaceSession 的只读快照和正式落地凭证，独立 UI_ShipFlightHUD 与构建器同步；纯展示 Tracker 处理驾驶/退出/重进/完成短提示，正高度不舍入为零，SafeAreaRoot 中避开坐标、Buff 与实际手机摇杆。
- PlayerWorldCoordinateHUD 地表读取 WorldLocalPresentation 逻辑坐标，太空读取乘员双精度宇宙位置（支撑船姿态 + 本地位置 / FreeMotion），显示千米与米级精度，不随 ViewOrigin 改变；坐标行宽 560、下方温度/气压与季节行独立。环境温度采样独立于移动、显示按最终精度去重；FoodUIModule 仅为 IsLocalProfile 玩家持有角色 HUD。PlayerBuffStatusHUD 重建容器时清旧行缓存，玩家卸载期间撤绑定，不由 Buff 清理回调重建视图；Buff 列表使用 SafeAreaRoot，最多显示六行，溢出时仅视口可滚动；`状态图标` 同步读取目录预载 Sprite，缺图显示名称首字，与底板和层数徽标独立。
- MOD 条目拖拽由共同父节点接收，来源为文件夹身份；跨页行复用保留来源，预览/落点线输入透明，关闭取消。

## 水容器表现

- `WaterVesselPanel.Appearances` 按物品 ID 提供剖面、内腔 Mask、水位区间、左右出口和 MouthWidth，未匹配时恢复默认外观；PNG Y 向下，水位/出口归一化 Y 向上。
- 液面/液流共用 LiquidDefinition.viscosity 与 VisualState/LiquidStyle。`WaterVesselLiquidGraphic` 为固定步长连续波面，LiquidStyle 管浑浊/沉淀/污膜/颗粒；Graphic 帧率不结算液量。
- 倾倒流量由基准流速×嘴宽比例×倾角倍率决定，与容量无关；90° 取消保留量上限但液量按流速递减。液层保持世界水平，网格覆盖旋转 Mask；出水来自下侧嘴沿锚点。
- `WaterVesselPourGraphic` 在 Mask 外、切面子树后绘制，前景窄液桥连接罐内与嘴沿，主水柱从嘴沿开始。透明根无底板射线，罐体接手势/拖放，按钮独立命中。
- `VesselContentsView` 在 Mask 下克隆真实库存槽，唯一空槽位于占用槽下层；重力/碰撞/阻尼只改姿态，关闭只销毁视觉。固体槽拒绝后仍可命中 IInventoryDragDropTarget 转液入口。
