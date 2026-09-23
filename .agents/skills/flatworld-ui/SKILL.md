---
name: flatworld-ui
description: "Use when: 定位或修改 FlatWorld 的 UIManager、BasePanel、主菜单、新游戏、存档列表、游戏内 UI、控件命名、动态 UI、UI 文案、多语言、音效或 UI Prefab。关键词：UIManager、BasePanel、GameManager.UI、SaveDataManager_UI、LocalizedTextBinder。"
---

# FlatWorld UI

## 统一视觉风格（新建 UI 必须遵守）

- 后续新建及重新设计的游戏 UI，以主菜单参考图为统一视觉基准：中性灰表面、近白文字、低对比细边界，只用少量暖黄表示焦点/关键操作。不要再为普通面板、按钮、输入框、槽位引入蓝绿或高饱和装饰主题。
- 通用 UI Chrome 默认使用 uGUI `Image` 的纯色灰阶表面，不依赖额外生成图片或烘焙色彩的 UI Sprite；游戏物品图标、世界美术和必要的功能图标保持原资源与原色，不被主题层染色。
- 面板层级主要通过明度区分：根面板最暗、内容区略亮、按钮/标题栏再抬一级；普通 UI 边框统一使用约 2 个参考像素的低透明白/灰线，文字描边仍保持约 1 像素，避免像素字体发糊；不叠加厚重双描边、木框或高对比阴影。
- 暖黄只用于细强调线、焦点描边、选择状态和少量关键操作；危险/生命等玩法语义色允许保留低饱和状态色，但不能让整套界面重新变成彩色主题。
- 文字继续使用现有 TMP/本地化字体与移动端字号、触控尺寸约束；标题、正文、说明只靠字号/明度/字重分级，禁止为填充视觉新增装饰性英文眉题、重复说明或无意义标签。
- 正式视觉必须落在可复用控件/Prefab 与 `FlatWorldUITheme` 中；Prefab 构建器保存前应重新应用统一主题，避免未来重建时恢复旧蓝绿/图集皮肤。仅修改业务行为时，不顺带整体翻修既有界面。
- `FlatWorldUIThemeMigrator` 只能通过 `FlatWorld/UI/主题迁移/` 菜单显式执行；禁止使用 `[InitializeOnLoad]`、`delayCall`、`EditorApplication.update` 等启动/重载钩子自动遍历并保存全部 UI Prefab，避免仅打开 Unity 就污染 Git 工作区。迁移版本升级后由开发者主动执行“执行当前版本迁移”，需要覆盖重烘焙时再使用“强制重新应用统一主题”。

## 入口

- 生命周期：`Assets/5_Scripts/5-5_UI/Core/{UIManager,BasePanel}.cs`
- 通用控件/表现：`Assets/5_Scripts/5-5_UI/Common/{Controls,Presentation}/`；输入：`Assets/5_Scripts/5-5_UI/Input/`
- 主菜单：`Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameManager.UI.cs`；存档 UI：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/SaveDataManager_UI.cs`
- 游戏内 UI：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/`
- Prefab：`Assets/2_Prefabs/2-1_UI/`；根：`Assets/Resources/UI/UIRoot.prefab`
- 运行时键/构建器：`Assets/5_Scripts/5-5_UI/Core/RuntimeUIPrefabKeys.cs`、`Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.cs`

## UI Prefab 索引

`UI_ActionList.prefab` - 设置功能列表面板
`UI_AudioSettings.prefab` - 音频设置面板
`UI_AutoSaveSettings.prefab` - 自动保存设置面板
`UI_Bag.prefab` - 背包面板
`UI_BasePanel.prefab` - 通用基础面板模板
`UI_BaseText.prefab` - 通用基础文本模板
`UI_Bonfire.prefab` - 篝火制作面板
`UI_BuffStatus.prefab` - 状态效果面板
`UI_BuffStatusItem.prefab` - 状态效果条目模板
`UI_CameraControlSettings.prefab` - 相机控制设置面板
`UI_CharacterSpeechBubble.prefab` - 角色对话气泡
`UI_CompostBin.prefab` - 堆肥桶交互面板
`UI_CoordinateDisplaySettings.prefab` - 坐标显示设置面板
`UI_Death.prefab` - 玩家死亡界面
`UI_Debug.prefab` - 调试面板
`UI_DebugScrollView.prefab` - 调试滚动列表面板
`UI_DifficultySettings.prefab` - 难度设置面板
`UI_DimensionLoading.prefab` - 维度加载面板
`UI_Equipment.prefab` - 装备面板
`UI_FireDrill.prefab` - 钻木取火面板
`UI_FlintStrike.prefab` - 燧石取火面板
`UI_Food.prefab` - 玩家饱食状态面板
`UI_FuelInteraction.prefab` - 通用燃料补充与点火交互面板
`UI_Furnace.prefab` - 熔炉制作面板
`UI_Hand.prefab` - 手持物面板
`UI_HandCraftTable.prefab` - 手工制作面板
`UI_HandSlot.prefab` - 手持槽位组件
`UI_Health.prefab` - 玩家生命状态面板
`UI_HotBar.prefab` - 快捷栏面板
`UI_InputBindingRow.prefab` - 按键绑定行组件
`UI_InputBindingSettings.prefab` - 按键绑定设置面板
`UI_InterfaceSettings.prefab` - 界面设置面板
`UI_MainMenu.prefab` - 主菜单面板
`UI_MainMenuExitConfirmation.prefab` - 主菜单退出确认面板
`UI_MainMenuSettings.prefab` - 主菜单设置面板
`UI_MakerTable.prefab` - 制作台面板
`UI_MeatRack.prefab` - 晾肉架面板
`UI_MobileControls.prefab` - 移动端控制面板
`UI_ModuleButton.prefab` - 角色状态模块按钮
`UI_ModuleList.prefab` - 生存状态模块列表
`UI_ModuleOpenButton.prefab` - 生存状态列表打开按钮
`UI_ModuleSettings.prefab` - 状态模块设置面板
`UI_NetworkMode.prefab` - 联机模式选择面板
`UI_NewGame.prefab` - 新建世界面板
`UI_PlayerChatInput.prefab` - 玩家聊天输入面板
`UI_PlayerWorldCoordinate.prefab` - 玩家世界坐标 HUD
`UI_QuestTracker.prefab` - 任务追踪面板
`UI_QuestTrackerItem.prefab` - 任务追踪条目模板
`UI_ResourceLoading.prefab` - 资源加载面板
`UI_RuntimeDebugOverlay.prefab` - 运行时调试覆盖层
`UI_SaveContextMenu.prefab` - 存档上下文菜单
`UI_SaveSelectionButton.prefab` - 存档选择按钮组件
`UI_SaveSelectionPanel.prefab` - 存档选择面板
`UI_SaveStatus.prefab` - 存档状态提示 HUD
`UI_SeasonSettings.prefab` - 季节设置面板
`UI_SelectBox.prefab` - 通用选择框控件
`UI_Sleep.prefab` - 玩家睡眠状态面板
`UI_Slider.prefab` - 通用滑动条控件
`UI_Slot.prefab` - 通用物品槽位组件
`UI_VisualEffectsSettings.prefab` - 视觉效果设置面板
`UI_WaterVessel.prefab` - 水容器交互面板
`UI_WorldLoading.prefab` - 世界加载面板
`UI_WorldStreamingSettings.prefab` - 世界流式加载设置面板
`UIRoot.prefab` - UI 根节点预制体

## 架构与运行时约束

- 面板动画位于 `Common/Animation/`，依赖固定为 `BasePanel → BaseUIAnimation → UIAnimationManager → JSON`：BasePanel 直接调用同物体 BUA，BUA 禁止反向引用或监听 BasePanel；`Opened/Closed` 继续保持同步业务事件，无动画组件时维持即时开关。
- 每个 BasePanel 同物体最多一个 `BaseUIAnimation` 或子类，稳定 `AnimationId` 匹配 `Resources/Config/UIAnimations.json`。JSON 只保存 Duration、相对 Offset、Scale、Ease 等结果参数，不保存移动/开关速度，也不按 `Screen.width/height` 二次换算；分辨率适配交给现有 CanvasScaler。
- `UIAnimationManager` 缓存校验后的配置并管理已注册动画的统一运行时播放倍率；倍率只作用于本系统根 Tween，不修改 DOTween/Unity 全局时间。热重载不得改正在播放行程的几何/时长快照。
- 滑动/缩放使用独立 MotionRoot，避免与安全区、LayoutGroup、拖拽器争写同一 RectTransform；反向开关保留当前进度，结束/禁用恢复姿态。正式接入需同步 Prefab 与构建器，禁止启动时自动批量迁移加载遮挡层/HUD。

- 液体视觉黏稠度使用 `LiquidStyle.Viscosity`，与浑浊度独立；液面和罐口液流必须共用该参数，不能按蜂蜜等具体液体 ID 分支。默认值 0 保持水的表现。新增样式通过 `FlatWorld/UI/Sync Water Vessel Liquid Styles` 定向写入现有 Prefab，避免为了新增液体重建容器外形或覆盖已有外观配置；完整重建和定向同步共用样式工厂。

- 水容器外形通过正式面板的 `WaterVesselPanel.Appearances` 按物品 ID 配置，运行时不按具体容器写分支；一套外观必须同时提供剖面、同画布内腔遮罩、归一化水位区间及左右出口。未匹配的容器恢复 Awake 捕获的默认外观，避免共用面板从椰子壳切回陶罐后残留遮罩或出口；正式 Prefab 与构建器必须同步维护。PNG 的 Y 从顶部向下，水位及出口归一化 Y 从底部向上。

- 水容器剖面通过 `WaterVesselLiquidGraphic` 和独立内腔 Mask 呈现；视觉配置按 `LiquidDefinition.VisualState` 匹配，不在 UI 重建水质枚举。`LiquidStyle` 的 `Murkiness/Sediment/SurfaceDebris/SuspendedParticles` 负责通用浑浊、沉淀、污膜和悬浮颗粒表现，罐口液流读取同一套颜色与浑浊度参数，禁止按具体液体 ID 单独硬编码。罐口倾倒液流使用正式 Prefab 中位于 `陶罐剖面`、但处于内腔 Mask 之外的 `倾倒液流/WaterVesselPourGraphic`；出水位置必须来自挂在 `陶罐切面` 下的左右罐口出口锚点，并按倾角选择下侧嘴沿，禁止再用“罐体中心 + 固定半径”猜测。当前陶罐嘴沿较厚，液流节点必须排在 `陶罐切面` 子树之后绘制，并用一段窄的前景液桥从嘴沿向罐内延伸连接罐腹水体；外部主水柱从嘴沿开始并与液桥重叠，禁止再把整条液流放到罐体后方，否则厚嘴沿会把根部完全遮断。新增表现状态需同步正式 Prefab 的 Styles；自定义 Graphic 必须显式声明 CanvasRenderer 依赖，避免预制体有脚本却不渲染。
- `UI_WaterVessel` 采用与石臼一致的无底板玩法面板：根 `Image` 与 `设置对话框/Image` 只保留透明射线阻挡，不绘制灰色背景或描边；统一主题不得把这两层重新着色，按钮仍按通用主题单独显示。

- 领域控制器创建/持有正式 Prefab，`UIManager` 管生命周期；控件节点名是绑定契约。正式 UI 不用 `new GameObject/AddComponent` 拼视觉。
- `UI_FuelInteraction` 只绑定通用 `Mod_FuelInteraction`，标题从当前物品定义读取；面板、控制器和文案逻辑不得按火把、油灯、火盆等具体物品 ID 分支。
- Prefab 是视觉真相；`BasePanel` 不在初始化时重写结构。运行时只用稳定键加载正式 Prefab。 编辑器构建器组装带 Awake 的视图时，应先停用根节点，完成所有序列化引用后再激活；新增必需视图引用必须同步生成正式 Prefab 并核对引用，不能只提交脚本。
- `GameRes` 的启动资源加载面板属于引导 UI：可以登记 Addressables，但运行时必须由 `WorldManager.prefab` 直接引用，不能依赖尚未初始化的资源字典。
- `UI_WorldLoading` 的根 `Image` 是不透明黑幕，`加载内容` 子节点有独立 `CanvasGroup`。新建/继续世界前须等黑幕完全覆盖并绘制一帧；区块表现真正就绪后先淡出内容、再淡出黑幕，最后释放玩法输入。同场景重生仍使用原有单段淡出。统一主题、迁移器和 Prefab 重建流程不得把根图改成半透明，也不得移除内容透明度控制。
- 同一 `PanelRoot` 下的面板置顶/置底必须使用 `SetAsLastSibling`/`SetAsFirstSibling`；全局层级序号只能用于独立 Canvas 的 `sortingOrder`，不能直接当作兄弟索引。
- 不经过 `UIManager`/`BasePanel` 打开流程的独立 Canvas，Prefab 根节点必须显式固化 `localScale = Vector3.one`；不能依赖面板动画在运行时恢复可见缩放。
- 槽位内的选中框、背景和装饰必须按槽内兄弟顺序分层；选中框切换时必须跟随当前槽位，不得留在旧槽位后再用世界坐标跨槽移动。
- 快捷栏的附属提示随 `UI_HotBar` 缩放和安全区移动，必须用 `LayoutElement.ignoreLayout` 排除在九格布局外，并保持图形与 CanvasGroup 输入透明；`Inventory.InitUI` 按实际 `ItemSlot_UI` 统计和管理槽位，不能按容器全部子节点计数。手持名称读取实际装备的快捷栏物品，不能与 `Inventory_Hand` 的拖拽携带物混用。
- 设置类模态页（主设置及其子页）需要独立高层 Canvas 与 `GraphicRaycaster`；非交互对话气泡在玩法模态打开时隐藏，避免首帧或跨 Canvas 绘制顺序造成遮挡。
- 常驻 HUD 不拦截输入，Graphic 关闭 raycastTarget；若 HUD 提供展开/收起功能，只允许开关按钮接收 raycast，内容和装饰元素仍必须输入透明；模态面板才获取输入锁和顶层手柄焦点，关闭/失败路径释放。
- 跟随角色的世界空间状态条若需要在水面上方可见，Canvas 的 Sorting Layer 必须高于项目 `Water` 层；`sortingOrder` 只能解决同一 Sorting Layer 内的前后关系。RectTransform 直接挂到普通 Transform 下时，实际偏移以 `anchoredPosition` 为准，不能只改序列化的 `localPosition`。
- 手机 HUD 的菜单/返回入口必须独立于可隐藏的玩法控制层；模态玩法面板打开时保留该入口并允许背包/制作等面板并行打开，Android 返回键或 Escape 优先关闭最上层可取消面板，避免移动端失去退出路径。
- 手机左侧“奔跑”是 `UI_MobileControls.prefab` 的状态按钮，但两态颜色会由 `PlayerMobileControlsHUD.RefreshRunButtonVisual` 在运行时重写；统一主题时不能只改 Prefab。关闭态保持灰黑表面与低对比边界，开启态仍用灰阶底，只允许暖黄描边/状态标记作为少量状态强调。
- 主菜单属于不可直接关闭的根面板；Android 返回键、Escape 或手柄取消应通过 `BasePanel.CancelShortcutOverride` 打开正式退出确认 Prefab，只有确认按钮退出应用，取消或再次返回只关闭确认层。
- 坐标、角色状态等信息型 HUD 使用屏幕角落锚点和透明容器，只显示会随运行时变化的字段/状态条；禁止为这类 HUD 添加整块背景、卡片标题或装饰性介绍文字。
- 左上角 `PlayerWorldCoordinateHUD` 的环境温度必须从玩家当前位置调用统一逐格温度查询，不读取角色体温或星球全局温度；环境采样不能绑定到“坐标改变”条件，玩家静止时天气和冷热源仍会改变读数，只按最终显示精度去重文本刷新。
- 角色参数 HUD 由 FoodUIModule 随模块生命周期创建/释放，只为已设定 IsLocalProfile 的本地 Player 创建；Food 模块也用于动物和静态食物，不能按模块存在或保存的 UI 显隐状态决定是否创建 HUD。
- 常驻组件事件驱动；禁止等待绑定或比较静态状态的 Update/LateUpdate 和逐帧 `GetComponent*`。
- 动态列表复用条目；结构变化才局部 MarkLayoutForRebuild，数值/颜色更新不强制布局。热路径禁止 ForceUpdateCanvases/ForceRebuild。
- 动态列表的通用节点名（如 `Content`）不得在整个面板全局查找；必须从所属 `ScrollRect` 或业务容器取引用，避免与 Dropdown 模板等同名节点串容器。
- EventSystem 反馈保持唯一非缩放 Tween，重入先 Kill，失活/销毁清理。
- 手机准线是 `UI_MobileControls.prefab` 的非交互 Graphic，由 `PlayerMobileControlsHUD` 按统一屏幕指针定位；不得让准线 Graphic 参与射线或手柄焦点。
- 旧缓存 Prefab 缺少手机准线节点时允许由 HUD 做一次性兼容补齐，不能把该兜底扩展成运行时拼装整套手机 UI。
- GM 调试面板由 `GMReflectionConsole` 运行时动态构建，不通过正式 UI Prefab；可持久化的调试开关统一放入 `GMConsolePreferences`，按钮状态需在场景切换和面板刷新时同步。图层页的世界观察模式由 `GMWorldLayerOverlay` 统一承载，同一时刻只显示一种热力图，避免温度与污染颜色叠加失真。
- GM 动态按钮的 `Selectable.ColorBlock` 是乘在深灰 `Image` 底色上的状态 Tint；禁用态应保持接近白色且不降低 Alpha，只通过轻微乘色降低一级明度。禁止使用半透明中灰作为 `disabledColor`，否则统一灰阶主题会把禁用按钮乘成近黑色，误导为视觉故障。
- 层级显示中的导航模式也走统一观察模式与透明度偏好；新增 `GmWorldLayerMode` 只追加枚举数值，不能重排已保存的模式。导航按钮保持独立行，避免挤压原有温度/污染按钮及透明度滑轨；箭头表示朝本地玩家的共享寻路场，不代表每只怪物当前都在追击。
- 日志页的 GM 入口广播 `RuntimeDebugOverlay.GmPanelOpenRequested`，由 `GMReflectionConsole` 订阅；日志属于 GamePlay，而 GM 属于依赖 GamePlay 的 `FlatWorld.Gameplay.Debug`，禁止反向直接引用。日志 Canvas 排序高于 GM，打开 GM 前先收起日志页。GM 点选传送层仅在主动选点时启用，持有独立触点和玩法输入锁；关闭、失焦和换场景必须释放。
- GM 分页枚举数值由 `ActivePageIndex` 保存；新页追加枚举项，显示顺序由 `BuildTabBar` 决定。页签横向内容宽度由布局计算，禁止恢复手写总宽而截断末尾分页。世界观察层独立于 GM 窗口显隐，关闭窗口只收起操作界面，不能顺带关闭观察层。
- 宣传片录制模式由 `UIManager` 统一管理：裸 F1 只临时停用全部已加载 `Canvas` 与 `GraphicRaycaster`，必须保存并恢复原 `enabled` 状态，不能通过 Close/ShowAll 改变业务面板开关；录制期间新建 Canvas 要在渲染前继续纳入隐藏。
- 主菜单控件名集中在 `GameManager.UI.cs`；定向构建 Prefab，避免无关重写。
- 主菜单仍保留柔焦世界背景和专属排版，但它是全局灰阶主题的视觉源：灰按钮、近白文字、淡金点缀必须与 `FlatWorldUITheme` 保持一致。只换主菜单布局/背景时使用 `MainMenuPrefabBuilder.ApplyReferenceStyle` 原位更新正式 Prefab，不调用清空子节点的完整重建入口。
- 玩家行囊 `UI_Bag` 不再使用独立的 Modular Inventory 彩色槽位皮肤；动态槽位直接沿用通用 `UI_Slot.prefab` 的灰阶简约样式，避免同类库存界面出现两套视觉语言。`InventorySlotVisualProfile` 仅保留为未来明确需要局部皮肤时的可选机制，不默认挂载。
- `SafeAreaRoot` 只约束交互内容；挂在其下的全屏背景使用 `FullScreenRectController` 反向扩展到根 Canvas，背景图用 `AspectRatioFitter.EnvelopeParent` 等比裁切。`CanvasScaler` 不再乘安全区比例，避免与 `SafeAreaRectController` 双重缩小 UI。
- 固定参考尺寸的主菜单模态卡片如果会因用户 UI 缩放或手机安全区高度不足而越界，应在面板根使用 `SafeAreaScaleGroup` 对卡片及其投影做统一“只缩不放”限幅；不要为了单个模态页去修改全局 `CanvasScaler`，否则 HUD 与其它面板会被连带缩小。

## 设置 Provider 契约

- 通用契约位于 `Assets/5_Scripts/5-1_Data/Settings/SettingsContracts.cs`，由 `Data` 程序集提供；它只描述设置元数据和读写能力，不引用 `UnityEngine.UI`、TMP 或具体 Prefab。
- 功能管理器要出现在设置中时实现 `ISettingsProvider`，按需提供 `ISettingsToggleProvider`、`ISettingsSliderProvider`、`ISettingsDropdownProvider`、`ISettingsSwitchProvider`；四类控件分别对应开关、滑动条、下拉列表和按钮式互斥切换。
- `ProviderId` 与设置 `Key` 必须稳定且按功能命名；运行时在管理器自身生命周期中通过 `SettingsProviderRegistry.Register/Unregister` 注册，UI 通过 Provider 和 Key 查找，不直接调用管理器的业务字段或 `AudioBus` 等实现细节。
- `ISettingsDropdown`/`ISettingsSwitch` 的选项使用稳定 `SettingOption.Id`，写入通过 `TrySetSelectedIndex` 返回错误；需要“应用/取消”或自定义输入的页面保留专用 View 状态，最终提交仍调用 Provider，不能把校验逻辑塞回 `BasePanel`。
- 现有静态偏好类通过 `SettingsProvider` 兼容入口注册；新增实例型系统优先让管理器直接实现接口。Provider 不负责创建 Prefab，正式布局仍由专用 Launcher 和 Prefab 管理。
- `UI_VisualEffectsSettings` 同时嵌套在游戏内与主菜单设置中；太阳长投影与柔化开关、模糊程度滑块绑定 `SunShadowSettings` Provider，关闭太阳投影必须停止对应渲染工作，柔化关闭只把有效强度置零。地面层级阴影开关和宽度滑块绑定 `GroundElevationShadowSettings` Provider。新增必需控件时同步源 Prefab、控制器和 `RuntimeUIPrefabBuilder.VisualEffects`，并核对两个嵌套使用处的真实引用。
- 游戏内设置页签由 `SettingsActionListPagination` 的页面名、入口名、页签映射和首个焦点控件共同定义；新增分页时同步正式 `UI_ActionList` 嵌套 Prefab 与完整/定向构建入口。直接挂在子页 Prefab 的控制器会由分页器收集 `ISettingsPageLifecycle`，不必再向 `SettingCanvas` 添加专用初始化分支。
- 调整界面缩放范围或默认值时，以 `UIUserSettings` 常量为权威，同时检查 Provider/写入校验、`UIScaleController` 的实际应用下限，并同步 `UI_InterfaceSettings.prefab` 与 `RuntimeUIPrefabBuilder`；`PlayerPrefs` 默认参数只服务无旧键的新配置，不得覆盖已有玩家值。

## Prefab 与目录约束

- 公共控件位于 `Common/Controls/UI_Button、UI_CloseButton、UI_TabButton、UI_Toggle、UI_SwitchOption、UI_Switch、UI_SliderControl、UI_ProgressBar、UI_Dropdown、UI_InputField.prefab`。窗口应使用真正的嵌套实例；关闭按钮/页签继承按钮，进度条继承滑块，互斥选项继承开关。不要 Unpack 或复制层级来复用。
- `ReusableUIControl` 标记子树的外观所有权；主题兼容层不得覆盖公共控件的颜色、字体、内部几何。使用处只保存文案、业务事件/数值、选项及外部布局差异，颜色等外观应编辑源 Prefab 或有明确用途的 Variant，否则会阻断统一风格传播。
- 页签业务选中状态由 `ReusableUITabVisual` 的 Prefab 字段决定，分页控制器只调用 `SetSelected`，不在业务脚本中重新写死公共页签配色。原位转换使用 `ObjectMatchMode.ByHierarchy` 保留未匹配原组件；清理嵌套实例覆盖时只处理 `ReusableUIControl` 所有的目标，因为 Unity 可能返回整个外层面板的覆盖集合。
- `FlatWorld/UI/Shared Controls/` 提供补建缺失资产、显式迁移和验证菜单；补建不覆盖已有公共 Prefab 的人工编辑。`RuntimeUIPrefabBuilder` 保存前应执行公共控件准备流程，避免重建重新生成独立控件。迁移不重建整个窗口，专用图标/复杂卡片与不匹配层级不强制替换。
- 可交互滑块和只读进度条使用不同资产。滑块根 Image 是透明命中区，Background/Fill/Handle 管理可见部分；根布局不得被主题当作轨道改锚点，手柄必须有非零高度。音量行预留 72 像素，滑块命中区为 60 像素；进度条禁用交互和导航。

- 创建 UI Prefab 时必须按用途放入 `Assets/2_Prefabs/2-1_UI/` 下合适的分类目录；优先复用 `Common`、`Gameplay`、`MainMenu`、`Settings`，不要把 Prefab 直接堆在 UI 根目录。现有分类都不匹配时，才新增职责明确的子目录。
- 普通合成正式 Prefab 为 `Gameplay/Crafting/UI_HandCraftTable.prefab` 与 `UI_MakerTable.prefab`：参考画布下固定 1344×756（约占 1920×1080 的 70%），普通边框 2 个参考像素，主要按钮高度不低于 60；手工台契约为 `输入_1...输入_4`，世界制作台为 `输入_1...输入_5`，两者共用 `输出_1/2`、`配方候选内容`、隐藏的 `配方候选模板`、`合成按钮`、`关闭`。运行时只复用模板生成候选项，不拼装视觉层级；`UI_MakerTable` 根节点不得保留旧 `Image` 流程箭头。
- 新增正式 Prefab 必须位于 Addressables `Prefab` 标签范围，并登记稳定加载键；移动或重命名资源时保留 `.meta`，同步检查加载键与引用。
- `5-5_UI` 的子目录统一继承根 `UI.asmdef`；整理脚本时使用 `AssetDatabase.MoveAsset` 连同 `.meta` 移动，不新建子程序集或重生成 GUID，避免 Prefab 上的 MonoScript 引用失效。

## 文案、焦点与联动

- 所有玩家可见文字同时使用 `flatworld-localization`：静态文本进入 `FlatWorldUI`，动态模板登记英文覆盖并使用 GetUiText/GetUiFormat；节点名不翻译。
- 同类主菜单模态面板（新建世界、存档选择、设置、联机）统一使用 `FlatWorldUIPanelMetrics.SharedModalCardSize`；调整任一面板尺寸时必须同步检查其余面板的卡片尺寸、锚点、边距和内容是否越界。
- 主菜单模态挂在 `SafeAreaRoot` 时，交互卡片继续受安全区约束；需要覆盖刘海区的纯视觉暗幕应作为独立子节点使用 `FullScreenRectController` 反向扩展，不能把整张交互卡扩到根 Canvas。
- 主菜单移动端流程的主要按钮和输入框触控高度不低于 60 逻辑像素，正文/说明文字不低于 17；内容增长优先交给 `ScrollRect`，并在 Device Simulator 中逐页检查存档、新建世界、难度、联机和设置窗口。
- 游戏内设置子页同样遵守 60 逻辑像素触控下限；下拉框根节点、下拉模板条目、输入框与应用/取消按钮都不能沿用桌面端紧凑高度，尤其避免 `ScrollRect` 把小尺寸下拉项的点按误判为拖动。
- 面板文案只保留完成当前操作所需的标题、字段名、状态、按钮和必要提示；删除眉题、重复介绍、流程串、装饰性英文和不会改变操作结果的占位说明，禁止为了填充留白新增无用文字。
- 手柄焦点限制在当前顶层导航面板；TMP 输入框在确认后才进入虚拟键盘编辑。
- ScrollRect 内可点击条目的鼠标按下焦点不能复用业务选中视觉；拖拽起点保持普通态，完整点击后再由领域选择状态高亮，键盘/手柄导航焦点可独立显示。
- 父面板内嵌危险操作确认层时，确认层必须优先消费手柄取消/Escape；第一次取消只关闭确认层并保留安全状态，不能直接关闭父面板或执行操作。
- UI 音效联动 Audio；存档/联机/背包/Quest/Buff HUD 只加载实际命中的领域 Skill。

## 验证

- 所有正式列表使用 `ItemStepScrollRect`；滚轮先除以当前 InputSystemUIInputModule 的 `scrollDeltaPerTick` 还原刻度，再按实际条目/网格行尺寸平滑移动。不要重新用固定的 120 或大像素倍率补偿；触摸拖动仍走 ScrollRect 原有路径。
- 旧 ScrollRect Prefab 的迁移使用显式 `ItemStepScrollRectMigration`，仅替换 MonoScript GUID，保留组件 fileID、内容/视口/滚动条引用；运行时列表和重建器也必须创建同一派生类型，不能只修改现有资源。
- `SafeAreaScaleGroup.presentationScale` 在安全区适配后生效，用于单独缩小内容；不能修改全屏输入层或 CanvasScaler。手工制作为 0.6，其他制作面板为 1；隔离预览断言必须使用相同的二阶段缩放公式。

- 检查 Prefab/节点/组件/事件、重复开关、输入锁、焦点边界、输入穿透、条目复用和本地化切换；最终布局再人工看。

## Skill 维护原则

- Device Simulator/窗口切换可让 `Screen.safeArea` 暂时属于上一分辨率；安全区应先校验属于当前屏幕，再计算锚点，并在 Canvas 绘制前补齐延后的屏幕变化。安全区根与系统手势计算共用同一归一化入口，不允许超出 [0,1] 的锚点。
- 快捷栏负底部间距是用户偏好，不应被重置；最终显示按变换后的矩形限制在安全区/强制手势边界之上。不要用固定像素补偿屏幕比例或忽略父节点缩放。
- GM 垂直主布局中的 Header/Search 水平布局必须关闭 `childForceExpandHeight`，否则子布局的弹性高度会抢占 Page Host，导致统计区被无意义的大标题/搜索框挤出可视范围。

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
- 创建正式 UI Prefab 时必须直接完成可用版本。不能只创建节点骨架或占位结构后交付；必须在 Unity Prefab Mode 中基于现有参考 Prefab（如 UI_Health、UI_ProgressBar 等）手动补齐视觉组件、布局组件、颜色、字体、嵌套 Prefab 引用和必要序列化配置。Prefab 提交时应达到可直接查看和使用的完成状态，后续代码绑定只负责数据驱动，不负责补视觉。
