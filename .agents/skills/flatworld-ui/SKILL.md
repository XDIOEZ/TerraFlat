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

- 领域控制器创建/持有正式 Prefab，`UIManager` 管生命周期；控件节点名是绑定契约。正式 UI 不用 `new GameObject/AddComponent` 拼视觉。
- Prefab 是视觉真相；`BasePanel` 不在初始化时重写结构。运行时只用稳定键加载正式 Prefab。 编辑器构建器组装带 Awake 的视图时，应先停用根节点，完成所有序列化引用后再激活；新增必需视图引用必须同步生成正式 Prefab 并核对引用，不能只提交脚本。
- `GameRes` 的启动资源加载面板属于引导 UI：可以登记 Addressables，但运行时必须由 `WorldManager.prefab` 直接引用，不能依赖尚未初始化的资源字典。
- `UI_WorldLoading` 的根 `Image` 是进入世界阶段的硬遮挡层，最终 Alpha 必须保持 `1`；统一主题、迁移器和 Prefab 重建流程都不能把它降成普通面板的半透明 Canvas，否则玩法 HUD 会在加载期间透出。
- 同一 `PanelRoot` 下的面板置顶/置底必须使用 `SetAsLastSibling`/`SetAsFirstSibling`；全局层级序号只能用于独立 Canvas 的 `sortingOrder`，不能直接当作兄弟索引。
- 不经过 `UIManager`/`BasePanel` 打开流程的独立 Canvas，Prefab 根节点必须显式固化 `localScale = Vector3.one`；不能依赖面板动画在运行时恢复可见缩放。
- 槽位内的选中框、背景和装饰必须按槽内兄弟顺序分层；选中框切换时必须跟随当前槽位，不得留在旧槽位后再用世界坐标跨槽移动。
- 快捷栏的附属提示随 `UI_HotBar` 缩放和安全区移动，必须用 `LayoutElement.ignoreLayout` 排除在九格布局外，并保持图形与 CanvasGroup 输入透明；`Inventory.InitUI` 按实际 `ItemSlot_UI` 统计和管理槽位，不能按容器全部子节点计数。手持名称读取实际装备的快捷栏物品，不能与 `Inventory_Hand` 的拖拽携带物混用。
- 设置类模态页（主设置及其子页）需要独立高层 Canvas 与 `GraphicRaycaster`；非交互对话气泡在玩法模态打开时隐藏，避免首帧或跨 Canvas 绘制顺序造成遮挡。
- 常驻 HUD 不拦截输入，Graphic 关闭 raycastTarget；若 HUD 提供展开/收起功能，只允许开关按钮接收 raycast，内容和装饰元素仍必须输入透明；模态面板才获取输入锁和顶层手柄焦点，关闭/失败路径释放。
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
- 日志页的 GM 入口广播 `RuntimeDebugOverlay.GmPanelOpenRequested`，由 `GMReflectionConsole` 订阅；日志属于 GamePlay，而 GM 属于依赖 GamePlay 的 `FlatWorld.Gameplay.Debug`，禁止反向直接引用。日志 Canvas 排序高于 GM，打开 GM 前先收起日志页。GM 点选传送层仅在主动选点时启用，持有独立触点和玩法输入锁；关闭、失焦和换场景必须释放。
- GM 分页枚举数值由 `ActivePageIndex` 保存；新页追加枚举项，显示顺序由 `BuildTabBar` 决定。页签横向内容宽度由布局计算，禁止恢复手写总宽而截断末尾分页。世界观察层独立于 GM 窗口显隐，关闭窗口只收起操作界面，不能顺带关闭观察层。
- 主菜单控件名集中在 `GameManager.UI.cs`；定向构建 Prefab，避免无关重写。
- 主菜单仍保留柔焦世界背景和专属排版，但它是全局灰阶主题的视觉源：灰按钮、近白文字、淡金点缀必须与 `FlatWorldUITheme` 保持一致。只换主菜单布局/背景时使用 `MainMenuPrefabBuilder.ApplyReferenceStyle` 原位更新正式 Prefab，不调用清空子节点的完整重建入口。
- 玩家行囊 `UI_Bag` 不再使用独立的 Modular Inventory 彩色槽位皮肤；动态槽位直接沿用通用 `UI_Slot.prefab` 的灰阶简约样式，避免同类库存界面出现两套视觉语言。`InventorySlotVisualProfile` 仅保留为未来明确需要局部皮肤时的可选机制，不默认挂载。
- `SafeAreaRoot` 只约束交互内容；挂在其下的全屏背景使用 `FullScreenRectController` 反向扩展到根 Canvas，背景图用 `AspectRatioFitter.EnvelopeParent` 等比裁切。`CanvasScaler` 不再乘安全区比例，避免与 `SafeAreaRectController` 双重缩小 UI。

## 设置 Provider 契约

- 通用契约位于 `Assets/5_Scripts/5-1_Data/Settings/SettingsContracts.cs`，由 `Data` 程序集提供；它只描述设置元数据和读写能力，不引用 `UnityEngine.UI`、TMP 或具体 Prefab。
- 功能管理器要出现在设置中时实现 `ISettingsProvider`，按需提供 `ISettingsToggleProvider`、`ISettingsSliderProvider`、`ISettingsDropdownProvider`、`ISettingsSwitchProvider`；四类控件分别对应开关、滑动条、下拉列表和按钮式互斥切换。
- `ProviderId` 与设置 `Key` 必须稳定且按功能命名；运行时在管理器自身生命周期中通过 `SettingsProviderRegistry.Register/Unregister` 注册，UI 通过 Provider 和 Key 查找，不直接调用管理器的业务字段或 `AudioBus` 等实现细节。
- `ISettingsDropdown`/`ISettingsSwitch` 的选项使用稳定 `SettingOption.Id`，写入通过 `TrySetSelectedIndex` 返回错误；需要“应用/取消”或自定义输入的页面保留专用 View 状态，最终提交仍调用 Provider，不能把校验逻辑塞回 `BasePanel`。
- 现有静态偏好类通过 `SettingsProvider` 兼容入口注册；新增实例型系统优先让管理器直接实现接口。Provider 不负责创建 Prefab，正式布局仍由专用 Launcher 和 Prefab 管理。
- 游戏内设置页签由 `SettingsActionListPagination` 的页面名、入口名、页签映射和首个焦点控件共同定义；新增分页时同步正式 `UI_ActionList` 嵌套 Prefab 与完整/定向构建入口。直接挂在子页 Prefab 的控制器会由分页器收集 `ISettingsPageLifecycle`，不必再向 `SettingCanvas` 添加专用初始化分支。
- 调整界面缩放范围或默认值时，以 `UIUserSettings` 常量为权威，同时检查 Provider/写入校验、`UIScaleController` 的实际应用下限，并同步 `UI_InterfaceSettings.prefab` 与 `RuntimeUIPrefabBuilder`；`PlayerPrefs` 默认参数只服务无旧键的新配置，不得覆盖已有玩家值。

## Prefab 与目录约束

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

- 检查 Prefab/节点/组件/事件、重复开关、输入锁、焦点边界、输入穿透、条目复用和本地化切换；最终布局再人工看。
- 默认静态诊断、编译和 Console；系统级变化运行 `UI.Smoke`。测试入口：`Assets/GameTest/UI/UISmokeTests.cs`；真实库存面板可用 Golden Path `ui.inventory-panel`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
