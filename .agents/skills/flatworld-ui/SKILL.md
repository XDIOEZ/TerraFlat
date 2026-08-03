---
name: flatworld-ui
description: "Use when: 定位或修改 FlatWorld 的 UIManager、BasePanel、主菜单、新游戏、存档列表、游戏内 UI、控件命名契约、动态 UI、UI 音效或 UI Prefab。关键词：UIManager、BasePanel、GameManager.UI、SaveDataManager_UI。"
argument-hint: "UI 面板、控件或 Prefab 问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld UI 系统定位

> 最后核对：2026-07-29。修改 Prefab 位置或控件节点名后必须立即更新本 Skill。

## 修改前先读

1. `Assets/5_Scripts/5-5_UI/UIManager.cs`：面板根节点、创建、注册、查询、显示和销毁。
2. `Assets/5_Scripts/5-5_UI/BasePanel.cs`：密封通用面板组件、控件收集、开关和拖拽；不得在初始化时修改视觉结构。
3. `Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.UI.cs`：主菜单、新游戏、存档面板及控件命名契约。
4. `Assets/5_Scripts/5-3_GamePlay/Manager/SaveDataManager_UI.cs`：存档动态列表与玩家按钮。

## 关键脚本

- 存档条目：`Assets/5_Scripts/5-5_UI/GameSaveItemView.cs`。
- 通用旧基类：`Assets/5_Scripts/5-5_UI/BaseUIManager.cs`。
- 旧视觉主题工具：`Assets/5_Scripts/5-5_UI/FlatWorldUITheme.cs`；正式运行时面板不再调用，Prefab 是视觉真相。
- UI 反馈：`Assets/5_Scripts/5-5_UI/FlatWorldUIFeedback.cs`。
- 游戏内适配：`Assets/5_Scripts/5-3_GamePlay/UI/`。
- UI 音频绑定：`Assets/5_Scripts/5-5_UI/Audio/`。
- UI Prefab 根目录：`Assets/2_Prefabs/2-1_UI/`。
- PanelRoot Prefab：`Assets/Resources/UI/UIRoot.prefab`；`UIManager` 必须从该 Prefab 实例化，禁止运行时拼装 Canvas。
- 正式运行时 UI：`Assets/2_Prefabs/2-1_UI/Runtime/{Settings,Dialogue,System}/`。
- Prefab 查询键：`Assets/5_Scripts/5-5_UI/RuntimeUIPrefabKeys.cs`。

## 当前架构

```text
领域控制器（GameManager、Inventory、NetworkModeUIController 等）
→ 直接持有/创建 BasePanel
→ 按 GameObject 节点名查询 Button/Input/Text/Toggle/Slider
→ UIManager 注册、查找和管理生命周期
```

- `BasePanel` 是 `sealed`，不要再建立领域 View 继承层或代理层。
- 模态面板调用 `BasePanel.PrepareForGamepadNavigation()` 后会补齐 Automatic Navigation、打开时选择首个/指定控件、关闭时恢复父面板焦点，并可通过 `ICancelHandler` 接收手柄 B 返回；根主菜单必须关闭取消退出。
- 需要锁定世界玩法的面板通过 `BasePanel.Opened/Closed` 让领域控制器持有和释放 `GameController` 输入锁，`BasePanel` 不直接依赖玩家系统。
- 面板控制器依赖节点名作为键；重命名 Prefab 节点必须同步修改对应 `*Key` 常量。
- UIManager 优先复用场景中的 `PanelRoot`，否则实例化 `Assets/Resources/UI/UIRoot.prefab`；缺失时直接报错，不得回退到运行时创建 Canvas。
- `BasePanel`/`BaseUIManager` 只允许运行时收集控件和补充非视觉音频反馈，不得调用主题系统新增装饰、改颜色或改布局。
- Prefab 移动后同时检查场景 Inspector 引用、Addressables/Resources 引用和本 Skill 路径。

## 正式运行时 Prefab

- 设置入口控制器：`Assets/5_Scripts/5-3_GamePlay/UI/{AudioSettingsPanelLauncher,UISettingsPanelLauncher,AutoSaveSettingsPanelLauncher,DifficultySettingsPanelLauncher,InputBindingPanelLauncher}.cs`。
- 设置面板：`UI_AudioSettings`、`UI_InterfaceSettings`、`UI_AutoSaveSettings`、`UI_DifficultySettings`、`UI_InputBindingSettings`。
- 按键绑定面板固定节点：`设备分页`、`键鼠分页按钮`、`手柄分页按钮`、`绑定列表/Content`、`恢复默认按钮`、`完成按钮`；分页只重建 `UI_InputBindingRow` 实例，不得运行时创建行内部视觉节点。
- 设置入口按钮预制在 `Assets/2_Prefabs/2-1_UI/Menu_UI/Info_Button_List.prefab`：`音量调节`、`UI设置`、`自动保存`、`游戏难度`、`按键绑定`。
- 世界加载面板：`Assets/2_Prefabs/2-1_UI/Runtime/System/UI_WorldLoading.prefab`；根 Canvas 使用最高层 Overlay 并跨场景保留，固定节点为 `加载标题`、`加载状态`、`加载进度`、`加载进度文本`、`加载提示`。
- 对话 UI：`Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_PlayerChatInput.prefab` 是底部半透明 Minecraft 风格单行输入条；`UI_CharacterSpeechBubble.prefab` 是角色头顶气泡。聊天控件固定节点为 `Text Area`、`Placeholder`、`Text`。
- `GameManager.UI.cs` 只实例化和更新加载 Prefab；禁止用 `new GameObject` 或 `AddComponent` 在运行时构建加载视觉。新建和进入存档时由 `GameManager.cs` 驱动阶段文字与进度。
- 统一重建器：`Assets/Editor/FlatWorld/RuntimeUIPrefabBuilder.cs`，菜单 `FlatWorld/UI/Rebuild Runtime Prefab UI`；运行时资产修改后通过该入口重建并在 Prefab Mode 中人工检查。
- `Assets/2_Prefabs` 是 Addressables 文件夹条目并带 `Prefab` 标签，其下新增运行时面板会由 `GameRes` 按 Prefab 名预加载。

## 主菜单与存档

- 主菜单/新游戏/存档 Prefab 引用字段位于 `GameManager.UI.cs`。
- 存档磁盘读写在 `SaveDataMgr.cs`，存档列表显示在 `SaveDataManager_UI.cs`。
- 主菜单控件名常量统一位于 `GameManager.UI.cs`，不要散落魔法字符串。
- 新世界难度入口位于 `UI_NewGame.prefab` 底部；弹层包含官方预设/自定义主分页，自定义页再分为 `自定义分类页_战斗`、`自定义分类页_生存`、`自定义分类页_世界`、`自定义分类页_生产`。当前共 16 个 `难度_*倍率` Slider 与 `死亡掉落全部物品` Toggle，全部由 `GameManager.UI.cs` 的公开命名常量绑定；百分比文本统一命名为 `{SliderKey}_数值`。
- 官方预设按钮由 `GameDifficultyCatalog.All` 驱动，统一命名为 `官方难度预设_{GameDifficultyId}`；新增官方难度后重建 Prefab 即可自动生成列表项。
- 新世界 UI 的权威重建入口为 `Assets/Editor/FlatWorld/NewGamePrefabBuilder.cs`（菜单 `FlatWorld/UI/Rebuild New Game UI`）；修改难度布局后必须通过该入口重建 Prefab。

## 联机动态 UI

- 会话逻辑：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModeUIController.cs`。
- UI 状态绑定：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModeUIController.UI.cs`。
- Prefab 加载：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModePanelView.cs`，文件中声明的是 `NetworkModeUIController` partial，不存在独立 `NetworkModePanelView` 类型；运行时不再构建 UI。
- 联机面板：`Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NetworkMode.prefab`，由 `GameRes` 通过 `Prefab` Addressables 标签预加载后实例化。
- 编辑器重建器：`Assets/Editor/FlatWorld/NetworkModePrefabBuilder.cs`，菜单 `FlatWorld/UI/Rebuild Network Mode UI`。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-07-31：`BasePanel` 增加通用手柄导航；`UI_InputBindingSettings` 固化键鼠/手柄分页，运行时按设备筛选重绑行、切换焦点并恢复当前分页默认值。
- 2026-07-29：玩家聊天输入支持裸 T 打开、Enter 发送、Esc 取消；打开期间锁定玩法输入，提交文字由现有角色气泡显示。
- 2026-07-29：新增 `UI_WorldLoading.prefab`；新建世界和进入已有存档时以跨场景 Overlay 显示阶段、进度和提示，直至玩家及首批周围区块就绪。
- 2026-07-29：设置、按键绑定动态行、聊天输入、角色气泡、背包整理、制作预览和联机玩家名称全部固化为可视化 Prefab；新增统一重建器与 `RuntimeUIPrefabKeys`，运行时只加载、实例化和绑定数据。
- 2026-07-29：`UIRoot.prefab` 移至 `Assets/Resources/UI/`；`UIManager` 删除运行时 Canvas 构建兜底，`BasePanel`/`BaseUIManager` 停止应用会修改视觉的运行时主题。
- 2026-07-29：联机面板固化为可在 Unity 中直接查看和编辑的 `UI_NetworkMode.prefab`；运行时只通过 `GameRes` 实例化，移除全部视觉节点构建代码。
- 2026-07-29：自定义难度扩展为战斗、生存、世界、生产四个分类页，提供 16 个倍率滑条与死亡掉落开关；详情卡实时汇总四类规则。
- 2026-07-29：联机动态面板的地址输入支持直接粘贴 `域名:端口`、`kcp://` 或 `udp://` 穿透端点，并明确提示必须使用 UDP 隧道；控件节点名保持不变。
- 2026-07-29：新世界面板增加“难度设置”入口、官方预设/自定义分页、规则详情预览与死亡掉落自定义开关；官方预设为目录驱动的可滚动列表，扩充 `GameDifficultyCatalog.All` 后重建 Prefab 即可生成入口。
- 2026-07-27：领域 UI 改为直接组合密封 `BasePanel`；`GameManager` 与联机控制器使用 partial 分离业务和 UI。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/UI/UISmokeTests.cs`；当前覆盖 UIManager、BasePanel 手柄导航/取消契约、按键绑定双分页节点、Resources UIRoot、五个设置 Prefab、按键行 Prefab、世界加载 Prefab、设置入口节点、正式脚本无运行时视觉构建，以及新世界难度命名契约；联机 Prefab 与 GameRes 加载约束由 `NetworkingSmokeTests.cs` 覆盖。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；UI 测试约定目录：`Assets/GameTest/UI/`；场景目录：`Assets/GameTest/Scenes/UI/`；冒烟分类：`UI.Smoke`。
- 新增面板、按钮、输入框、动态 UI、存档列表或 UI 音效行为时必须增加系统测试；修复 Bug 时先增加回归测试。面板打开、交互和关闭主流程变化时同步更新 UI 冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；必须验证控件命名契约、组件类型、事件绑定和重复打开关闭，视觉观感仍交由人工确认。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category UI.Smoke`；无需视觉模型或测试工具卡片。涉及核心流程、玩家输入、存档、联机或音频时追加对应分类；只有布局、配色或最终视觉观感变化才做定向截图。
- 玩家聊天 UI 行为由 `Assets/GameTest/Dialogue/PlayerChatSmokeTests.cs` 覆盖；`DialogueSmokeTests.RuntimeDialogueUIUsesInspectablePrefabs` 保护聊天框/气泡 Prefab 节点契约和“运行时不构造视觉树”约束。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

任何 UI Prefab 移动、重命名、删除，控件节点名变化，PanelKey 变化，动态 UI 文件拆分，`PanelRoot` 规则或领域控制器绑定变化后，必须在同一任务内更新本 Skill 的路径、命名契约和近期变更；涉及具体系统时也更新该系统 Skill。
