# Android 输入运行时导航图

## 主调用链

`MobileVirtualJoystick` / `MobileInputButton`
→ `MobileInputRuntime`
→ `FlatWorldMobileDevice`
→ `PlayerInputActions` 的 `Mobile` 绑定组
→ `GameController`
→ 移动、指向、攻击、交互、使用及各玩法模块。

排查时从链首确认触摸所有权和写入值，再逐层向下；不要在 HUD 或玩法模块直接模拟键盘、鼠标或手柄。

## 权威入口

| 层级 | 文件 | 职责 |
|---|---|---|
| 虚拟设备 | `Assets/5_Scripts/5-3_GamePlay/Player/Controller/FlatWorldMobileDevice.cs` | 注册独立设备，保存三组方向和按钮位，集中写入与 `ResetAll()` |
| Action 真相 | `Assets/PlayerInput/PlayerInputActions.inputactions` | 定义 `Mobile` 控制方案与动作绑定 |
| 生成包装 | `Assets/PlayerInput/PlayerInputActions.cs` | Input System 自动生成；只检查，不手改 |
| 输入语义 | `Assets/5_Scripts/5-3_GamePlay/Player/Controller/GameController.cs` | 设备切换、径向虚拟指针、攻击事件、输入锁和生命周期清理 |
| 准星系统 | `Assets/5_Scripts/5-3_GamePlay/Player/Controller/PlayerAimCursorSystem.cs` | 统一摇杆死区、世界距离、屏幕径向位置和目标距离裁剪；`GameController` 持有并调用 |
| 摇杆 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/MobileVirtualJoystick.cs` | 每实例持有 `pointerId`；移动、浮动指向与攻击摇杆写入 |
| 按钮 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/MobileInputButton.cs` | 按住/抬起转换为虚拟设备按钮并可靠释放 |
| HUD 控制器 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/PlayerMobileControlsHUD.cs` | 本地玩家可见性、Prefab 绑定、抽屉、快捷栏、面板联动和总清理 |
| 正式视觉 | `Assets/2_Prefabs/2-1_UI/Runtime/Mobile/UI_MobileControls.prefab` | 控件布局、射线顺序与节点命名契约 |
| Prefab 构建 | `Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.cs` | `FlatWorld/UI/Rebuild Mobile Controls UI`，生成正式手机 UI |
| Prefab 键 | `Assets/5_Scripts/5-5_UI/Core/RuntimeUIPrefabKeys.cs` | 手机控制 Prefab 的稳定寻址键 |

## Mobile 动作映射

| 设备控件 | Action | 语义 |
|---|---|---|
| `move` | `Move_Player` | 左摇杆移动 |
| `aim` | `MobileAim_Player` | 普通朝向，不攻击 |
| `attackAim` | `MobileAttackAim_Player` | 攻击期间覆盖朝向 |
| `attack` | `Attack_Player` | 仅产生攻击开始/结束 |
| `interact` | `E` | 世界对象交互 |
| `use` | `RightClick` | 当前快捷栏物品 `Act` |
| `run` | `ToggleRun` | 奔跑切换 |
| `inventory` / `equipment` / `crafting` / `survival` | `B` / `P` / `H` / `Tab` | 打开正式面板 |
| `settings` | `ESC` | 统一返回栈与设置 |

## UI、触控与安全区

| 问题 | 先检查 |
|---|---|
| 多指被合并、第二根手指无效 | `Assets/5_Scripts/5-5_UI/Input/EventSystemGuard.cs` 的逐触点绑定和 `AllPointersAsIs` |
| 刘海遮挡、横屏翻转或尺寸变化 | `Assets/5_Scripts/5-5_UI/Common/Controls/SafeAreaRectController.cs`、`Assets/5_Scripts/5-5_UI/Core/UIManager.cs`、`Assets/Resources/UI/UIRoot.prefab` |
| 面板打开后仍能移动/攻击 | `UIManager.InteractionSurfaceChanged`、`PlayerMobileControlsHUD.RefreshInteractionSurface()`、`GameController.CancelActiveAttackAndMobileInput()` |
| 按钮会改变普通朝向 | `UI_MobileControls.prefab` 的层级/射线顺序和指向捕获层范围 |
| 快捷栏与摇杆重叠 | `PlayerMobileControlsHUD.TryConfigureHotbarWidth()`；上限为安全区宽度 44% 与 760 参考像素的较小值 |
| Android 返回行为错误 | `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/Module_Setting.cs` 与 `PlayerMobileControlsHUD.TryCloseActiveDrawer()` |

返回顺序保持：最上层临时/正式面板 → 手机抽屉 → 设置面板；不要直接退出游戏。

## 玩法消费者

- 武器只监听 `AttackStarted`/`AttackEnded`：`Assets/5_Scripts/5-3_GamePlay/Entities/Combat/Mod_Weapon_AnimationAction.cs`、`Assets/5_Scripts/5-3_GamePlay/Entities/Combat/Mod_ColdWeapon.cs`。
- 建造和世界指向：`Assets/5_Scripts/5-3_GamePlay/World/Building/Mod_Building.cs`。
- 锄地、农田补给和种植：`Assets/5_Scripts/5-3_GamePlay/Items/Food/Mod_Hoe.cs`、`Assets/5_Scripts/5-3_GamePlay/Items/Food/Mod_FarmlandSupply.cs`、`Assets/5_Scripts/5-3_GamePlay/Entities/Item/Modules/Growth/Mod_Seed.AuthoritativePlanting.cs`。
- 丢弃、槽位长按和物品菜单：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/Modules/Inventory/Module_DiscardItem.cs`、`Assets/5_Scripts/5-3_GamePlay/Items/Inventory/ItemSlot_UI.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/RightClickMenu_UI.cs`。
- Android 帧率与质量启动配置：`Assets/5_Scripts/5-3_GamePlay/Core/MobilePlatformBootstrap.cs`；平台序列化配置位于 `ProjectSettings/ProjectSettings.asset` 和 `ProjectSettings/QualitySettings.asset`。

## 定位命令

```powershell
rg -n "FlatWorldMobileDevice|MobileInputRuntime|MobileAim_Player|MobileAttackAim_Player" Assets
rg -n "AttackStarted|AttackEnded|LeftClick|RightClick" Assets/5_Scripts/5-3_GamePlay
rg -n "GetPointerScreenPosition|GetMouseWorldPosition|Mouse.current|Input.mousePosition|ScreenToWorldPoint" Assets/5_Scripts/5-3_GamePlay
rg -n "ResetAllTouchState|CancelActiveAttackAndMobileInput|InteractionSurfaceChanged" Assets
```

若第三条命令在新增的手机可达玩法路径中发现直接鼠标读取，优先改为统一指向接口；不要无关重构编辑器工具或纯桌面功能。
