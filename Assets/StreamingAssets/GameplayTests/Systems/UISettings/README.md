# UI与设置

2026-10-17 卡顿修复新增 `new_game_deferred_content` 与 `save_file_catalog` 规则，尚未编译或执行。新世界使用独立停用面板和内存控件模板，调用正式 `NewGameDeferredContent`，检查首次访问前无内容、世界与难度分别首次创建、重复访问保留实例与控件状态、动态内容注册到 `BasePanel`、内容归属及随面板销毁；不注册到用户 UIManager、不操作现场。正式 `UI_NewGame` 保留名称页和空容器，世界与难度子树分为两个显式引用的正式 Prefab，构建器同步。此规则不覆盖正式 Prefab 导入、未访问世界页直接生成世界的完整请求、区块默认偏好回填、真实键鼠/手柄切页、首次访问难度的耗时或最终画面，这些仍由用户验收。

存档目录规则向正式 `SaveSelectionFileCatalog.Load` 注入只读内存元数据，覆盖空目录、新旧时间与缺失时间混合、时间相同的名称排序；检查每条路径只采样一次，再按时间倒序和不区分大小写的名称排序。此处不触碰磁盘档案或用户 UI，不代表真实大目录分帧预算或取消/重开流程已验收。

2026-10-15：当时登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。2026-10-17 新增的下拉布局、设置会话规则尚未编译或执行，不属于该历史通过范围；本轮只做静态修改，保留用户当前 PlayMode，最终画面验收由用户进行。

快速入口：`rules.lua`；操作：`ui_settings/mobile_size、navigation_policy、panel_lifecycle、held_item_world_drop、held_item_world_drop_position、dropdown_layout、edit_session_values、edit_session_commit、edit_session_participants、edit_session_errors、input_binding_session`。

已编写的规则覆盖：正式 UIUserSettings 控件倍率净化；停用独立 Selectable 的手柄焦点策略（设置滑块、显示条、滚动条、快捷栏）；停用独立 BasePanel 的关闭初始化、重复开关事件幂等、视觉透明度和交互状态。临时排序计数在结束时恢复，不注册测试面板到 UIManager。

窗口外手持丢弃规则使用无 Canvas 的自有 BasePanel，调用正式能力设置、Open/Close、组件禁用/节点停用和销毁接口，再复用 UIManager 同一份面板资格聚合器采样。19 条资格规则覆盖无面板、允许/拒绝、非模态、叠加拒绝层、关闭/重开、禁用/停用、撤销能力、变更或销毁锁持有者、共享持有者、空/未知持有者拒绝与重复授权；仅 PlayMode 且已有 UIManager 时可额外采样重复授权的修订号不变，不创建单例。另有 10 条屏幕边界规则使用真实 UIWindowDragSurface.Configure 声明小窗口，检查全屏面板根内的窗口外位置仍可丢弃、窗口内部透明区/边缘内外、缺失拖动面时回退面板矩形、WorldSpace Canvas 自有停用相机及 NaN/Infinity 拒绝。夹具不注册到用户 UI，不绑定输入、不操作玩家或 PlayerPrefs，结束销毁自有对象并恢复排序计数。这些断言验证面板聚合、锁持有者资格与屏幕几何，不代表真实触摸轻点/长按、丢弃数量守恒、窗口实际射线遮挡、真实 controller 硬锁/未知锁或 HUD 自动刷新已覆盖；独立 controller 的初始化和销毁会修改全局输入状态，因此此规则不创建它。新增规则本轮未编译、未运行，实际 UI 射线与手势由用户验收。

下拉布局规则使用独立 Canvas、无输入模块的临时 EventSystem、真实 TMP/Legacy Dropdown 与正式 `DropdownListLayout`。夹具只补齐原生 Start 初始化和编辑态同步清理，每次展开前显式调用正式 `PrepareForOpen`，再调用原生 Show 生成所有选项；采样 1、3、5、6、10 项的实际行高、视口高度、Content 高度、活动选项数、首尾行完整可见性和滚动到末项，并在同一控件上按 10→1→6→3→5 改选项后重复展开。Lua 持有五行上限与边界断言；夹具不复制生产高度算法，不注册到 UIManager，不修改 PlayerPrefs，结束时销毁自己的列表、Blocker、Canvas 和事件系统，保留原界面选择。此规则验证控件几何与正式滚动接口，不验证 PlayMode 的自动 OnEnable 挂钩；该挂钩、多分辨率、触屏拖动、滚轮手感和 Game View 画面仍待用户后续验收。

设置会话规则使用正式 `SettingsProviderRegistry` 和只存内存的独立 Provider，覆盖四类控件未改、已改、改后改回、混合修改、重复关闭、保存建立新基线、带变化检测的补充状态、无检测能力时保守恢复，以及字段读取、恢复写入、补充状态检测失败后的错误收集与后续字段/Provider 继续恢复。采样返回实际恢复次数和 `restoreAttempted`，Lua 持有应跳过/应恢复的判断。若已有活动设置会话则拒绝运行，不覆盖当前基线；结束只关闭自建会话并注销自有 Provider，不写 PlayerPrefs，也不操作用户当前窗口。此处验证事务和多余工作是否被跳过，不测实际关闭帧耗时或 Game View 手感。

按键会话规则使用独立 `PlayerInputActions`、正式 `InputBindingService` 与内存覆盖存储，覆盖默认绑定/已有覆盖基线下的未改、修改、改后改回、保存后关闭与重复关闭；采样真实 JSON、变化通知次数和存储调用次数，确认无变化关闭跳过重载/广播、放弃修改不写入存储。夹具不挂 EventSystem、不启用动作、不读写玩家偏好；实际硬件重绑期间取消、运行时 UI 动作同步和关闭帧耗时仍未覆盖。

PlayMode 入口：`gameplay-suite.json` / `gameplay.lua`，与本地化系统共用同一个 `ui_localization_gameplay` 案例；不重复登记第二份脚本。

真实流程覆盖：在已经正式入世的隔离世界中，调用 `GameManager.OpenMainMenuSettings` 创建正式客户端设置 Prefab，通过现有语言下拉、音量滑块和保存按钮检查中英文正式表/TMP Binder 刷新、节点身份、缺项回退、非法 Locale 拒绝、设置保存基线与关闭放弃、重复 Open 幂等、面板注册、射线交互、可取消导航和 UIManager 玩法阻断；还检查窗口打开时 AI 头顶调试标签主动避让。截图保留已经切到英语的实际窗口，随后显式关闭、注销，并恢复原音量/语言及涉及的 PlayerPrefs 原值和原有无；Lua 失败、中止也经租约清理。

语言切换等待正式 LanguageChanged 与原 TMP 文本在 8 秒内就绪，记录耗时和事件，不手动刷新 Binder；实例 ID 同时确认文字节点和 Binder 没有被替换。

关键流程待补：游戏内设置模块的专用输入锁和单机暂停、未保存修改的确认弹窗分支、销毁跨帧后的全部事件订阅、SafeArea/多分辨率/移动布局、真实手柄顶层焦点输入、CJK 缺字和翻译后排版。客户端设置窗口处于真实世界中，但不等于游戏内设置模块所有流程已覆盖。规则入口不改当前 UI 或 PlayerPrefs；本 PlayMode 用例会短暂修改并恢复音量与语言偏好。

历史通过证据只覆盖当时已经登记并执行的断言；2026-10-17 新增下拉布局、设置会话断言本轮未编译、未运行。窗口画面、听感和最终验收由用户执行，不能据此推断覆盖缺口也已通过。
