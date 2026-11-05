# UI 资源索引

> 用途：为人和 AI 提供可搜索的 UI ID、用途别名与**实际资源路径**；不把资产清单塞进 `SKILL.md`。扫描快照：**2026-11-06**。

## 查找规则

- 本表路径均相对于项目根目录 `C:/Users/CatStudio/Desktop/TerraFlat-master/`，使用 `/` 分隔符。表中 `UI_...` 是 **Prefab 文件名**，并非所有运行时节点的 Hierarchy 路径。
- **先查这份索引**：按中文名称或 `UI_` 标识搜索。改视觉/布局查 Prefab 与编辑器构建器，改行为查脚本；节点名和 `RuntimeUIPrefabKeys` 不要当作翻译文案。
- 范围：项目自有 UI 目录内的 `.prefab`，加上独立的 `UIRoot.prefab` 与常见嵌入式页面。第三方包（如 Fungus/Mirror）的演示/插件界面不纳入。
- 该索引是文件资产清单，不意味着 Prefab 全部会在当前平台/场景实例化，也不穷举每个 Prefab 内的子按钮和动态生成的临时节点。

## 最常用 UI 快速入口

| 查找目标 | 对应资源 |
| --- | --- |
| UI 根 / 场景 Canvas | `Assets/Resources/UI/UIRoot.prefab` |
| 背包、库存、容器 | `Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Bag.prefab` |
| 装备界面 | `Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Equipment.prefab` |
| 游戏内设置主面板 | `Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_ActionList.prefab` |
| 主菜单设置总面板 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_MainMenuSettings.prefab` |
| 保存 / 角色选择 | `Assets/2_Prefabs/2-1_UI/MainMenu/Save/UI_SaveSelectionPanel.prefab` |
| 快捷栏 | `Assets/2_Prefabs/2-1_UI/Gameplay/HUD/UI_HotBar.prefab` |
| 画质视觉细项 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_VisualEffectsSettings.prefab` |
| 统一按钮 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_Button.prefab` |

## 所有 UI Prefab（按目录分组）

### 公共控件与模板（19）

| UI ID | 用途 / 搜索词 | Prefab 路径 |
| --- | --- | --- |
| `UI_Button` | 通用按钮 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_Button.prefab` |
| `UI_CloseButton` | 关闭按钮 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_CloseButton.prefab` |
| `UI_DestructiveButton` | 危险操作按钮 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_DestructiveButton.prefab` |
| `UI_Dropdown` | 下拉菜单 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_Dropdown.prefab` |
| `UI_InputField` | 文本输入框 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_InputField.prefab` |
| `UI_MultiSelectDropdown` | 多选下拉菜单 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_MultiSelectDropdown.prefab` |
| `UI_PanelBackground` | 通用面板底板 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_PanelBackground.prefab` |
| `UI_PrimaryButton` | 主操作按钮 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_PrimaryButton.prefab` |
| `UI_ProgressBar` | 进度条 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_ProgressBar.prefab` |
| `UI_SelectBox` | 选中框（快捷栏） | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_SelectBox.prefab` |
| `UI_Slider` | 滑动条 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_Slider.prefab` |
| `UI_SliderControl` | 组合滑动条控件 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_SliderControl.prefab` |
| `UI_Switch` | 切换开关 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_Switch.prefab` |
| `UI_SwitchOption` | 开关选项 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_SwitchOption.prefab` |
| `UI_TabButton` | 导航页签按钮 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_TabButton.prefab` |
| `UI_Toggle` | 勾选框 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_Toggle.prefab` |
| `UI_TransparentButton` | 透明命中按钮 | `Assets/2_Prefabs/2-1_UI/Common/Controls/UI_TransparentButton.prefab` |
| `UI_BasePanel` | 基础面板模板 | `Assets/2_Prefabs/2-1_UI/Common/Templates/UI_BasePanel.prefab` |
| `UI_BaseText` | 通用文本模板 | `Assets/2_Prefabs/2-1_UI/Common/Templates/UI_BaseText.prefab` |

### 游戏内 UI

| UI ID | 用途 / 搜索词 | Prefab 路径 |
| --- | --- | --- |
| `UI_FuelInteraction` | 燃料交互 | `Assets/2_Prefabs/2-1_UI/Gameplay/Containers/UI_FuelInteraction.prefab` |
| `UI_WaterVessel` | 水容器、液体容器 | `Assets/2_Prefabs/2-1_UI/Gameplay/Containers/UI_WaterVessel.prefab` |
| `UI_Anvil` | 砧、锻造 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Anvil.prefab` |
| `UI_Bonfire` | 篝火 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Bonfire.prefab` |
| `UI_CompostBin` | 堆肥箱 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_CompostBin.prefab` |
| `UI_Cultivator` | 耕地工具 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Cultivator.prefab` |
| `UI_FireDrill` | 钻木取火 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_FireDrill.prefab` |
| `UI_FishingRod` | 钓鱼竿 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_FishingRod.prefab` |
| `UI_FlintStrike` | 打火石 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_FlintStrike.prefab` |
| `UI_FluidTank` | 流体罐、气罐 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_FluidTank.prefab` |
| `UI_Furnace` | 熔炉 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Furnace.prefab` |
| `UI_HandCraftTable` | 手工制作 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_HandCraftTable.prefab` |
| `UI_HandDrill` | 手钻 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_HandDrill.prefab` |
| `UI_IndustrialFluid` | 工业流体设备 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_IndustrialFluid.prefab` |
| `UI_MakerTable` | 制作台 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_MakerTable.prefab` |
| `UI_MeatRack` | 晾肉架 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_MeatRack.prefab` |
| `UI_Mechanical` | 机械设备、电池 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Mechanical.prefab` |
| `UI_Minecart` | 矿车 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Minecart.prefab` |
| `UI_Ship` | 飞船 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Ship.prefab` |
| `UI_SpaceElevator` | 太空电梯 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_SpaceElevator.prefab` |
| `UI_SpaceLanding` | 太空着陆 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_SpaceLanding.prefab` |
| `UI_Spacesuit` | 宇航服 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_Spacesuit.prefab` |
| `UI_SteamEngine` | 蒸汽机 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_SteamEngine.prefab` |
| `UI_StoneMortar` | 石臼 | `Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/UI_StoneMortar.prefab` |
| `UI_Debug` | 调试面板 | `Assets/2_Prefabs/2-1_UI/Gameplay/Debug/UI_Debug.prefab` |
| `UI_DebugScrollView` | 调试滚动内容 | `Assets/2_Prefabs/2-1_UI/Gameplay/Debug/UI_DebugScrollView.prefab` |
| `UI_RuntimeDebugOverlay` | 运行时调试悬浮窗 | `Assets/2_Prefabs/2-1_UI/Gameplay/Debug/UI_RuntimeDebugOverlay.prefab` |
| `UI_CharacterSpeechBubble` | 角色台词气泡 | `Assets/2_Prefabs/2-1_UI/Gameplay/Dialogue/UI_CharacterSpeechBubble.prefab` |
| `UI_PlayerChatInput` | 玩家聊天输入 | `Assets/2_Prefabs/2-1_UI/Gameplay/Dialogue/UI_PlayerChatInput.prefab` |
| `UI_HotBar` | 物品快捷栏 | `Assets/2_Prefabs/2-1_UI/Gameplay/HUD/UI_HotBar.prefab` |
| `UI_PlayerWorldCoordinate` | 坐标、FPS、TPS HUD | `Assets/2_Prefabs/2-1_UI/Gameplay/HUD/UI_PlayerWorldCoordinate.prefab` |
| `UI_SaveStatus` | 存档状态 HUD | `Assets/2_Prefabs/2-1_UI/Gameplay/HUD/UI_SaveStatus.prefab` |
| `UI_ShipFlightHUD` | 飞船飞行 HUD | `Assets/2_Prefabs/2-1_UI/Gameplay/HUD/UI_ShipFlightHUD.prefab` |
| `UI_HandSlot` | 手持物品槽 | `Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Components/UI_HandSlot.prefab` |
| `UI_ItemTooltip` | 物品悬浮提示 | `Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Components/UI_ItemTooltip.prefab` |
| `UI_Slot` | 通用物品槽 | `Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Components/UI_Slot.prefab` |
| `UI_Bag` | 背包、容器、库存 | `Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Bag.prefab` |
| `UI_Equipment` | 角色装备 | `Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Equipment.prefab` |
| `UI_Hand` | 手部、手持槽窗口 | `Assets/2_Prefabs/2-1_UI/Gameplay/Inventory/Panels/UI_Hand.prefab` |
| `UI_DimensionLoading` | 维度加载界面 | `Assets/2_Prefabs/2-1_UI/Gameplay/Loading/UI_DimensionLoading.prefab` |
| `UI_ResourceLoading` | 资源加载界面 | `Assets/2_Prefabs/2-1_UI/Gameplay/Loading/UI_ResourceLoading.prefab` |
| `UI_WorldLoading` | 世界加载界面 | `Assets/2_Prefabs/2-1_UI/Gameplay/Loading/UI_WorldLoading.prefab` |
| `UI_MobileControlLayoutEditor` | 手机按键布局编辑器 | `Assets/2_Prefabs/2-1_UI/Gameplay/Mobile/UI_MobileControlLayoutEditor.prefab` |
| `UI_MobileControls` | 手机触控 HUD | `Assets/2_Prefabs/2-1_UI/Gameplay/Mobile/UI_MobileControls.prefab` |
| `UI_BodyPartTreatment` | 身体部位治疗 | `Assets/2_Prefabs/2-1_UI/Gameplay/Player/UI_BodyPartTreatment.prefab` |
| `UI_AtmosphereAnalyzer` | 大气分析仪 | `Assets/2_Prefabs/2-1_UI/Gameplay/Screens/UI_AtmosphereAnalyzer.prefab` |
| `UI_AtmosphereAnalyzerGasRow` | 气体数据行 | `Assets/2_Prefabs/2-1_UI/Gameplay/Screens/UI_AtmosphereAnalyzerGasRow.prefab` |
| `UI_Death` | 死亡界面 | `Assets/2_Prefabs/2-1_UI/Gameplay/Screens/UI_Death.prefab` |
| `UI_ReadableBook` | 阅读书籍 | `Assets/2_Prefabs/2-1_UI/Gameplay/Screens/UI_ReadableBook.prefab` |
| `UI_BuffStatus` | Buff 状态列表 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Buff/UI_BuffStatus.prefab` |
| `UI_BuffStatusItem` | 单条 Buff | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Buff/UI_BuffStatusItem.prefab` |
| `UI_CreatureHealth` | 生物血条 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Creatures/UI_CreatureHealth.prefab` |
| `UI_Food` | 食物、营养状态条 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_Food.prefab` |
| `UI_Health` | 生命值条 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_Health.prefab` |
| `UI_ModuleButton` | 模块按钮 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_ModuleButton.prefab` |
| `UI_ModuleList` | 模块列表 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_ModuleList.prefab` |
| `UI_ModuleOpenButton` | 模块展开按钮 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_ModuleOpenButton.prefab` |
| `UI_ModuleSettings` | 模块设置 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_ModuleSettings.prefab` |
| `UI_Sleep` | 睡眠界面 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_Sleep.prefab` |
| `UI_Stamina` | 体力状态条 | `Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_Stamina.prefab` |

### 主菜单、新世界和存档（12）

| UI ID | 用途 / 搜索词 | Prefab 路径 |
| --- | --- | --- |
| `UI_ActionList` | 游戏内设置主面板、分类列表 | `Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_ActionList.prefab` |
| `UI_MainMenu` | 主菜单 | `Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_MainMenu.prefab` |
| `UI_MainMenuExitConfirmation` | 主菜单退出确认 | `Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_MainMenuExitConfirmation.prefab` |
| `UI_ModManager` | MOD 管理 | `Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_ModManager.prefab` |
| `UI_SaveContextMenu` | 存档操作菜单 | `Assets/2_Prefabs/2-1_UI/MainMenu/Save/UI_SaveContextMenu.prefab` |
| `UI_SaveRenameDialog` | 存档重命名弹窗 | `Assets/2_Prefabs/2-1_UI/MainMenu/Save/UI_SaveRenameDialog.prefab` |
| `UI_SaveSelectionButton` | 存档、角色选择条目 | `Assets/2_Prefabs/2-1_UI/MainMenu/Save/UI_SaveSelectionButton.prefab` |
| `UI_SaveSelectionPanel` | 存档选择界面 | `Assets/2_Prefabs/2-1_UI/MainMenu/Save/UI_SaveSelectionPanel.prefab` |
| `UI_NetworkMode` | 联机模式选择 | `Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NetworkMode.prefab` |
| `UI_NewGame` | 新游戏、创建世界 | `Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NewGame.prefab` |
| `UI_NewGameDifficultyContent` | 新游戏难度内容 | `Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NewGameDifficultyContent.prefab` |
| `UI_NewGameWorldContent` | 新游戏世界内容 | `Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NewGameWorldContent.prefab` |

### 设置面板与组件（14）

| UI ID | 用途 / 搜索词 | Prefab 路径 |
| --- | --- | --- |
| `UI_InputBindingRow` | 按键绑定单行控件 | `Assets/2_Prefabs/2-1_UI/Settings/Components/UI_InputBindingRow.prefab` |
| `UI_AudioSettings` | 音频设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_AudioSettings.prefab` |
| `UI_AutoSaveSettings` | 自动保存设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_AutoSaveSettings.prefab` |
| `UI_CameraControlSettings` | 镜头控制设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_CameraControlSettings.prefab` |
| `UI_CoordinateDisplaySettings` | 坐标显示设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_CoordinateDisplaySettings.prefab` |
| `UI_DebugSettings` | 调试设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_DebugSettings.prefab` |
| `UI_DifficultySettings` | 难度设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_DifficultySettings.prefab` |
| `UI_GameSettings` | 游戏设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_GameSettings.prefab` |
| `UI_InputBindingSettings` | 按键绑定设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_InputBindingSettings.prefab` |
| `UI_InterfaceSettings` | 界面设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_InterfaceSettings.prefab` |
| `UI_MainMenuSettings` | 主菜单设置总面板 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_MainMenuSettings.prefab` |
| `UI_SeasonSettings` | 季节设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_SeasonSettings.prefab` |
| `UI_VisualEffectsSettings` | 画质、视觉效果设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_VisualEffectsSettings.prefab` |
| `UI_WorldStreamingSettings` | 世界流送与性能设置 | `Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_WorldStreamingSettings.prefab` |

## UI 根节点（独立于上方目录）

| 名称 | 作用 | 路径 |
| --- | --- | --- |
| `UIRoot` | 全局 UI 根 Prefab；运行时面板的归属与具体挂载根由 `UIManager` 决定 | `Assets/Resources/UI/UIRoot.prefab` |

## 嵌入式页面与运行时创建的 UI

> 这些名称可以被用户看到或被代码查询，但**没有同名的独立 Prefab**。不要搜不到 `UI_语言设置.prefab` 就创建一个。

| 页面 / 子节点（搜索词） | 宿主资源 / 节点位置 | 控制入口 |
| --- | --- | --- |
| `设置分页_语言`（语言设置） | `UI_ActionList` 与 `UI_MainMenuSettings` 内嵌页 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/LanguageSettingsPanelLauncher.cs` |
| `设置分页_画质设置`（画质预设） | 同上，画质页；视觉细项复用 `UI_VisualEffectsSettings` | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/GraphicsSettingsPanelLauncher.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/VisualEffectsSettingsPanelLauncher.cs` |
| `设置分页_显示设置`（显示、FPS/TPS） | 同上，显示页 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/FrameRateSettingsPanelController.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/CoordinateDisplaySettingsPanelLauncher.cs` |
| `设置分页_UI设置`（界面缩放等） | 同上，界面设置页；另有 `UI_InterfaceSettings` Prefab | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/UISettingsPanelLauncher.cs` |
| `FWUI_InventoryCategoryTabs`（背包分类栏） | `UI_Bag` 的子树 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/InventoryCategoryFilter.cs` |
| `画面风格行`（后处理风格） | 画质页；旧 Prefab 缺行时运行时复用同页现有下拉行 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/GraphicsSettingsPanelLauncher.cs` |
| 保存退出确认层 | `UI_ActionList` 的内嵌层 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/SettingsExitConfirmationController.cs` |
| `存档条目_*` / `角色条目_*` | `UI_SaveSelectionPanel` 内的动态列表，条目基于 `UI_SaveSelectionButton` 复用 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/SaveDataManager_UI.cs` |

## 行为、布局与资源重建入口

| 关联领域 | 脚本 / 构建器路径 |
| --- | --- |
| UI 创建、关闭与焦点 | `Assets/5_Scripts/5-5_UI/Core/UIManager.cs`、`Assets/5_Scripts/5-5_UI/Core/BasePanel.cs` |
| 运行时 Prefab ID 常量 | `Assets/5_Scripts/5-5_UI/Core/RuntimeUIPrefabKeys.cs` |
| 游戏内设置加载、分页、会话 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/Mod_SettingCanvas.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/SettingsActionListPagination.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/SettingsEditSessionController.cs` |
| 设置 Prefab 构建 | `Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.SettingsPages.cs`、`Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.SettingsNavigation.cs` |
| 背包筛选、虚拟格子及布局 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/InventoryCategoryFilter.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/InventoryVirtualizedSlotGrid.cs`、`Assets/Editor/FlatWorld/PrefabBuilders/UI/GameUIPrefabRebuilder.Inventory.cs`、`Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.InventoryFilter.cs` |
| 主菜单资源构建 | `Assets/Editor/FlatWorld/PrefabBuilders/UI/MainMenuPrefabBuilder.cs` |
| 存档列表与 Prefab 构建 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/SaveDataManager_UI.cs`、`Assets/Editor/FlatWorld/PrefabBuilders/UI/GameSavePrefabBuilder.cs` |
| HUD 脚本与构建 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/PlayerWorldCoordinateHUD.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/PlayerShipFlightHUD.cs`、`Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.Hud.cs`、`Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.ShipFlightHud.cs` |
| UI 主题配置 | `Assets/Resources/GameConfig/UI/ui-theme.json` |
| UI 动画配置 | `Assets/Resources/Config/UIAnimations.json` |

## 更新约定

1. 新增、删除或移动项目自有 UI Prefab 时，同步修改这份表；**以文件实际路径为准**，移动文件时别丢 `.meta/GUID`。
2. 新增只存在于某个 Prefab 内的页面时，登记在「嵌入式页面」区，不虚构独立资源文件。
3. 名称、资源路径、运行时键、构建器是不同概念：只有确实在 `RuntimeUIPrefabKeys.cs` 声明的才称为运行时常量键。
4. 查找问题建议先用 `rg -n 'UI_Bag|背包' .agents/skills/flatworld-ui/references/ui-catalog.md`，再按命中的实际路径读取 Prefab、脚本和构建器；不用每次全量读取 UI Skill 参考资料。
