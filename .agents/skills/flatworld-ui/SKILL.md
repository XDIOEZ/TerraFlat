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

- 主菜单为视觉基准：纯色灰阶 Image、近白文字、低对比边界，暖黄用于少量焦点/关键操作；面板从根到内容/按钮逐级提亮，普通边框约 2 参考像素、文字描边约 1 像素。物品图标/内容语义色独立于 Chrome 主题；信息 HUD 为角落锚点与透明容器。
- `FlatWorldUIThemeConfigCatalog → FlatWorldUITheme → BasePanel/构建器` 共用 ui-theme.json。公共控件为真正嵌套 Prefab，`ReusableUIControl` 持有内部结构/字体/Sprite/几何；使用处持有文案、事件、数值、选项与外部布局，页签由 `ReusableUITabVisual.SetSelected` 改状态。
- 正式 Prefab 持有完整视觉与序列化引用，领域控制器持有实例/业务，UIManager 管生命周期。控件节点名和 RuntimeUIPrefabKeys 为绑定身份，资产处于 Addressables `Prefab` 标签范围；UI 子目录继承根 UI.asmdef，脚本/资源移动保留 meta/GUID。
- 构建器先停用带 Awake 的根、填好引用再激活，保存时应用共享控件/主题。主题与公共控件迁移均为显式菜单操作；公共资产补建不覆盖人工编辑，迁移只处理自身所有的覆盖。
- 普通面板首个视觉底板为 `UI_PanelBackground`，根 Image 保留透明射线职责；HUD、加载幕、主菜单与无底板专属面板各自持有外观。确认/改名弹窗仅显示悬浮窗口，阻断层为透明 Image。
- Prefab 根 CanvasGroup.alpha=1，初始关闭由注册前 `InitClosed()` 建立；独立 Canvas 根 localScale=1。启动资源加载面板由 WorldManager.prefab 直接引用，独立于尚未初始化的资源字典。

## 生命周期、输入与排序

- 同一 PanelRoot 以兄弟顺序置顶/置底，sortingOrder 仅管理独立 Canvas。模态持有玩法锁和顶层手柄焦点，关闭/失败释放；常驻 HUD 输入透明，可收展 HUD 仅开关按钮接收射线。
- 主背包 `UI_Bag` 可并行，参与置顶/导航/取消链但不持玩法锁；库存/加工/容器背景输入透明，只有槽位、按钮与拖放/手势区命中，`UIWindowDragSurface` 命中标题/窄边框。快捷栏随 PanelRoot 排序；手机模态拖放由独立 Canvas 临时提升。
- 手机菜单/返回位于独立常驻层，返回顺序为顶层可取消面板→抽屉→设置。主菜单 CancelShortcutOverride 打开退出确认；嵌套危险确认先消费取消。存档删除统一由 `SaveDataManager_UI` 确认层提交并锁定目标身份。
- 文本通过 FlatWorldUI 表与 GetUiText/GetUiFormat，节点名不翻译；手柄焦点限定顶层面板，TMP 输入确认后进入虚拟键盘。鼠标按下焦点与列表业务选中状态分离，完整点击才提交选择。
- 面板可能先于模块销毁，Unity `!= null` 判断存活；即使面板已销毁仍释放锁/快捷转移引用。资源重载清空运行时 UI 与注册缓存，保留 UIRoot/SafeAreaRoot，再由当前 GameRes/Player 重建。
- `UI_WorldLoading` 为不透明黑幕与独立内容 CanvasGroup：进世界前覆盖并绘制一帧，区块表现就绪后内容→黑幕淡出→释放输入；同场景重生为单段淡出。
- 角色世界状态条使用高于 Water 的 Sorting Layer；直接挂普通 Transform 的 RectTransform 偏移以 anchoredPosition 为准。

## 布局、动画与公共控件

- UIRoot 使用 UIScaleController 与 1920×1080 基准；UIUserSettings 为缩放默认/范围权威。SafeAreaRoot 只约束交互，FullScreenRectController 反向扩展视觉背景，CanvasScaler 不再乘安全区比例。
- 安全区先确认属于当前屏幕，再归一化并在 Canvas 绘制前补更新；`SafeAreaScaleGroup` 对固定卡片只缩不放，presentationScale 在安全区适配后生效。快捷栏负底部间距保留为偏好，最终矩形避让强制手势区。
- 主菜单模态共用 SharedModalCardSize；MainMenuResponsiveLayout 以原位置和安全区共同限幅品牌/菜单/设置入口。移动端交互高度≥60、正文≥17 逻辑像素；内容增长由 ScrollRect 承载。
- 列表使用 `ItemStepScrollRect`，滚轮按 scrollDeltaPerTick 还原刻度，再按条目/网格行移动；单段长文本使用 fixedWheelStep。显式迁移只换 MonoScript GUID，保留组件 fileID/引用。
- `BasePanel → BaseUIAnimation → UIAnimationManager → JSON` 单向依赖，每面板最多一个动画组件，Opened/Closed 是同步业务事件。正式面板以 `ScaleUIAnimation/panel.scale` 绕 MotionRoot 可见中心缩放，动画不争写布局/安全区目标。
- 动画配置保存 Duration/相对 Offset/Scale/Ease，CanvasScaler 管分辨率；全局播放倍率只作用本系统根 Tween，热重载保留当前行程快照，反向开关保留进度，结束/禁用恢复姿态。
- 交互滑块与只读进度条为不同资产；滑块根为透明命中区，Background/Fill/Handle 持有外观，进度条禁交互/导航。动态 Content 引用来自所属容器，EventSystem 反馈持有唯一非缩放 Tween。

## 设置数据

- Data 契约仅含元数据/读写能力；功能管理器实现 `ISettingsProvider` 及 Toggle/Slider/Dropdown/Switch 能力，在自身生命周期注册。UI 按稳定 ProviderId/Key 绑定；选项身份为 SettingOption.Id，写入返回校验结果。
- `SettingsEditSessionController` 持有保存基线；非控件偏好由 `ISettingsEditSessionParticipant` 快照/还原，按键绑定单独编辑会话。Provider 不创建视觉，分页器收集子页 `ISettingsPageLifecycle`。
- 模拟半径为设备级 PlayerPrefs 偏好，三半径整组校验严格递增，Item/AIECS 共用；镜头缩放独立。视觉特效默认/范围来自 default-rendering.json，玩家值由 Provider 保存；太阳投影关闭不关闭脚底阴影。
- 主菜单/游戏内共用 NewWorldUserSettings。新世界窗口每次打开回填默认，名称/世界设置切页保留输入；返回先关难度层、再名称页、最后窗口。难度共用 GameDifficultyCatalog 的 0～20 级，摘要为战斗/生存/世界/生产。

## 业务视图契约

- 死亡界面 `UI_Death` 的主标题由 `Mod_PlayerDeathState.DeathMessage` 消费权威 `DeathCauseRecord`，通过 `DeathMessageCatalog` 与 `GameConfig/Combat/death-messages.json` 组合死因；来源缺失不编造名称。动态标题关闭静态 Binder，语言切换保留 Variant；正式 Prefab 与 `GameUIPrefabRebuilder.BuildDeathScreen` 同步多行自动字号布局。
- `MachinePanelFactoryRegistry` 按 LogicId 注册租约；通用会话管生命周期、库存绑定、权威请求，具体会话管专属控件。`IInstanceUI.IsPanelOpen` 来自真实面板，模块列表按创建/展开/操作事件刷新。
- 热栏外观由 `Common/Controls/UI_SelectBox.prefab` 与 Inventory_HotBar.SelectBoxPrefab 持有；选中框归属当前槽位。附属提示 ignoreLayout 且输入透明，Inventory.InitUI 按真实 ItemSlot_UI 计槽；手持名称读取装备热栏物品。
- 普通合成使用 `Gameplay/Crafting/UI_HandCraftTable/UI_MakerTable.prefab`，参考尺寸 1344×756；正式输入/输出/配方模板按真实容量复用。工业流体/宇航服/气罐使用 MechanicalPanelView 与 PreservePrefabLayout，宇航服入口共用 SpacesuitPanelSession 的真实供氧槽。
- 飞船 ShipPanelSession 的命令行/按钮共用执行入口，反馈历史有上限，查看历史不被消息置底。FuelInteraction 仅绑定通用模块，标题来自定义。任务/教程阅读入口为书籍/UI_ReadableBook。
- PlayerWorldCoordinateHUD 地表读取 WorldLocalPresentation 逻辑坐标，太空读取乘员双精度宇宙位置（支撑船姿态 + 本地位置 / FreeMotion），显示千米与米级精度，不随 ViewOrigin 改变；坐标行宽 560、下方温度/气压与季节行独立。环境温度采样独立于移动、显示按最终精度去重；FoodUIModule 仅为 IsLocalProfile 玩家持有角色 HUD。
- MOD 条目拖拽由共同父节点接收，来源为文件夹身份；跨页行复用保留来源，预览/落点线输入透明，关闭取消。

## 水容器表现

- `WaterVesselPanel.Appearances` 按物品 ID 提供剖面、内腔 Mask、水位区间、左右出口和 MouthWidth，未匹配时恢复默认外观；PNG Y 向下，水位/出口归一化 Y 向上。
- 液面/液流共用 LiquidDefinition.viscosity 与 VisualState/LiquidStyle。`WaterVesselLiquidGraphic` 为固定步长连续波面，LiquidStyle 管浑浊/沉淀/污膜/颗粒；Graphic 帧率不结算液量。
- 倾倒流量由基准流速×嘴宽比例×倾角倍率决定，与容量无关；90° 取消保留量上限但液量按流速递减。液层保持世界水平，网格覆盖旋转 Mask；出水来自下侧嘴沿锚点。
- `WaterVesselPourGraphic` 在 Mask 外、切面子树后绘制，前景窄液桥连接罐内与嘴沿，主水柱从嘴沿开始。透明根无底板射线，罐体接手势/拖放，按钮独立命中。
- `VesselContentsView` 在 Mask 下克隆真实库存槽，唯一空槽位于占用槽下层；重力/碰撞/阻尼只改姿态，关闭只销毁视觉。固体槽拒绝后仍可命中 IInventoryDragDropTarget 转液入口。
