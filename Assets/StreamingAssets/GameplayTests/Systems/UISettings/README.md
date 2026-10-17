# UI与设置

2026-10-15：当时登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。2026-10-17 新增的下拉布局规则尚未编译或执行，不属于该历史通过范围；本轮只做静态修改，保留用户当前 PlayMode，最终画面验收由用户进行。

快速入口：`rules.lua`；操作：`ui_settings/mobile_size、navigation_policy、panel_lifecycle、dropdown_layout`。

已编写的规则覆盖：正式 UIUserSettings 控件倍率净化；停用独立 Selectable 的手柄焦点策略（设置滑块、显示条、滚动条、快捷栏）；停用独立 BasePanel 的关闭初始化、重复开关事件幂等、视觉透明度和交互状态。临时排序计数在结束时恢复，不注册测试面板到 UIManager。

下拉布局规则使用独立 Canvas、无输入模块的临时 EventSystem、真实 TMP/Legacy Dropdown 与正式 `DropdownListLayout`。夹具只补齐原生 Start 初始化和编辑态同步清理，每次展开前显式调用正式 `PrepareForOpen`，再调用原生 Show 生成所有选项；采样 1、3、5、6、10 项的实际行高、视口高度、Content 高度、活动选项数、首尾行完整可见性和滚动到末项，并在同一控件上按 10→1→6→3→5 改选项后重复展开。Lua 持有五行上限与边界断言；夹具不复制生产高度算法，不注册到 UIManager，不修改 PlayerPrefs，结束时销毁自己的列表、Blocker、Canvas 和事件系统，保留原界面选择。此规则验证控件几何与正式滚动接口，不验证 PlayMode 的自动 OnEnable 挂钩；该挂钩、多分辨率、触屏拖动、滚轮手感和 Game View 画面仍待用户后续验收。

PlayMode 入口：`gameplay-suite.json` / `gameplay.lua`，与本地化系统共用同一个 `ui_localization_gameplay` 案例；不重复登记第二份脚本。

真实流程覆盖：在已经正式入世的隔离世界中，调用 `GameManager.OpenMainMenuSettings` 创建正式客户端设置 Prefab，通过现有语言下拉、音量滑块和保存按钮检查中英文正式表/TMP Binder 刷新、节点身份、缺项回退、非法 Locale 拒绝、设置保存基线与关闭放弃、重复 Open 幂等、面板注册、射线交互、可取消导航和 UIManager 玩法阻断；还检查窗口打开时 AI 头顶调试标签主动避让。截图保留已经切到英语的实际窗口，随后显式关闭、注销，并恢复原音量/语言及涉及的 PlayerPrefs 原值和原有无；Lua 失败、中止也经租约清理。

语言切换等待正式 LanguageChanged 与原 TMP 文本在 8 秒内就绪，记录耗时和事件，不手动刷新 Binder；实例 ID 同时确认文字节点和 Binder 没有被替换。

关键流程待补：游戏内设置模块的专用输入锁和单机暂停、未保存修改的确认弹窗分支、销毁跨帧后的全部事件订阅、SafeArea/多分辨率/移动布局、真实手柄顶层焦点输入、CJK 缺字和翻译后排版。客户端设置窗口处于真实世界中，但不等于游戏内设置模块所有流程已覆盖。规则入口不改当前 UI 或 PlayerPrefs；本 PlayMode 用例会短暂修改并恢复音量与语言偏好。

历史通过证据只覆盖当时已经登记并执行的断言；2026-10-17 新增下拉布局断言本轮未编译、未运行。窗口画面、听感和最终验收由用户执行，不能据此推断覆盖缺口也已通过。
