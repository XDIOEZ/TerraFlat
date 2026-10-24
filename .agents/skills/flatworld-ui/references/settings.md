# 设置会话

- Data契约只含元数据/读写能力；功能管理器实现 `ISettingsProvider` 与 Toggle/Slider/Dropdown/Switch能力并在自身生命周期注册。UI按稳定 ProviderId/Key 绑定，选项身份为 SettingOption.Id，写入返回校验结果；Provider不创建视觉。
- `SettingsEditSessionController` 持保存基线，非控件偏好用 `ISettingsEditSessionParticipant` 快照/还原，按键独立会话，分页器收集 `ISettingsPageLifecycle`。关闭只恢复偏离基线值，扩展状态用 `ISettingsEditSessionChangeTracker` 跳过无变化恢复（未实现者仍恢复）；仅可能写回时保存。按键放弃仅在覆盖改变时重载/通知，避免无改动关闭重建输入/画质/写盘。
- 主菜单设置底部的保存按钮固定节点名 `SettingsEditSessionController.SaveButtonName`（`保存设置`）、文案“保存”；正式 `UI_MainMenuSettings.prefab` 与 `ConfigureMainMenuSettingsActionList` 同步维护，并给保存、恢复默认和提示文案预留互不重叠的空间。
- 设置内入口：画质页开显示/视觉特效，绑定页开镜头，界面页开主菜单语言。`SettingsActionListPagination` 保生命周期/Provider，仅手柄切页自动焦点；恢复不替鼠标/触屏选输入框。
- 甩动选项由 InputBindingPanelLauncher 绑定 InventoryFlickUserSettings（inventory_flick），速度/距离25～200%、步长1、默认100，方向默认开；固定首项`操作选项`不随动态按键行回收，复用保存/放弃/默认会话。
- 三模拟半径为设备级PlayerPrefs、整组严格递增，Item/AIECS共用，镜头独立。特效范围来自default-rendering.json，玩家值由Provider存；关太阳不关脚底阴影。
- `WaterVisualSettings` Dropdown稳定ID：stylized/realistic/unity-stylized-water/low-spec；UI_VisualEffectsSettings与构建器同步公共列表，随Provider/语言更新。`GraphicsSettingsPanelLauncher` 主菜单/游戏内共用六档画质页，UI顺序不等于持久化枚举；只有显式选预设才联动水体/阴影/后处理，启动/放弃恢复各项，保留微调。
- `FrameRateUserSettings` 持帧率，默认60，预设/不限与1～1000整数输入均进会话；FrameRateSettingsPanelController只绑控件，切画质后重应用、移动启动不覆上限。手机不限用屏幕刷新率，避免-1降30帧。
- 主菜单/游戏内共用NewWorldUserSettings，每次打开回填、名称/世界页切换保输入；返回先难度层→名称页→窗口。难度用GameDifficultyCatalog的0～20级，摘要分战斗/生存/世界/生产。
- PlayerRestartButton仅保存后重启整游戏，清克隆事件、不支持平台禁用标注。保存退出确认左右固定“不保存直接退出”/“保存并退出”，Prefab/构建器/本地化同时维护。
