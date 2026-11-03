# 设置会话

- 主菜单与游戏内共用 `SettingsActionListPagination` 的左侧竖向滚动分类栏、右侧设置内容区；画面只分「显示」「画质」两个相邻分类：显示容纳坐标、FPS/TPS 与帧率，画质将六档预设、后处理、水体、透视、阴影和描边合入同一可滚动 Prefab。界面、语言、镜头、音频、调试仍独立，世界/会话操作只在游戏内显示。Prefab 构建器调用 `EnsureSidebarLayout` 固化结构，运行时隐藏旧视觉特效侧栏入口。
- 所有设置分页统一 76 高标题、左右 20／上下 18 内边距、10 行距和左上对齐；按键绑定的模式行位于标题说明下方，绑定列表保留独立滚动和弹性高度。
- 单项浮点偏好共用 `PlayerPrefsFloatValueCommitter` 的缓存比较/持久化；校验、惰性初始化和保存后通知归各领域。画质/动画的重应用及批量重置保留专属流程，不改成逐项保存。
- Data契约只含元数据/读写能力；功能管理器实现 `ISettingsProvider` 与 Toggle/Slider/Dropdown/Switch能力并在自身生命周期注册。UI按稳定 ProviderId/Key 绑定，选项身份为 SettingOption.Id，写入返回校验结果；Provider不创建视觉。
- `SettingsEditSessionController` 持保存基线，非控件偏好用 `ISettingsEditSessionParticipant` 快照/还原，按键独立会话，分页器收集 `ISettingsPageLifecycle`。关闭只恢复偏离基线值，扩展状态用 `ISettingsEditSessionChangeTracker` 跳过无变化恢复（未实现者仍恢复）；仅可能写回时保存。按键放弃仅在覆盖改变时重载/通知，避免无改动关闭重建输入/画质/写盘。
- 主菜单设置底部的保存按钮固定节点名 `SettingsEditSessionController.SaveButtonName`（`保存设置`）、文案“保存”；正式 `UI_MainMenuSettings.prefab` 与 `ConfigureMainMenuSettingsActionList` 同步维护，并给保存、恢复默认和提示文案预留互不重叠的空间。
- 左侧分类可直接进入显示、画质、镜头和语言；不再保留「视觉特效」第三分页或画质跳转快捷入口。`SettingsActionListPagination` 保生命周期/Provider，仅手柄切页自动焦点；恢复不替鼠标/触屏选输入框。
- 甩动选项由 InputBindingPanelLauncher 绑定 InventoryFlickUserSettings（inventory_flick），速度/距离25～200%、步长1、默认100，方向默认开；固定首项`操作选项`不随动态按键行回收，复用保存/放弃/默认会话。
- 三模拟半径为设备级PlayerPrefs、整组严格递增，Item/AIECS共用，镜头独立。特效范围来自default-rendering.json，玩家值由Provider存；关太阳不关脚底阴影。
- `WaterVisualSettings` Dropdown稳定ID：stylized/realistic/unity-stylized-water/low-spec；`UI_VisualEffectsSettings` 作为画质页唯一滚动内容，原 `VisualEffectsSettingsPanelLauncher` 继续绑定视觉细项。`GraphicsSettingsPanelLauncher` 负责六档预设和后处理，两个控制器共用页底「恢复默认」按钮并各自重置所属 Provider；UI顺序不等于持久化枚举；只有显式选预设才联动水体/阴影/后处理，启动/放弃恢复各项，保留微调。重建入口 `Rebuild Graphics Settings UI` 同步游戏内和主菜单 Prefab。
- 画质页新增独立 `ScreenPostProcessSettings.StyleSettingKey` 下拉（clear/soft/cinematic），当前旧 Prefab 缺行时 `GraphicsSettingsPanelLauncher` 只在初始化复用正式“特效质量”整行，未来 Builder 生成直接使用固定节点；主菜单与游戏内共用保存/放弃/恢复默认会话。风格不被整体画质预设覆盖；画质仍仅控制后处理开销。
- `FrameRateUserSettings` 持帧率，默认60，预设/不限与1～1000整数输入均进会话；FrameRateSettingsPanelController只绑控件，切画质后重应用、移动启动不覆上限。手机不限用屏幕刷新率，避免-1降30帧。
- `GameplayTickRateUserSettings` 独立管理游戏模拟 TPS（30/60/120，默认30，联机强制30），仅改变自主游戏时钟与同频脚本物理，不改 Unity `Time.fixedDeltaTime`；随显示设置页下拉参与保存/放弃与恢复默认。FPS 上限独立，旧显示 Prefab 缺控件时 FrameRateSettingsPanelController 复用现有下拉行兼容，编辑器构建器生成正式控件。
- 主菜单/游戏内共用NewWorldUserSettings，每次打开回填、名称/世界页切换保输入；返回先难度层→名称页→窗口。难度用GameDifficultyCatalog的0～20级，摘要分战斗/生存/世界/生产。
- PlayerRestartButton仅保存后重启整游戏，清克隆事件、不支持平台禁用标注。保存退出确认左右固定“不保存直接退出”/“保存并退出”，Prefab/构建器/本地化同时维护。
