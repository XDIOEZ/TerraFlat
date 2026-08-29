---
name: flatworld-ui
description: "Use when: 定位或修改 FlatWorld 的 UIManager、BasePanel、主菜单、新游戏、存档列表、游戏内 UI、控件命名、动态 UI、UI 文案、多语言、音效或 UI Prefab。关键词：UIManager、BasePanel、GameManager.UI、SaveDataManager_UI、LocalizedTextBinder。"
---

# FlatWorld UI

## 入口

- 生命周期：`Assets/5_Scripts/5-5_UI/Core/{UIManager,BasePanel}.cs`
- 通用控件/表现：`Assets/5_Scripts/5-5_UI/Common/{Controls,Presentation}/`；输入：`Assets/5_Scripts/5-5_UI/Input/`
- 主菜单：`Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameManager.UI.cs`；存档 UI：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/SaveDataManager_UI.cs`
- 游戏内 UI：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/`
- Prefab：`Assets/2_Prefabs/2-1_UI/`；根：`Assets/Resources/UI/UIRoot.prefab`
- 运行时键/构建器：`Assets/5_Scripts/5-5_UI/Core/RuntimeUIPrefabKeys.cs`、`Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.cs`

## 架构与运行时约束

- 领域控制器创建/持有正式 Prefab，`UIManager` 管生命周期；控件节点名是绑定契约。正式 UI 不用 `new GameObject/AddComponent` 拼视觉。
- Prefab 是视觉真相；`BasePanel` 不在初始化时重写结构。运行时只用稳定键加载正式 Prefab。
- `GameRes` 的启动资源加载面板属于引导 UI：可以登记 Addressables，但运行时必须由 `WorldManager.prefab` 直接引用，不能依赖尚未初始化的资源字典。
- 同一 `PanelRoot` 下的面板置顶/置底必须使用 `SetAsLastSibling`/`SetAsFirstSibling`；全局层级序号只能用于独立 Canvas 的 `sortingOrder`，不能直接当作兄弟索引。
- 不经过 `UIManager`/`BasePanel` 打开流程的独立 Canvas，Prefab 根节点必须显式固化 `localScale = Vector3.one`；不能依赖面板动画在运行时恢复可见缩放。
- 槽位内的选中框、背景和装饰必须按槽内兄弟顺序分层；选中框切换时必须跟随当前槽位，不得留在旧槽位后再用世界坐标跨槽移动。
- 设置类模态页（主设置及其子页）需要独立高层 Canvas 与 `GraphicRaycaster`；非交互对话气泡在玩法模态打开时隐藏，避免首帧或跨 Canvas 绘制顺序造成遮挡。
- 常驻 HUD 不拦截输入，Graphic 关闭 raycastTarget；若 HUD 提供展开/收起功能，只允许开关按钮接收 raycast，内容和装饰元素仍必须输入透明；模态面板才获取输入锁和顶层手柄焦点，关闭/失败路径释放。
- 手机 HUD 的菜单/返回入口必须独立于可隐藏的玩法控制层；模态玩法面板打开时保留该入口并允许背包/制作等面板并行打开，Android 返回键或 Escape 优先关闭最上层可取消面板，避免移动端失去退出路径。
- 主菜单属于不可直接关闭的根面板；Android 返回键、Escape 或手柄取消应通过 `BasePanel.CancelShortcutOverride` 打开正式退出确认 Prefab，只有确认按钮退出应用，取消或再次返回只关闭确认层。
- 坐标、角色状态等信息型 HUD 使用屏幕角落锚点和透明容器，只显示会随运行时变化的字段/状态条；禁止为这类 HUD 添加整块背景、卡片标题或装饰性介绍文字。
- 常驻组件事件驱动；禁止等待绑定或比较静态状态的 Update/LateUpdate 和逐帧 `GetComponent*`。
- 动态列表复用条目；结构变化才局部 MarkLayoutForRebuild，数值/颜色更新不强制布局。热路径禁止 ForceUpdateCanvases/ForceRebuild。
- 动态列表的通用节点名（如 `Content`）不得在整个面板全局查找；必须从所属 `ScrollRect` 或业务容器取引用，避免与 Dropdown 模板等同名节点串容器。
- EventSystem 反馈保持唯一非缩放 Tween，重入先 Kill，失活/销毁清理。
- 手机准线是 `UI_MobileControls.prefab` 的非交互 Graphic，由 `PlayerMobileControlsHUD` 按统一屏幕指针定位；不得让准线 Graphic 参与射线或手柄焦点。
- 旧缓存 Prefab 缺少手机准线节点时允许由 HUD 做一次性兼容补齐，不能把该兜底扩展成运行时拼装整套手机 UI。
- GM 调试面板由 `GMReflectionConsole` 运行时动态构建，不通过正式 UI Prefab；可持久化的调试开关统一放入 `GMConsolePreferences`，按钮状态需在场景切换和面板刷新时同步。
- 主菜单控件名集中在 `GameManager.UI.cs`；定向构建 Prefab，避免无关重写。
- `SafeAreaRoot` 只约束交互内容；挂在其下的全屏背景使用 `FullScreenRectController` 反向扩展到根 Canvas，背景图用 `AspectRatioFitter.EnvelopeParent` 等比裁切。`CanvasScaler` 不再乘安全区比例，避免与 `SafeAreaRectController` 双重缩小 UI。

## 设置 Provider 契约

- 通用契约位于 `Assets/5_Scripts/5-1_Data/Settings/SettingsContracts.cs`，由 `Data` 程序集提供；它只描述设置元数据和读写能力，不引用 `UnityEngine.UI`、TMP 或具体 Prefab。
- 功能管理器要出现在设置中时实现 `ISettingsProvider`，按需提供 `ISettingsToggleProvider`、`ISettingsSliderProvider`、`ISettingsDropdownProvider`、`ISettingsSwitchProvider`；四类控件分别对应开关、滑动条、下拉列表和按钮式互斥切换。
- `ProviderId` 与设置 `Key` 必须稳定且按功能命名；运行时在管理器自身生命周期中通过 `SettingsProviderRegistry.Register/Unregister` 注册，UI 通过 Provider 和 Key 查找，不直接调用管理器的业务字段或 `AudioBus` 等实现细节。
- `ISettingsDropdown`/`ISettingsSwitch` 的选项使用稳定 `SettingOption.Id`，写入通过 `TrySetSelectedIndex` 返回错误；需要“应用/取消”或自定义输入的页面保留专用 View 状态，最终提交仍调用 Provider，不能把校验逻辑塞回 `BasePanel`。
- 现有静态偏好类通过 `SettingsProvider` 兼容入口注册；新增实例型系统优先让管理器直接实现接口。Provider 不负责创建 Prefab，正式布局仍由专用 Launcher 和 Prefab 管理。

## Prefab 与目录约束

- 创建 UI Prefab 时必须按用途放入 `Assets/2_Prefabs/2-1_UI/` 下合适的分类目录；优先复用 `Common`、`Gameplay`、`MainMenu`、`Settings`，不要把 Prefab 直接堆在 UI 根目录。现有分类都不匹配时，才新增职责明确的子目录。
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
