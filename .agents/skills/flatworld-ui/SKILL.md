---
name: flatworld-ui
description: "Use when: 定位或修改 FlatWorld 的 UIManager、BasePanel、主菜单、新游戏、存档列表、游戏内 UI、控件命名契约、动态 UI、UI 文案、多语言绑定、UI 音效或 UI Prefab。关键词：UIManager、BasePanel、GameManager.UI、SaveDataManager_UI、FlatWorldUI、LocalizedTextBinder。"
---

# FlatWorld UI 系统定位

> 最后核对：2026-08-09。修改 Prefab 位置或控件节点名后必须立即更新本 Skill。

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
- UI 反馈：`Assets/5_Scripts/5-5_UI/FlatWorldUIFeedback.cs`。
- 游戏内适配：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/`。
- 玩家世界坐标 HUD：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/{PlayerWorldCoordinateHUD,PlayerWorldCoordinateDisplayPreferences}.cs`；只为本地 `Player` 创建非交互常驻卡片，并持久化世界坐标/经纬度显示偏好。
- 保存状态 HUD：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/GameSaveStatusHUD.cs`；只实例化 `UI_SaveStatus` Prefab，显示异步保存状态，不拦截玩家输入。
- Buff 状态 HUD：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/{PlayerBuffStatusHUD,BuffStatusRowView}.cs`；只为本地 `Player` 读取 `BuffManager.ActiveBuffs`，实例化左侧中部非交互状态列表并显示剩余时间。
- 开发调试控制台：`Assets/5_Scripts/5-3_GamePlay/Development/Debug/GMReflectionConsole.cs` 及其 `Navigation`/`Buffs` partial；F4 GM 工具是既有的运行时调试 Canvas，属于正式 Prefab UI 规则之外的开发者专用例外。
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

## UI 文案与多语言

- 新建或修改任何面向玩家的 UI 文字（Prefab 标题、按钮、提示、状态、编辑器生成文字或运行时动态文字）时，必须调用 `flatworld-localization`，不能只把中文写进 Prefab 或脚本后结束。
- 正式 UI 文字统一进入 `FlatWorldUI` String Table；Prefab 静态文字由 `FlatWorldLocalizationSetup` 扫描，运行时动态文字必须在 `Assets/5_Scripts/5-2_Editor/Localization/FlatWorldLocalizationSetup.cs` 的 `EnglishUiOverrides` 中登记“中文源模板 → 英文表达”，并使用 `GetUiText`/`GetUiFormat` 查询。
- 新增文字必须保留中文 fallback，使用 `FlatWorldLocalizationService.GetUiTextKey(sourceText)` 生成稳定 key；不要用翻译后的英文、控件节点名或显示状态值作为 key。控件节点名仍是 UI 绑定契约，不随语言翻译。
- 完成文字或 Prefab 修改后执行 `FlatWorld/Localization/Setup Default Tables`，确认 `zh-CN` 与 `en` 两列都写入、动态模板占位符数量一致，并检查缺失英文翻译警告。
- 本地化只改变显示文本，不改变 UI 层级、尺寸、锚点、字体、颜色或布局；文字过长导致的溢出另行使用 `flatworld-ugui-layout` 处理。

## 正式运行时 Prefab

- 设置入口控制器：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/{AudioSettingsPanelLauncher,UISettingsPanelLauncher,CoordinateDisplaySettingsPanelLauncher,AutoSaveSettingsPanelLauncher,WorldStreamingSettingsPanelLauncher,DifficultySettingsPanelLauncher,InputBindingPanelLauncher,SettingsActionListPagination}.cs`。
- 设置面板：`UI_AudioSettings`、`UI_InterfaceSettings`、`UI_CoordinateDisplaySettings`、`UI_AutoSaveSettings`、`UI_WorldStreamingSettings`、`UI_DifficultySettings`、`UI_InputBindingSettings`。
- 按键绑定面板固定节点：`设备分页`、`键鼠分页按钮`、`手柄分页按钮`、`绑定列表/Content`、`恢复默认按钮`、`完成按钮`；动态 `UI_InputBindingRow` 固定包含 `操作名称`、`绑定值`、`修改按钮`、`清除按钮`，分页只重建行 Prefab 实例，不得运行时创建行内部视觉节点。
- 设置入口按钮预制在 `Assets/2_Prefabs/2-1_UI/Menu_UI/Info_Button_List.prefab`：使用 `设置分页_界面`、`设置分页_世界`、`设置分页_会话` 三页及 `设置上一页按钮`、`设置下一页按钮`、`设置页码文本` 控制；“显示设置”位于界面页并打开独立坐标显示设置窗口。
- 世界加载面板：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_WorldLoading.prefab`；根 Canvas 使用最高层 Overlay 并跨场景保留，固定节点为 `加载标题`、`加载状态`、`加载进度`、`加载进度文本`、`加载提示`。
- 保存状态 HUD：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_SaveStatus.prefab`；右上角锚点（`-32,-118`，`260×52`），固定节点为 `背景`、`强调线`、`保存状态文本`，CanvasGroup 默认隐藏且不拦截输入。
- 玩家坐标 HUD：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_PlayerWorldCoordinate.prefab`；根节点固定左上锚点（`32,-32`，`296×72`），契约节点为 `背景`、`强调线`、`坐标标题`、`坐标文本`。它不使用 `BasePanel`，由 `PlayerWorldCoordinateHUD` 仅在本地 Player 下实例化到 `PanelRoot` 最低子层级，并且所有 Graphic 都必须关闭 `raycastTarget`；有限循环世界按边界映射经度/纬度，无限世界按当前星球半径提供本地地理参考。
- Buff 状态 HUD：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_BuffStatus.prefab` 与 `UI_BuffStatusItem.prefab`；根节点锚定屏幕左侧中部（`32,0`，`320×360`），固定节点为 `标题`、`数量文本`、`内容列表/Viewport/Content`、`空状态文本`，条目固定包含 `占位图标`、`占位符文本`、`状态名称`、`剩余时间`。所有 Graphic 必须关闭 `raycastTarget`，由 `PlayerBuffStatusHUD` 挂到 `Player.prefab` 并保持在对话气泡之后、模态面板之前。
- 对话 UI：`Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_PlayerChatInput.prefab` 是底部半透明 Minecraft 风格单行输入条；`UI_CharacterSpeechBubble.prefab` 是角色头顶气泡。聊天控件固定节点为 `Text Area`、`Placeholder`、`Text`。
- `GameManager.UI.cs` 只实例化和更新加载 Prefab；禁止用 `new GameObject` 或 `AddComponent` 在运行时构建加载视觉。新建和进入存档时由 `GameManager.cs` 驱动阶段文字与进度。
- 统一重建器：`Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.cs`；全量菜单为 `FlatWorld/UI/Rebuild Runtime Prefab UI`，仅重建流送设置使用 `FlatWorld/UI/Rebuild World Streaming Settings UI`，按键绑定行使用 `FlatWorld/UI/Rebuild Input Binding UI`，坐标 HUD 使用 `FlatWorld/UI/Rebuild Player World Coordinate HUD`，保存状态 HUD 使用 `FlatWorld/UI/Rebuild Save Status HUD`，Buff 状态 HUD 使用 `FlatWorld/UI/Rebuild Buff Status HUD`，显示设置与列表分页使用 `FlatWorld/UI/Rebuild Coordinate Display Settings UI`；定向入口避免无关 Prefab 重写。
- `Assets/2_Prefabs` 是 Addressables 文件夹条目并带 `Prefab` 标签，其下新增运行时面板会由 `GameRes` 按 Prefab 名预加载。

## 主菜单与存档

- 主菜单/新游戏/存档 Prefab 引用字段位于 `GameManager.UI.cs`。
- 存档磁盘读写在 `SaveDataMgr.cs`，存档列表显示在 `SaveDataManager_UI.cs`。
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

- 2026-08-09：新增 `UI_BuffStatus` 左侧中部非交互 Buff 状态 HUD；从本地玩家 `BuffManager` 读取活动 Buff，使用 `UI_BuffStatusItem` 占位图标显示名称和剩余时间，并保持对话气泡与模态面板层级契约。
- 2026-08-09：区分常驻 HUD 与模态手柄面板；常驻模块菜单不再触发游戏内右摇杆准星退出，背包/设置等模态面板仍可接管 UI 焦点。
- 2026-08-09：新增 `UI_SaveStatus` 右上角非交互保存状态 HUD；`GameManager.SaveGame()` 改用分帧快照与后台原子写盘，保存期间提示“正在保存…”，完成后自动隐藏。
- 2026-08-09：槽位 UI 新增独立的手柄主要操作契约；虚拟光标确认直接进入手柄交换路径，鼠标 PointerDown 保持键鼠交换，不再共用一次点击状态。
- 2026-08-09：游戏内 `SettingCanvas` 打开时，单机且处于游戏世界会暂停并保存原 `Time.timeScale`；关闭或销毁时恢复，联机设置不修改全局时间流速。
- 2026-08-09：手柄 B 默认改为 UI 返回，不再触发背包开关；键盘 B 仍可开关背包，库存面板的手柄取消关闭保持可用。
- 2026-08-09：手柄焦点到 TMP 输入框时不再自动弹出虚拟键盘，只有再次按 A/Submit 才打开；键盘 Enter 保持普通键盘输入路径。
- 2026-08-09：游戏内 `Info_Button_List` 从 9 项单列拆为界面/世界/会话三分页；新增 `UI_CoordinateDisplaySettings` 和“显示设置”入口，可立即持久化左上角 HUD 的世界坐标或经纬度显示，有限循环世界按边界投影经纬度。
- 2026-08-09：按键绑定行新增“清除按钮”；`InputBindingService` 使用空覆盖路径禁用单个键鼠/手柄绑定并立即保存，`UISmokeTests` 与输入回归测试覆盖节点和清除契约。
- 2026-08-09：`UI_HotBar` 常驻快捷栏从手柄焦点导航和最上层焦点恢复链中排除，避免左摇杆移动时改变快捷栏槽位。
- 2026-08-09：修复键盘 B 背包关闭被右上角常驻菜单抢先消费：`FlatWorldUI/Cancel` 不绑定键盘 B，`Info_Button_List` 保留手柄导航但退出全局取消栈；手柄 B 另由 UI Cancel 统一返回。
## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/UI/UISmokeTests.cs` 与 `Assets/GameTest/UI/WorldTopologyUISmokeTests.cs`；当前覆盖 UIManager、BasePanel 手柄导航/取消契约、手柄 B 统一返回且不抢键盘 B、运行时 UI 导航不绑定键盘移动键、存档动态条目的焦点/选择态和自动导航、输入框手柄焦点与虚拟键盘确认触发、按键绑定双分页节点及行内修改/清除按钮、游戏内设置单机暂停契约、Resources UIRoot、八个设置 Prefab（含坐标显示和主菜单设置）、主菜单设置入口、设置列表三分页、流送性能入口、世界加载 Prefab、保存状态 HUD 与手动异步保存契约、玩家坐标 HUD 的节点/左上锚点/输入穿透/Player 绑定、Buff 状态 HUD 的节点/左侧中部锚点/滚动内容/输入穿透/Player 绑定、新世界难度命名契约，以及可选世界种子输入框的命名与卡片边界；`Assets/GameTest/PlayerInteraction/InputBindingServiceTests.cs` 覆盖单项清除绑定的空路径与持久化；联机 Prefab 与 GameRes 加载约束由 `NetworkingSmokeTests.cs` 覆盖。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；UI 测试约定目录：`Assets/GameTest/UI/`；场景目录：`Assets/GameTest/Scenes/UI/`；冒烟分类：`UI.Smoke`。
- 新增面板、按钮、输入框、动态 UI、存档列表或 UI 音效行为时必须增加系统测试；修复 Bug 时先增加回归测试。面板打开、交互和关闭主流程变化时同步更新 UI 冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；必须验证控件命名契约、组件类型、事件绑定和重复打开关闭，视觉观感仍交由人工确认。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category UI.Smoke`；无需视觉模型或测试工具卡片。涉及核心流程、玩家输入、存档、联机或音频时追加对应分类；只有布局、配色或最终视觉观感变化才做定向截图。
- UI 核心取消路由由 `Assets/GameTest/UI/UISmokeTests.cs`（`UI.Smoke`）覆盖；聊天与气泡的详细行为不再属于精简 Smoke 集合。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

任何 UI Prefab 移动、重命名、删除，控件节点名变化，PanelKey 变化，动态 UI 文件拆分，`PanelRoot` 规则或领域控制器绑定变化后，必须在同一任务内更新本 Skill 的路径、命名契约和近期变更；涉及具体系统时也更新该系统 Skill。

## 新世界拓扑控件（2026-08-06）

- `UI_NewGame.prefab` 与 `NewGamePrefabBuilder` 都必须包含 `GameManager.NewGameTopologyToggleKey`（“有限循环世界”）Toggle，默认开启。
- Toggle 关闭时半径输入框必须禁用，并提交 Infinite；开启时提交 Wrapped。控件继续由 `BasePanel.PrepareForGamepadNavigation` 纳入焦点链。
- Prefab、默认值和绑定契约由 `WorldTopologyUISmokeTests`（`UI.Smoke`）保护。
