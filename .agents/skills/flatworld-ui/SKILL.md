---
name: flatworld-ui
description: "FlatWorld uGUI 面板、Prefab/公共控件、主题、设置 Provider 与输入焦点。"
---

# FlatWorld UI

## 入口

- 框架 `Assets/5_Scripts/5-5_UI/{Core,Common,Input}/`：UIManager、BasePanel、RuntimeUIPrefabKeys；业务 `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/`，主菜单 `Core/Lifecycle/GameManager.UI*.cs`，存档 `SaveDataManager_UI.cs`。
- Prefab `Assets/2_Prefabs/2-1_UI/{Common,Gameplay,MainMenu,Settings}/`；根 `Assets/Resources/UI/UIRoot.prefab`；构建器 `Assets/Editor/FlatWorld/PrefabBuilders/UI/`。
- 构建器主文件只留入口；`RuntimeUIPrefabBuilder` 的设置页/设置导航、HUD、手机、加载、库存、对话分别维护，`GameUIPrefabRebuilder` 按库存、槽位快捷栏、制作、状态菜单和窗口外观拆分。字体加载、矩形定位、描边、无数组分配的深度优先节点查询共用 `FlatWorldEditorUiLayout`，新增 partial 用静态导入复用，不再复制。
- 主题 `Assets/Resources/GameConfig/UI/ui-theme.json`；动画 `Assets/Resources/Config/UIAnimations.json`；设置契约 `Assets/5_Scripts/5-1_Data/Settings/SettingsContracts.cs`。

## 公共控件与布局

- **普通按钮/页签（含 GM、调试窗口）实例化 `Common/Controls/UI_Button` / `UI_TabButton`，不临时拼 Image/Button/TMP。** `ReusableUIControl` 持结构、字体、Sprite；使用处只设文案、事件、值、外部布局，页签选中走 `ReusableUITabVisual.SetSelected`。
- **公共 Prefab 不限制父布局。** 固定导航设 min/preferredHeight、flexibleHeight=0，父组控制高度并关闭纵向扩张；正文独立 flexibleHeight=1。验收同时限制行高、按钮上限和正文剩余空间，不能只查未越界；尺寸见[布局参考](references/layout-animation.md)。
- Prefab 持视觉/引用，领域控制器持业务，UIManager 管生命周期。节点名/RuntimeUIPrefabKeys 是身份，不翻译；文案走 FlatWorldUI 表 GetUiText/GetUiFormat。资产标 Addressables Prefab，移动保 meta/GUID，UI 子目录继承 UI.asmdef。
- 主菜单灰阶为基准，根→内容/按钮逐级提亮；近白字、低对比边框约2像素、字描边约1，图标/语义色独立。主题走 `FlatWorldUIThemeConfigCatalog → FlatWorldUITheme → BasePanel/构建器`；文案只留字段、操作、状态和必要规则。
- 构建器先停有 Awake 的根，引用齐再激活；迁移仅显式菜单，补建保人工编辑。`FlatWorldUIFeedback` 保基础缩放，布局用 `SetBaseScale`，预览停反馈。
- 普通底板用 `UI_PanelBackground`，根 Image 透明；HUD角落锚点/透明容器，确认/改名遮罩透明。CanvasGroup.alpha=1、独立 Canvas.localScale=1，注册前 `InitClosed()`；启动加载页由 WorldManager.prefab 直引。
- 固定 TMP 用 `FlatWorldUITextLayout.Configure(text, 设计字号)` 并同步 Prefab：最大字号不放大，下限通常12～18（可保更低有效值），超长 Ellipsis。输入、自适应长文、手动分页保自身规则，不在 Update 遍历修字。

## 生命周期与输入

- `CreatePanelFromGameObject` 归当前 `SceneUI/SafeAreaRoot`；玩家HUD/库存/设置/死亡页显式用 `CreatePersistentPanelFromGameObject`。共享库存按对象分流，弹窗/键盘用 `GetPanelRootFor` 继承；全屏只提排序。
- 左上角 `PlayerWorldCoordinateHUD` 的 FPS 开关同时显示实际 FPS/TPS；共用0.5秒真实时间窗口，TPS 取统一固定步序号差，暂停后归零。
- 同根靠兄弟顺序置顶，sortingOrder 只管独立 Canvas；模态持玩法锁/顶层手柄焦点，关闭/失败释放，HUD仅按钮接射线。
- 普通窗口用 `BasePanel.PrepareForGameplayWindow`，并行、可导航/取消、可窗外丢弃、不持玩法锁；开关经 `Mod_GameController.CanToggleGameplayWindow`。背景透明，拖动仅标题/窄边框；快捷栏随根排序，手机模态拖放临时升 Canvas。
- `SetHeldItemWorldDropAllowed` 声明丢弃，专用锁才传自身 Owner。UIManager 聚合阻断窗口/锁和真实矩形（优先 WindowRect）；未知锁/未授权窗口拦截，窗内透明区也不可落物。
- 返回只走 `FlatWorldUI/Cancel → EventSystemGuard.CancelRequested`，`BasePanel.OnCancel` 防同帧重复；手机：顶层面板→抽屉→设置。主菜单/设置用 CancelShortcutOverride 保确认，危险确认先消费。完整点击才业务选中，手柄焦点限顶层，TMP确认再开虚拟键盘。
- 手柄停用只解绑，保最新输入资产；Configure 替换/撤销延后到 OnEnable 恢复，不复用已销毁资产。
- 虚拟库存可在面板关闭时绑定；查找父级 ScrollRect 必须包含停用层级，保留正式滚动回调，规则 `virtualized_inventory` 覆盖关闭绑定后滚动。
- 飞船高度尺按生存状态面板和手机摇杆的实际矩形避让，状态面板移动/缩放后重新布局；回归 `ship_flight_hud_reserved_rectangles` 的桌面、竖屏与移动宽面板。
- 面板先销毁也须释放锁/引用。库存/快捷栏/手部用 `Inventory.ReleaseRuntimePanel`，制作/设置在 OnUnload 清理；`DestroyPanel` 延迟销毁前先停用。卸载清失效注册/缓存，资源重载保两类根、清运行UI再重建。

## 按需参考

只读涉及主题；UI消费权威状态，不代替事务。

| 任务 | 参考 |
| --- | --- |
| 安全区/缩放、列表、动画、加载/睡眠幕、HUD | [布局与动画](references/layout-animation.md) |
| Provider、保存/放弃/默认、画质/帧率、新世界 | [设置会话](references/settings.md) |
| 库存/提示、机器/装备/飞船/状态HUD、水容器 | [业务视图](references/business-views.md) |
