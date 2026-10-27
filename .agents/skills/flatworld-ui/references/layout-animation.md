# 布局与动画

## 缩放与控件

- GM 两级导航复用 `UI_TabButton` 并列入 GameRes.StartupPrefabKeys；二级行为42、按钮150×38逻辑像素，窄屏只压宽，导航 flexibleHeight=0、正文独占剩余高度。规则 `ui_settings/gm_secondary_pages` 同时检查尺寸上限、正文预算和公共控件身份。
- 启动缓存重建时 `GameRes.LoadStatusText` 追加首次较慢提示，加载幕/抢跑入世共用，缓存命中不提示；提示进入双语表。
- UIRoot 用 UIScaleController、1920×1080基准，UIUserSettings 为默认/范围权威；SafeAreaRoot 仅约束交互，FullScreenRectController 反向扩视觉背景，CanvasScaler 不乘安全区比例。安全区先验当前屏幕再归一化，绘制前补更新；SafeAreaScaleGroup 固定卡片只缩不放，之后才用 presentationScale。负快捷栏底距可保存，最终矩形仍避强制手势区。
- 主菜单模态共用 SharedModalCardSize；MainMenuResponsiveLayout 按原位置/安全区限幅，移动交互高≥60、正文≥17逻辑像素，增长内容进ScrollRect。设置不拖动；背包无保存位置时等首个可见动画布局结束，按真实快捷栏上沿定位一次，人工拖动后让位。
- 主菜单四个旅程入口采用 0.9 倍设计比例（原 0.6 放大50%）；MainMenuResponsiveLayout 与构建器/Prefab 共用此基准，按完整设计内容占屏预算对标题、卡片、设置按钮及边距统一限幅，优先保持 UIRoot/UIScaleController 的实时用户倍率，空间不足才等比缩小，不能按已缩放 Canvas 尺寸反向缩边距。
- 选择存档、新世界、联机、MOD 管理四个主菜单模态面板的卡片/投影（新世界另含难度弹窗）保留各自内部布局，RectTransform 基准比例统一为 0.9（原 0.6 放大50%）；SafeAreaScaleGroup 按真实基准尺寸限幅，全局用户倍率只由根 CanvasScaler 执行。构建器与正式 Prefab 同步，规则 `ui_settings/main_menu_modal_scale_layout` 检查基准尺寸和空间不足时的限幅。
- 列表 `ItemStepScrollRect` 按 scrollDeltaPerTick 还原刻度，再按条目/网格行滚动；长文本用 fixedWheelStep。显式迁移只换 MonoScript GUID，保 fileID/引用。
- `InventoryVirtualizedSlotGrid` 用数据索引/环形池，仅重绑新进入格；回调读取当前 slotIndex，同库存走 RebindVirtualSlotUI，搜索只改变化格，拖动时延迟重绑，重建池重建回调归属。
- `InventoryBagSearch.Bind` 在同一 `Inventory_Data` 扩缩槽重绑时保留滚动；切换数据或更改搜索/分类才回顶，未提交的筛选回顶请求继续保留，列表缩短时由虚拟网格夹紧到有效范围。
- `SaveSelectionFileCatalog` 每档案一次退出时间采样后内存排序；打开刷新、Start仅兜底，首行同步，其余每帧≤4行/约2ms，关闭停补建。完整身份列表不裁剪，批选/改名不依赖已建行；删除由 SaveDataManager_UI 确认层锁定目标。
- 新游戏由 `UI_NewGame`、`UI_NewGameWorldContent/UI_NewGameDifficultyContent` 三Prefab与 NewGamePrefabBuilder 共持；隐藏页按需建一次，创建请求直接读未开页默认值。世界参数分页仅改显隐，难度覆盖层透明，加载存档失败弹正式确认并停止角色选择。
- `DropdownListLayout` 展开前用真实行高，≤5项全显、更多最多5行；BasePanel/公共构建器同步接入。条目/Content归原生Dropdown，模板禁竞争LayoutGroup/ContentSizeFitter，Viewport无垂直内缩。交互滑块与只读进度条分资产；纯色无Sprite进度用纵缩放，不能Filled。

- 大气检测仪屏幕由正式贴图、可滚动气体行和 SafeAreaScaleGroup 组成；预览与规则按 1920×1080 Expand 和 UI 倍率换算画布，桌面与竖屏都检查。

## 动画与覆盖层

- `BasePanel → BaseUIAnimation → UIAnimationManager → JSON` 单向依赖，单面板≤1动画，Opened/Closed同步。ScaleUIAnimation/panel.scale 绕 MotionRoot可见中心，不争写布局/安全区；Duration/相对Offset/Scale/Ease存配置，全局倍率仅本系统根Tween，热重载保行程快照，反向保进度，结束/禁用复姿态。
- `InventoryQuickTransferAnimation` 消费已提交事务，顶层无射线Canvas、非缩放时间0.24秒、≤32并发；甩动接松手坐标、Shift从源槽出发，仅用真实可见且绑定未换代槽位，按世界角点换算父缩放；关闭/卸载取消，不锁输入或延迟事务。EventSystem反馈唯一非缩放Tween，动态Content从所属容器取。
- `UI_WorldLoading` 不透明黑幕+独立内容CanvasGroup：进世界先绘制一帧，表现就绪后内容→黑幕淡出→解锁；同场景重生单段淡出。`UI_Sleep` 纯黑Image扩满安全区外、无通用动画；Mod_Tent黑幕绘制一帧后手动保存，结束从当前Alpha只淡一次并完成交互；Prefab/BuildSleepHud同步。
- 世界状态条层高于Water，普通Transform下RectTransform用anchoredPosition。Mod_Stamina避真实快捷栏上沿，随后 HotbarItemNameHUD.PositionAboveHud 留12逻辑像素，不改公共UI_Slider；安全区/缩放/底距变化仍避让。ApplicationVersionLabel归UI根静态内容，热更保留，场景副本不重复。
- 日志窗Toggle查看/复制共用 GameLogManager.MatchesRuntimeLogFilter，Exception/Assert归错误，不改磁盘日志。
