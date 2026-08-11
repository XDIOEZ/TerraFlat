---
name: flatworld-ui
description: "Use when: 定位或修改 FlatWorld 的 UIManager、BasePanel、主菜单、新游戏、存档列表、游戏内 UI、控件命名契约、动态 UI、UI 文案、多语言绑定、UI 音效或 UI Prefab。关键词：UIManager、BasePanel、GameManager.UI、SaveDataManager_UI、FlatWorldUI、LocalizedTextBinder。"
---

# FlatWorld UI 系统定位

> 最后核对：2026-08-10。修改 Prefab 位置或控件节点名后必须立即更新本 Skill。

## 修改前先读

1. `Assets/5_Scripts/5-5_UI/UIManager.cs`：面板根节点、创建、注册、查询、显示和销毁。
2. `Assets/5_Scripts/5-5_UI/BasePanel.cs`：密封通用面板组件、控件收集、开关和拖拽；不得在初始化时修改视觉结构。
3. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.UI.cs`：主菜单、新游戏、存档面板及控件命名契约。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/SaveDataManager_UI.cs`：存档动态列表与玩家按钮。
5. 如果本次 UI 会新增面向玩家的文字，必须同时读取并调用 `flatworld-localization`；重点检查 `FlatWorldLocalizationService`、`FlatWorldLocalizationSetup`、`FlatWorldUIAutoLocalizer` 和 `LocalizedTextBinder`。

## 关键脚本

- 存档条目：`Assets/5_Scripts/5-5_UI/GameSaveItemView.cs`。
- 通用旧基类：`Assets/5_Scripts/5-5_UI/BaseUIManager.cs`。
- 旧视觉主题工具：`Assets/5_Scripts/5-5_UI/FlatWorldUITheme.cs`；正式运行时面板不再调用，Prefab 是视觉真相。
- UI 反馈：`Assets/5_Scripts/5-5_UI/FlatWorldUIFeedback.cs`；组件直接接收 EventSystem 事件，并用非缩放时间 DOTween 按需播放唯一缩放动画，静止时没有组件级 `Update`。
- UI 用户偏好：`Assets/5_Scripts/5-5_UI/UIUserSettings.cs`；`UIUserSettings` 缓存 PlayerPrefs 并广播 `Changed`，`UIScaleController` 按设置、尺寸和应用恢复事件刷新。
- 游戏内适配：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/`。
- 玩家世界坐标 HUD：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/{PlayerWorldCoordinateHUD,PlayerWorldCoordinateDisplayPreferences}.cs`；只为本地 `Player` 创建非交互常驻卡片，以 10Hz 刷新坐标，并通过缓存偏好与 `Changed` 事件切换世界坐标/经纬度显示。
- 保存状态 HUD：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/GameSaveStatusHUD.cs`；只实例化 `UI_SaveStatus` Prefab，显示异步保存状态，不拦截玩家输入。
- Buff 状态 HUD：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/{PlayerBuffStatusHUD,BuffStatusRowView}.cs`；只为本地 `Player` 读取 `BuffManager.ActiveBuffs`，由增删、显式时长变化和整秒倒计时事件刷新，不保留兜底轮询。
- 任务追踪 HUD：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/{PlayerQuestTrackerHUD,QuestTrackerRowView}.cs`；只为本地 `Player` 读取任务快照，由 `QuestManager.RuntimeReady/RuntimeRemoving` 与 `PlayerQuestRuntime.QuestChanged` 刷新，不持有可写进度记录。
- 开发调试控制台：`Assets/5_Scripts/5-3_GamePlay/Development/Debug/GMReflectionConsole.cs` 及其 `Navigation`/`Buffs`/`Quests` partial；F4 GM 工具是既有的运行时调试 Canvas，属于正式 Prefab UI 规则之外的开发者专用例外。任务分页动态枚举 `debugOnly` 定义，不复制正式任务逻辑。
- UI 音频绑定：`Assets/5_Scripts/5-5_UI/Audio/`。
- UI Prefab 根目录：`Assets/2_Prefabs/2-1_UI/`。
- PanelRoot Prefab：`Assets/Resources/UI/UIRoot.prefab`；`UIManager` 必须从该 Prefab 实例化，禁止运行时拼装 Canvas。
- 正式运行时 UI：`Assets/2_Prefabs/2-1_UI/Runtime/{Settings,Dialogue,System}/`。
- Prefab 查询键：`Assets/5_Scripts/5-5_UI/RuntimeUIPrefabKeys.cs`。
- 主菜单设置视觉 Prefab：`Assets/2_Prefabs/2-1_UI/Runtime/Settings/UI_MainMenuSettings.prefab`；包含“窗口大小”“显示模式”“画质预设”“特效质量”“游戏语言”占位下拉项，暂不绑定设置逻辑。

## 当前架构

```text
领域控制器（GameManager、Inventory、NetworkModeUIController 等）
→ 直接持有/创建 BasePanel
→ 按 GameObject 节点名查询 Button/Input/Text/Toggle/Slider
→ UIManager 注册、查找和管理生命周期
```

- `BasePanel` 是 `sealed`，不要再建立领域 View 继承层或代理层。
- 模态面板调用 `BasePanel.PrepareForGamepadNavigation()` 后会补齐 Automatic Navigation、打开时选择首个/指定控件、关闭时恢复父面板焦点，并可通过 `ICancelHandler` 接收手柄 B 返回；根主菜单必须关闭取消退出。
- 橙色焦点框由 `FlatWorldUIFeedback`、`GameSaveItemView` 和槽位 UI 显示；`FlatWorldUI/Navigate` 只绑定手柄，键鼠模式的 W/A/S/D 必须只控制玩家移动、不能改变 EventSystem 焦点。游戏内右摇杆准星以玩家为中心，只有模态 UI 才切换到自由虚拟光标。`FlatWorldUI/Cancel` 的键盘入口只能是 Esc，手柄 B/Start 负责返回，键盘 B 保留给背包开关。主菜单没有 Player 时由 `GamepadUIRuntimeController` 直接检测键鼠/手柄输入切换运行模式。
- 需要锁定世界玩法的面板通过 `BasePanel.Opened/Closed` 让领域控制器持有和释放 `GameController` 输入锁，`BasePanel` 不直接依赖玩家系统。
- 面板控制器依赖节点名作为键；重命名 Prefab 节点必须同步修改对应 `*Key` 常量。
- UIManager 优先复用场景中的 `PanelRoot`，否则实例化 `Assets/Resources/UI/UIRoot.prefab`；缺失时直接报错，不得回退到运行时创建 Canvas。
- 角色说话气泡是非交互提示，必须保持为 `PanelRoot` 的第一个子节点；背包、右键菜单和模态面板均应位于其上方。
- `Info_Button_List` 等右上角常驻模块菜单可调用 `PrepareForGamepadNavigation` 提供焦点，但必须传入 `closeOnCancel: false, closeOnEscape: false`，不得被 B/Esc 的临时面板关闭栈误关。
- `BasePanel`/`BaseUIManager` 只允许运行时收集控件和补充非视觉音频反馈，不得调用主题系统新增装饰、改颜色或改布局。
- Prefab 移动后同时检查场景 Inspector 引用、Addressables/Resources 引用和本 Skill 路径。

## 性能与事件驱动约束

- 常驻 HUD、设置应用器和通用 UI 反馈禁止为了等待绑定或比较静态状态保留 `Update`/`LateUpdate`；组件引用只在 `Awake`、绑定、资格变化或层级变化事件中解析，禁止逐帧 `GetComponent*`。
- 动态列表必须分离“结构变化”和“内容变化”：增删、排序或显隐变化才调用 `LayoutRebuilder.MarkLayoutForRebuild()` 标记所属局部 `RectTransform`；倒计时、数值、颜色等内容刷新不得强制重建布局。
- 高频或常驻路径禁止调用 `Canvas.ForceUpdateCanvases()` 与 `LayoutRebuilder.ForceRebuildLayoutImmediate()`。只有需要在同一调用栈立即读取最终尺寸的低频显式操作才可定向使用，并必须说明原因。
- `FlatWorldUIFeedback` 直接负责 EventSystem 输入；状态变化时 Kill 旧缩放 Tween 并通过 `DOScale(...).SetUpdate(true)` 重定向唯一动画，失活/销毁时必须 Kill。不得恢复手写 `Update`，也不要为禁用事件接收组件额外增加输入中继。
- `UIUserSettings` 的 PlayerPrefs 值只在首次访问和显式写入时处理，所有修改必须走 `Set*`/`ResetToDefaults()` 并广播 `Changed`；`UIScaleController` 订阅事件，并通过 `OnRectTransformDimensionsChange`、应用焦点/暂停恢复处理显示环境变化。
- `BasePanel` 使用一次 `GetComponentsInChildren<Component>(true, reusableList)` 建立按钮、文本、Selectable 等共享层级快照；同一结构版本内的查询、打开和导航准备不得再次扫描。直属子节点变化会自动标脏，深层动态列表完成增删后必须调用一次 `RefreshUIComponents()` 显式提交；本地化、音频、主题和通用反馈必须消费该缓存列表，不能各自重新扫描。
- `UIManager.RootCanvas` 是虚拟光标的权威 Canvas 缓存，面板开关、排序、显隐和结构变化通过 `InteractionSurfaceRevision` 广播；顶层手柄/模态面板查询必须按该修订号复用结果，缓存命中时禁止重新解析根 Canvas 或扫描 `BasePanel`。`GamepadUIRuntimeController` 只在光标位置、修订号或 Canvas 尺寸变化时重新 `RaycastAll`，静止时仅保留 0.2 秒安全校验。回归/Profiler 可读取 `BasePanel.HierarchySnapshotRebuildCount`、`CachedSelectableCount`、`UIManager.PanelQueryCacheRebuildCount`、`CanvasResolveCount` 和 `HoverRaycastCount`。
- `GamepadUISelectionFollower` 缓存所属 `ScrollRect`、Content、Viewport 和自身 RectTransform；焦点变化只覆盖该 ScrollRect 的待处理目标，通过一次 `Canvas.willRenderCanvases` 回调在帧末合并。同一容器每帧最多局部 `ForceRebuildLayoutImmediate(content)` 一次，禁止恢复 `Canvas.ForceUpdateCanvases()` 或组件级 `Update/LateUpdate`。
- 按键绑定、存档和角色动态条目必须保留组件引用并复用历史实例；切页或刷新只更新数据、局部布局标记与前后两个选择态，只有历史容量不足时才实例化并提交一次 `BasePanel` 层级快照。可通过 `InputBindingPanelLauncher.RetainedRowCount` 与 `SaveDataManager_UI.RetainedEntryCount` 检查容量是否稳定。
- `UIDragResizer` 只能在 `IPointerEnterHandler`、`IPointerMoveHandler` 与拖拽事件中计算边缘，不得恢复空闲 `Update`；纯定位标记组件（如 `UI_Content`）不得声明空的生命周期方法。

## UI 文案与多语言

- 新建或修改任何面向玩家的 UI 文字（Prefab 标题、按钮、提示、状态、编辑器生成文字或运行时动态文字）时，必须调用 `flatworld-localization`，不能只把中文写进 Prefab 或脚本后结束。
- 正式 UI 文字统一进入 `FlatWorldUI` String Table；Prefab 静态文字由 `FlatWorldLocalizationSetup` 扫描，运行时动态文字必须在 `Assets/5_Scripts/5-2_Editor/Localization/FlatWorldLocalizationSetup.cs` 的 `EnglishUiOverrides` 中登记“中文源模板 → 英文表达”，并使用 `GetUiText`/`GetUiFormat` 查询。
- 新增文字必须保留中文 fallback，使用 `FlatWorldLocalizationService.GetUiTextKey(sourceText)` 生成稳定 key；不要用翻译后的英文、控件节点名或显示状态值作为 key。控件节点名仍是 UI 绑定契约，不随语言翻译。
- 完成文字或 Prefab 修改后执行 `FlatWorld/Localization/Setup Default Tables`，确认 `zh-CN` 与 `en` 两列都写入、动态模板占位符数量一致，并检查缺失英文翻译警告。
- 本地化只改变显示文本，不改变 UI 层级、尺寸、锚点、字体、颜色或布局；文字过长导致的溢出另行使用 `flatworld-ugui-layout` 处理。

## 正式运行时 Prefab

- 设置入口控制器：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/{AudioSettingsPanelLauncher,UISettingsPanelLauncher,CoordinateDisplaySettingsPanelLauncher,AutoSaveSettingsPanelLauncher,WorldStreamingSettingsPanelLauncher,DifficultySettingsPanelLauncher,InputBindingPanelLauncher,SettingsActionListPagination}.cs`。
- 设置面板：`UI_AudioSettings`、`UI_InterfaceSettings`、`UI_CoordinateDisplaySettings`、`UI_AutoSaveSettings`、`UI_WorldStreamingSettings`、`UI_DifficultySettings`、`UI_InputBindingSettings`。
- 按键绑定面板固定节点：`设备分页`、`键鼠分页按钮`、`手柄分页按钮`、`绑定列表/Content`、`恢复默认按钮`、`完成按钮`；动态 `UI_InputBindingRow` 固定包含 `操作名称`、`绑定值`、`修改按钮`、`清除按钮`，分页必须复用行池并只更新数据，不得销毁重建实例或运行时创建行内部视觉节点。
- 设置入口按钮预制在 `Assets/2_Prefabs/2-1_UI/Menu_UI/Info_Button_List.prefab`：使用 `设置分页_界面`、`设置分页_世界`、`设置分页_会话` 三页及 `设置上一页按钮`、`设置下一页按钮`、`设置页码文本` 控制；“显示设置”位于界面页并打开独立坐标显示设置窗口。
- 世界加载面板：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_WorldLoading.prefab`；根 Canvas 使用最高层 Overlay 并跨场景保留，固定节点为 `加载标题`、`加载状态`、`加载进度`、`加载进度文本`、`加载提示`。
- 保存状态 HUD：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_SaveStatus.prefab`；右上角锚点（`-32,-118`，`260×52`），固定节点为 `背景`、`强调线`、`保存状态文本`，CanvasGroup 默认隐藏且不拦截输入。
- 玩家坐标 HUD：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_PlayerWorldCoordinate.prefab`；根节点固定左上锚点（`32,-32`，`296×72`），契约节点为 `背景`、`强调线`、`坐标标题`、`坐标文本`。它不使用 `BasePanel`，由 `PlayerWorldCoordinateHUD` 仅在本地 Player 下实例化到 `PanelRoot` 最低子层级，并且所有 Graphic 都必须关闭 `raycastTarget`；有限循环世界按边界映射经度/纬度，无限世界按当前星球半径提供本地地理参考。
- Buff 状态 HUD：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_BuffStatus.prefab` 与 `UI_BuffStatusItem.prefab`；根节点锚定屏幕左侧中部（`32,0`，`320×360`），固定节点为 `标题`、`数量文本`、`内容列表/Viewport/Content`、`空状态文本`，条目固定包含 `占位图标`、`占位符文本`、`状态名称`、`剩余时间`。所有 Graphic 必须关闭 `raycastTarget`，由 `PlayerBuffStatusHUD` 挂到 `Player.prefab` 并保持在对话气泡之后、模态面板之前。
- 任务追踪 HUD：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_QuestTracker.prefab` 与 `UI_QuestTrackerItem.prefab`；根节点锚定屏幕右上（`-32,-190`，`380×420`），固定节点为 `标题`、`数量文本`、`内容列表/Viewport/Content`、`空状态文本`，条目固定包含 `状态线`、`任务标题`、`任务状态`、`任务说明`、`目标文本`、`进度背景/进度填充`。所有 Graphic 必须关闭 `raycastTarget`，最多显示并复用四条，由 `PlayerQuestTrackerHUD` 挂到 `Player.prefab`。
- 对话 UI：`Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_PlayerChatInput.prefab` 是底部半透明 Minecraft 风格单行输入条；`UI_CharacterSpeechBubble.prefab` 是角色头顶气泡。聊天控件固定节点为 `Text Area`、`Placeholder`、`Text`。
- `GameManager.UI.cs` 只实例化和更新加载 Prefab；禁止用 `new GameObject` 或 `AddComponent` 在运行时构建加载视觉。新建和进入存档时由 `GameManager.cs` 驱动阶段文字与进度。
- 统一重建器：`Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.cs`；全量菜单为 `FlatWorld/UI/Rebuild Runtime Prefab UI`，仅重建流送设置使用 `FlatWorld/UI/Rebuild World Streaming Settings UI`，按键绑定行使用 `FlatWorld/UI/Rebuild Input Binding UI`，坐标 HUD 使用 `FlatWorld/UI/Rebuild Player World Coordinate HUD`，保存状态 HUD 使用 `FlatWorld/UI/Rebuild Save Status HUD`，Buff 状态 HUD 使用 `FlatWorld/UI/Rebuild Buff Status HUD`，任务追踪使用 `FlatWorld/UI/Rebuild Quest Tracker HUD`，显示设置与列表分页使用 `FlatWorld/UI/Rebuild Coordinate Display Settings UI`；定向入口避免无关 Prefab 重写。
- `Assets/2_Prefabs` 是 Addressables 文件夹条目并带 `Prefab` 标签，其下新增运行时面板会由 `GameRes` 按 Prefab 名预加载。

## 主菜单与存档

- 主菜单/新游戏/存档 Prefab 引用字段位于 `GameManager.UI.cs`。
- 存档磁盘读写在 `SaveDataMgr.cs`，存档列表显示在 `SaveDataManager_UI.cs`。
- `SaveDataManager_UI` 的存档/角色条目共享复用池；刷新列表只更新差异选择态并标记两个局部 Content，禁止遍历整个层级清选择或销毁重建全部按钮。
- 主菜单控件名常量统一位于 `GameManager.UI.cs`，不要散落魔法字符串。
- 主菜单设置入口位于 `UI_Hello.prefab` 右上角，控件名为“设置”，对应 `GameManager.MainMenuSettingsButtonKey`；`GameManager.OpenMainMenuSettings()` 通过 `RuntimeUIPrefabKeys.MainMenuSettings` 加载并注册 `UI_MainMenuSettings`，绑定“关闭按钮”“返回按钮”，设置功能本身仍为占位。
- 新世界难度入口位于 `UI_NewGame.prefab` 底部；弹层包含官方预设/自定义主分页，自定义页再分为 `自定义分类页_战斗`、`自定义分类页_生存`、`自定义分类页_世界`、`自定义分类页_生产`。当前共 16 个 `难度_*倍率` Slider 与 `死亡掉落全部物品` Toggle，全部由 `GameManager.UI.cs` 的公开命名常量绑定；百分比文本统一命名为 `{SliderKey}_数值`。
- 官方预设按钮由 `GameDifficultyCatalog.All` 驱动，统一命名为 `官方难度预设_{GameDifficultyId}`；新增官方难度后重建 Prefab 即可自动生成列表项。
- 新世界 UI 的权威重建入口为 `Assets/Editor/FlatWorld/PrefabBuilders/UI/NewGamePrefabBuilder.cs`（菜单 `FlatWorld/UI/Rebuild New Game UI`）；修改难度布局后必须通过该入口重建 Prefab。
- 新世界种子输入框固定命名为 `GameManager.NewGameSeedInputKey`（“世界种子输入框”），位于“世界生成概览”卡片内；允许数字或文字，留空时由 `GameManager` 随机生成。

## 联机动态 UI

- 会话逻辑：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModeUIController.cs`。
- UI 状态绑定：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModeUIController.UI.cs`。
- Prefab 加载：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModePanelView.cs`，文件中声明的是 `NetworkModeUIController` partial，不存在独立 `NetworkModePanelView` 类型；运行时不再构建 UI。
- 联机面板：`Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NetworkMode.prefab`，由 `GameRes` 通过 `Prefab` Addressables 标签预加载后实例化。
- 编辑器重建器：`Assets/Editor/FlatWorld/PrefabBuilders/UI/NetworkModePrefabBuilder.cs`，菜单 `FlatWorld/UI/Rebuild Network Mode UI`。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-11：F4 GM 控制台新增独立“任务”分页；动态任务卡提供开启、批量开启、状态目标刷新与手动交付，并订阅 `QuestChanged` 更新状态，分页搜索和历史页索引契约保持兼容。
- 2026-08-11：新增右上角非交互任务追踪 HUD；从本地玩家任务快照显示标题、说明、状态、当前目标与进度条，最多复用四条，任务完成后自动移出，并通过任务运行时生命周期和 `QuestChanged` 完全事件驱动刷新。
- 2026-08-11：库存面板创建时把真正的模态库存统一归一为关闭态，保证首次 `Open` 会获取输入锁；快捷栏与 `Inventory_Hand` 明确排除在模态焦点/输入锁外。Golden Path `ui.inventory-panel` 现在断言背包打开时锁定、关闭时释放，并在失败清理中兜底释放。
- 2026-08-10：完成剩余 UI 热路径收敛：`UIManager` 按交互面修订号缓存顶层手柄/模态面板；按键绑定与存档/角色列表改为条目池和差异选择刷新；设置分页缓存首项并局部标记布局；拖拽缩放改为指针事件；坐标 HUD 仅本地玩家 10Hz 刷新并订阅偏好事件。
- 2026-08-10：清理 UI 常驻轮询与重复扫描：Buff HUD 改为事件驱动局部布局；通用按钮反馈改用非缩放时间 DOTween；UI 用户偏好改为静态缓存广播；`BasePanel` 改为共享层级快照；虚拟光标改为缓存与按需射线；滚动焦点跟随改为缓存父级、帧末按 ScrollRect 合并并仅重建目标 Content。
- 2026-08-10：GM 游戏事件卡的“强制触发”继续调用统一事件管理器，并由事件触发载荷区分 GM 强制环境绕过；面板按钮和状态协议不变。
- 2026-08-09：GM 开发者专用运行时 Canvas 增加分辨率变化检测；按页面实际视口宽度重算操作网格列数、卡片宽度与网格高度，避免固定四列在窄窗口中挤压控件。
- 2026-08-09：新增 `UI_BuffStatus` 左侧中部非交互 Buff 状态 HUD；从本地玩家 `BuffManager` 读取活动 Buff，使用 `UI_BuffStatusItem` 占位图标显示名称和剩余时间，并保持对话气泡与模态面板层级契约。
- 2026-08-09：区分常驻 HUD 与模态手柄面板；常驻模块菜单不再触发游戏内右摇杆准星退出，背包/设置等模态面板仍可接管 UI 焦点。
- 2026-08-09：新增 `UI_SaveStatus` 右上角非交互保存状态 HUD；`GameManager.SaveGame()` 改用分帧快照与后台原子写盘，保存期间提示“正在保存…”，完成后自动隐藏。

## 修改后验证

- 基础测试脚本：`Assets/GameTest/UI/UISmokeTests.cs` 与 `Assets/GameTest/UI/WorldTopologyUISmokeTests.cs`；当前覆盖 UIManager、BasePanel 手柄导航/取消契约与共享层级快照、虚拟光标根 Canvas/交互面修订号/按需射线契约、滚动焦点按 ScrollRect 合并/最后目标/局部布局契约、手柄 B 统一返回且不抢键盘 B、运行时 UI 导航不绑定键盘移动键、存档动态条目的焦点/选择态和自动导航、输入框手柄焦点与虚拟键盘确认触发、按键绑定双分页节点及行内修改/清除按钮/动态快照提交、游戏内设置单机暂停契约、Resources UIRoot、八个设置 Prefab（含坐标显示和主菜单设置）、主菜单设置入口、设置列表三分页、流送性能入口、世界加载 Prefab、保存状态 HUD 与手动异步保存契约、玩家坐标 HUD 的节点/左上锚点/输入穿透/Player 绑定、Buff 状态 HUD 的节点/左侧中部锚点/滚动内容/输入穿透/Player 绑定及事件驱动布局契约、任务追踪 HUD 的节点/右上锚点/条目进度条/输入穿透/Player 绑定及事件驱动契约、通用按钮反馈的 DOTween/无 Update/清理契约与 UI 设置缓存广播契约、新世界难度命名契约，以及可选世界种子输入框的命名与卡片边界；`Assets/GameTest/PlayerInteraction/InputBindingServiceTests.cs` 覆盖单项清除绑定的空路径与持久化；联机 Prefab 与 GameRes 加载约束由 `NetworkingSmokeTests.cs` 覆盖。
- Golden Path 自动化程序集显式引用 `UI`；真实单机面板生命周期由操作 `ui.inventory-panel` 覆盖：通过玩家背包公开入口创建、打开/关闭，并断言输入锁随面板获取与释放；失败清理会兜底关闭面板，不使用物理输入或查找按钮点击。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；UI 测试约定目录：`Assets/GameTest/UI/`；场景目录：`Assets/GameTest/Scenes/UI/`；冒烟分类：`UI.Smoke`。
- 新增面板、按钮、输入框、动态 UI、存档列表或 UI 音效行为时必须增加系统测试；修复 Bug 时先增加回归测试。面板打开、交互和关闭主流程变化时同步更新 UI 冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；必须验证控件命名契约、组件类型、事件绑定和重复打开关闭，视觉观感仍交由人工确认。
- 先按 `flatworld-test-automation` 的触发门槛判断：普通局部 UI 清理只做静态诊断、相关程序集编译和 Console 检查；达到系统级门槛或用户明确要求时，才执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category UI.Smoke`。涉及核心流程、玩家输入、存档、联机或音频时再追加对应分类。
- UI 核心取消路由由 `Assets/GameTest/UI/UISmokeTests.cs`（`UI.Smoke`）覆盖；聊天与气泡的详细行为不再属于精简 Smoke 集合。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

任何 UI Prefab 移动、重命名、删除，控件节点名变化，PanelKey 变化，动态 UI 文件拆分，`PanelRoot` 规则或领域控制器绑定变化后，必须在同一任务内更新本 Skill 的路径、命名契约和近期变更；涉及具体系统时也更新该系统 Skill。

## 新世界拓扑控件（2026-08-06）

- `UI_NewGame.prefab` 与 `NewGamePrefabBuilder` 都必须包含 `GameManager.NewGameTopologyToggleKey`（“有限循环世界”）Toggle，默认开启。
- Toggle 关闭时半径输入框必须禁用，并提交 Infinite；开启时提交 Wrapped。控件继续由 `BasePanel.PrepareForGamepadNavigation` 纳入焦点链。
- Prefab、默认值和绑定契约由 `WorldTopologyUISmokeTests`（`UI.Smoke`）保护。
