---
name: flatworld-ui
description: "FlatWorld uGUI 面板、Prefab/公共控件、主题、设置 Provider 与输入焦点。"
---

# FlatWorld UI

## 入口与资源

- `Assets/5_Scripts/5-5_UI/`：`Core/{UIManager,BasePanel,RuntimeUIPrefabKeys}.cs`，`Common/{Controls,Presentation,Animation}/`、`Input/`。
- `Assets/5_Scripts/5-3_GamePlay/`：主菜单 `Core/Lifecycle/GameManager.UI*.cs`、业务视图 `Presentation/UI/`、存档 `SaveDataManager_UI.cs`。
- Prefab：`Assets/2_Prefabs/2-1_UI/{Common,Gameplay,MainMenu,Settings}/`；根 `Assets/Resources/UI/UIRoot.prefab`；构建器 `Assets/Editor/FlatWorld/PrefabBuilders/UI/`。
- 主题 `Assets/Resources/GameConfig/UI/ui-theme.json`；动画 `Assets/Resources/Config/UIAnimations.json`；设置契约 `Assets/5_Scripts/5-1_Data/Settings/SettingsContracts.cs`。

## 所有权与外观

- 正式 Prefab 持完整视觉/引用，领域控制器持业务/实例，UIManager 管生命周期；节点名与 RuntimeUIPrefabKeys 是绑定身份，文本用 FlatWorldUI 表的 GetUiText/GetUiFormat，不翻译节点名。资产需在 Addressables Prefab 标签内，子目录继承 UI.asmdef，移动保 meta/GUID。
- 主菜单为视觉基准：灰阶 Image、近白字、低对比边界、少量暖黄焦点；根→内容/按钮逐级提亮，边框约2参考像素、文字描边约1。图标/语义色独立于 Chrome，信息 HUD 为角落锚点/透明容器。静态文案只留字段、操作、真实状态与必要规则。
- `FlatWorldUIThemeConfigCatalog → FlatWorldUITheme → BasePanel/构建器` 共用主题。`ReusableUIControl` 嵌套 Prefab 持内部结构/字体/Sprite/几何，使用处持文案/事件/值/选项/外部布局，页签用 `ReusableUITabVisual.SetSelected`。构建器先停用有 Awake 的根，引用齐后激活；主题/控件迁移仅显式菜单，补建不覆盖人工编辑。
- 普通面板底板用 `UI_PanelBackground`，根 Image 只留透明射线；HUD/加载幕/主菜单独立外观，确认/改名阻断层透明。根 CanvasGroup.alpha=1、独立 Canvas.localScale=1，注册前 `InitClosed()`；启动加载面板由 WorldManager.prefab 直引，不依赖未就绪资源字典。

## 生命周期与输入

- `CreatePanelFromGameObject` 挂当前 `SceneUI/SafeAreaRoot`，场景卸载销毁；玩家 HUD/库存/设置/死亡页显式用 `CreatePersistentPanelFromGameObject` 挂常驻根。共享库存按所属对象分流，弹窗/虚拟键盘用 `GetPanelRootFor` 继承归属；全屏层只提排序，不改归属。
- 同根用兄弟顺序置顶，sortingOrder 只管独立 Canvas；模态持玩法锁/顶层手柄焦点，关闭/失败均释放。HUD 输入透明，收展 HUD 仅按钮接射线。
- 背包/制作/机器等普通窗口统一 `BasePanel.PrepareForGameplayWindow`，可并行、参加导航/取消、允许窗外手持丢弃、不持玩法锁。按键经 `Mod_GameController.CanToggleGameplayWindow`，服从硬锁/模态；背景透明，仅槽位/按钮/手势区命中，`UIWindowDragSurface` 命中标题/窄边框。快捷栏随根排序，手机模态拖放临时升独立 Canvas。
- `SetHeldItemWorldDropAllowed` 声明窗外丢弃，只有专用锁面板传自身 Owner。UIManager 汇总所有阻断窗口/兼容锁与真实矩形（优先 WindowRect）；未知锁/未授权窗口继续拦截，窗口内透明空白不能成为世界落点。
- `FlatWorldUI/Cancel` 是唯一返回入口，玩法经 `EventSystemGuard.CancelRequested`，不另订阅 ESC/Win10；`BasePanel.OnCancel` 检查同帧消费。手机返回顺序：顶层可取消面板→抽屉→设置；主菜单/设置通过 CancelShortcutOverride 保留退出/未保存确认，危险确认先消费取消。鼠标按下焦点不等于业务选中，完整点击才提交；手柄焦点限定顶层，TMP确认后开虚拟键盘。
- 手柄停用只解绑动作、保留最新输入资产，OnEnable 恢复返回/确认；停用期间 Configure 替换/撤销延后生效，不能复用已销毁资产。
- 面板可先于模块销毁，按 Unity 存活判断仍释放锁/转移引用。库存/快捷栏/手部调用 `Inventory.ReleaseRuntimePanel` 注销并销毁；制作/设置在 OnUnload 同样清理。`UIManager.DestroyPanel` 在延迟 Destroy 前即停用；场景卸载清失效注册/交互缓存，资源重载清两类根下运行 UI、保留根再重建。

## 按需参考

只读取当前任务涉及的参考，不批量加载；业务表现读权威状态，不延迟或代替事务。

| 任务 | 参考 |
| --- | --- |
| 安全区/缩放、列表/虚拟化、动画、加载/睡眠幕、HUD定位 | [布局与动画](references/layout-animation.md) |
| Provider、保存/放弃/默认值、画质/帧率、新世界设置 | [设置会话](references/settings.md) |
| 库存/提示、机器/装备/飞船/状态HUD、水容器 | [业务视图](references/business-views.md) |
