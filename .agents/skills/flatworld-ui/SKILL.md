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

## 关键脚本
- 存档条目：`Assets/5_Scripts/5-5_UI/GameSaveItemView.cs`。
- 通用旧基类：`Assets/5_Scripts/5-5_UI/BaseUIManager.cs`。
- 旧视觉主题工具：`Assets/5_Scripts/5-5_UI/FlatWorldUITheme.cs`；正式运行时面板不再调用，Prefab 是视觉真相。
- UI 反馈：`Assets/5_Scripts/5-5_UI/FlatWorldUIFeedback.cs`；组件直接接收 EventSystem 事件，并用非缩放时间 DOTween 按需播放唯一缩放动画，静止时没有组件级 `Update`。
- UI 用户偏好：`Assets/5_Scripts/5-5_UI/UIUserSettings.cs`；`UIUserSettings` 缓存 PlayerPrefs 并广播 `Changed`，`UIScaleController` 按设置、尺寸和应用恢复事件刷新。
- 游戏内适配：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/`。

## 当前架构
```text
领域控制器（GameManager、Inventory、NetworkModeUIController 等）
→ 直接持有/创建 BasePanel
→ 按 GameObject 节点名查询 Button/Input/Text/Toggle/Slider
→ UIManager 注册、查找和管理生命周期
```

## 性能与事件驱动约束
- 常驻 HUD、设置应用器和通用 UI 反馈禁止为了等待绑定或比较静态状态保留 `Update`/`LateUpdate`；组件引用只在 `Awake`、绑定、资格变化或层级变化事件中解析，禁止逐帧 `GetComponent*`。
- 动态列表必须分离“结构变化”和“内容变化”：增删、排序或显隐变化才调用 `LayoutRebuilder.MarkLayoutForRebuild()` 标记所属局部 `RectTransform`；倒计时、数值、颜色等内容刷新不得强制重建布局。
- 高频或常驻路径禁止调用 `Canvas.ForceUpdateCanvases()` 与 `LayoutRebuilder.ForceRebuildLayoutImmediate()`。只有需要在同一调用栈立即读取最终尺寸的低频显式操作才可定向使用，并必须说明原因。
- `FlatWorldUIFeedback` 直接负责 EventSystem 输入；状态变化时 Kill 旧缩放 Tween 并通过 `DOScale(...).SetUpdate(true)` 重定向唯一动画，失活/销毁时必须 Kill。不得恢复手写 `Update`，也不要为禁用事件接收组件额外增加输入中继。

## UI 文案与多语言
- 新建或修改任何面向玩家的 UI 文字（Prefab 标题、按钮、提示、状态、编辑器生成文字或运行时动态文字）时，必须调用 `flatworld-localization`，不能只把中文写进 Prefab 或脚本后结束。
- 正式 UI 文字统一进入 `FlatWorldUI` String Table；Prefab 静态文字由 `FlatWorldLocalizationSetup` 扫描，运行时动态文字必须在 `Assets/5_Scripts/5-2_Editor/Localization/FlatWorldLocalizationSetup.cs` 的 `EnglishUiOverrides` 中登记“中文源模板 → 英文表达”，并使用 `GetUiText`/`GetUiFormat` 查询。
- 新增文字必须保留中文 fallback，使用 `FlatWorldLocalizationService.GetUiTextKey(sourceText)` 生成稳定 key；不要用翻译后的英文、控件节点名或显示状态值作为 key。控件节点名仍是 UI 绑定契约，不随语言翻译。
- 完成文字或 Prefab 修改后执行 `FlatWorld/Localization/Setup Default Tables`，确认 `zh-CN` 与 `en` 两列都写入、动态模板占位符数量一致，并检查缺失英文翻译警告。

## 正式运行时 Prefab
- 设置入口控制器：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/{AudioSettingsPanelLauncher,UISettingsPanelLauncher,CoordinateDisplaySettingsPanelLauncher,AutoSaveSettingsPanelLauncher,WorldStreamingSettingsPanelLauncher,DifficultySettingsPanelLauncher,InputBindingPanelLauncher,SettingsActionListPagination}.cs`。
- 设置面板：`UI_AudioSettings`、`UI_InterfaceSettings`、`UI_CoordinateDisplaySettings`、`UI_AutoSaveSettings`、`UI_WorldStreamingSettings`、`UI_DifficultySettings`、`UI_InputBindingSettings`。
- 按键绑定面板固定节点：`设备分页`、`键鼠分页按钮`、`手柄分页按钮`、`绑定列表/Content`、`恢复默认按钮`、`完成按钮`；动态 `UI_InputBindingRow` 固定包含 `操作名称`、`绑定值`、`修改按钮`、`清除按钮`，分页必须复用行池并只更新数据，不得销毁重建实例或运行时创建行内部视觉节点。
- 设置入口按钮预制在 `Assets/2_Prefabs/2-1_UI/Menu_UI/Info_Button_List.prefab`：使用 `设置分页_界面`、`设置分页_世界`、`设置分页_会话` 三页及 `设置上一页按钮`、`设置下一页按钮`、`设置页码文本` 控制；“显示设置”位于界面页并打开独立坐标显示设置窗口。

## 主菜单与存档
- 主菜单/新游戏/存档 Prefab 引用字段位于 `GameManager.UI.cs`。
- 存档磁盘读写在 `SaveDataMgr.cs`，存档列表显示在 `SaveDataManager_UI.cs`。
- `SaveDataManager_UI` 的存档/角色条目共享复用池；刷新列表只更新差异选择态并标记两个局部 Content，禁止遍历整个层级清选择或销毁重建全部按钮。
- 主菜单控件名常量统一位于 `GameManager.UI.cs`，不要散落魔法字符串。

## 联机动态 UI
- 会话逻辑：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModeUIController.cs`。
- UI 状态绑定：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModeUIController.UI.cs`。
- Prefab 加载：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModePanelView.cs`，文件中声明的是 `NetworkModeUIController` partial，不存在独立 `NetworkModePanelView` 类型；运行时不再构建 UI。
- 联机面板：`Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NetworkMode.prefab`，由 `GameRes` 通过 `Prefab` Addressables 标签预加载后实例化。

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：修正输入框手柄交互：TMP 输入框继续作为正常 Selectable 显示焦点框；`GamepadInputFieldNavigationBridge` 在虚拟键盘未打开时转发方向导航，按确认后才由虚拟键盘接管编辑，不再用 `DeactivateInputField` 破坏选中表现。
- 2026-08-12：手柄焦点增加顶层面板边界：`GamepadUIRuntimeController.LateUpdate` 在 EventSystem 导航后调用 `UIManager` 的缓存顶层面板校验，Automatic Navigation 越界到背景 UI 时立即恢复当前弹窗默认焦点。
- 2026-08-11：按键设置的键鼠与手柄分页都新增“切换奔跑/长按奔跑”两个独立绑定项；手柄左摇杆按下默认改为切换奔跑。
- 2026-08-11：修复手柄焦点进入 TMP 输入框后无法离开：`GamepadUIRuntimeController` 在未确认、虚拟键盘未打开时保留 EventSystem 选中框但关闭 TMP 文字编辑态，方向键/左摇杆继续参与自动导航。
- 2026-08-11：游戏内设置“会话”分页新增“不保存直接退出”按钮；`SettingCanvas` 绑定独立无保存退出入口，按钮继续沿用正式 Prefab、自动导航和本地化扫描链。

## 修改后验证
- 基础测试脚本：`Assets/GameTest/UI/UISmokeTests.cs` 与 `Assets/GameTest/UI/WorldTopologyUISmokeTests.cs`；当前覆盖 UIManager、BasePanel 手柄导航/取消契约与共享层级快照、虚拟光标根 Canvas/交互面修订号/按需射线契约、滚动焦点按 ScrollRect 合并/最后目标/局部布局契约、手柄 B 统一返回且不抢键盘 B、运行时 UI 导航不绑定键盘移动键、存档动态条目的焦点/选择态和自动导航、输入框手柄焦点与虚拟键盘确认触发、按键绑定双分页节点及行内修改/清除按钮/动态快照提交、游戏内设置单机暂停契约、Resources UIRoot、八个设置 Prefab（含坐标显示和主菜单设置）、主菜单设置入口、设置列表三分页、流送性能入口、世界加载 Prefab、保存状态 HUD 与手动异步保存契约、玩家坐标 HUD 的节点/左上锚点/输入穿透/Player 绑定、Buff 状态 HUD 的节点/左侧中部锚点/滚动内容/输入穿透/Player 绑定及事件驱动布局契约、通用按钮反馈的 DOTween/无 Update/清理契约与 UI 设置缓存广播契约、新世界难度命名契约，以及可选世界种子输入框的命名与卡片边界；`Assets/GameTest/PlayerInteraction/InputBindingServiceTests.cs` 覆盖单项清除绑定的空路径与持久化；联机 Prefab 与 GameRes 加载约束由 `NetworkingSmokeTests.cs` 覆盖。
- Golden Path 自动化程序集显式引用 `UI`；真实单机面板生命周期由操作 `ui.inventory-panel` 覆盖：通过玩家背包公开入口创建、打开/关闭，并断言输入锁随面板获取与释放；失败清理会兜底关闭面板，不使用物理输入或查找按钮点击。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；UI 测试约定目录：`Assets/GameTest/UI/`；场景目录：`Assets/GameTest/Scenes/UI/`；冒烟分类：`UI.Smoke`。
- 新增面板、按钮、输入框、动态 UI、存档列表或 UI 音效行为时必须增加系统测试；修复 Bug 时先增加回归测试。面板打开、交互和关闭主流程变化时同步更新 UI 冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；必须验证控件命名契约、组件类型、事件绑定和重复打开关闭，视觉观感仍交由人工确认。
- 先按 `flatworld-test-automation` 的触发门槛判断：普通局部 UI 清理只做静态诊断、相关程序集编译和 Console 检查；达到系统级门槛或用户明确要求时，才执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category UI.Smoke`。涉及核心流程、玩家输入、存档、联机或音频时再追加对应分类。

## 修改后维护本 Skill
任何 UI Prefab 移动、重命名、删除，控件节点名变化，PanelKey 变化，动态 UI 文件拆分，`PanelRoot` 规则或领域控制器绑定变化后，必须在同一任务内更新本 Skill 的路径、命名契约和近期变更；涉及具体系统时也更新该系统 Skill。

## 新世界拓扑控件（2026-08-06）
- `UI_NewGame.prefab` 与 `NewGamePrefabBuilder` 都必须包含 `GameManager.NewGameTopologyToggleKey`（“有限循环世界”）Toggle，默认开启。
- Toggle 关闭时半径输入框必须禁用，并提交 Infinite；开启时提交 Wrapped。控件继续由 `BasePanel.PrepareForGamepadNavigation` 纳入焦点链。
- Prefab、默认值和绑定契约由 `WorldTopologyUISmokeTests`（`UI.Smoke`）保护。
