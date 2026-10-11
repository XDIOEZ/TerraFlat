---
name: flatworld-android-input
description: "FlatWorld 移动触控、虚拟输入设备、摇杆/指向/攻击、手机 HUD、多指所有权与安全区。"
---

# FlatWorld Android 输入

## 入口与分层

`Assets/5_Scripts/5-3_GamePlay/`：

- `Player/Controller/FlatWorldMobileDevice.cs`：独立 InputDevice 与 `MobileInputRuntime` 状态；首次触控写入时按需创建。
- 同目录 `Mod_GameController.cs/PlayerAimCursorSystem.cs`：输入语义、径向指针与最后瞄准状态。
- `Presentation/UI/{MobileVirtualJoystick,MobileInputButton,MobileHeldItemDropSurface,PlayerMobileControlsHUD}.cs`：触点、手持丢弃、HUD。
- 视觉：`Assets/2_Prefabs/2-1_UI/Gameplay/Mobile/UI_MobileControls.prefab`；动作源：`Assets/PlayerInput/PlayerInputActions.inputactions`。
- UI 支撑：`Assets/5_Scripts/5-5_UI/Input/EventSystemGuard.cs`、`Common/Controls/{SafeAreaRectController,AndroidSystemGestureInsets}.cs`。
- 补充导航：`references/runtime-map.md`。

`触控所有权 → MobileInputRuntime → FlatWorldMobileDevice → Input Actions → Mod_GameController → 玩法模块`

## 输入语义与触点

- 虚拟设备独立于 Gamepad。攻击仅产生 `AttackStarted/AttackEnded`，交互为 E，使用为 RightClick；手机使用按钮允许从 HUD 进入，桌面右键仍受 UI 遮挡保护。
- 普通指向更新朝向/最后力度；攻击按下立即开始，死区内沿普通朝向、死区外更新并保持。松开只结束即时输入，攻击摇杆保留视觉位置；输入锁等完整清理回中，但持久瞄准方向/力度/目标保留。
- 每个摇杆/按住按钮持有独立 pointerId，EventSystem 为 `AllPointersAsIs`。锁定、模态、暂停、失焦/后台、禁用、尺寸变化、玩家销毁统一幂等释放触点与移动/攻击语义；设备状态变化不清触摸。
- 世界坐标来自控制器统一指针接口。建筑虚影旋转使用独立 touchId 的短按与当前虚影命中，再调用建筑旋转入口；拖动/长按/正式按钮/攻击区/锁定不进入该通道。
- `Inventory_Hand` 有物品时摇杆释放所有权；快捷栏拖拽长按复用槽位 pointerId 与 UI 射线。拿起后的轻点/长按由 `MobileHeldItemDropSurface` 持有，轻点抬起丢单件，长按提交前结束触点以避免重复；移动超限取消丢弃但保留跟手到抬起，换物完整取消。
- 中间丢弃面只在手持物存在时接收射线，位于玩法控件下；主背包打开时移至常驻层底部，正式面板/槽位优先，其它模态不开放世界丢弃。
- 双指捏合只占左右控制区之间的中间区域，按两个 touchId 的间距变化识别：分开拉近、合拢拉远，同向平移无缩放。统一调用 `Mod_Cam.ApplyPinchZoom`，灵敏度为 `PinchZoomSensitivity` 的 0～300（0 关闭、100 基准）。

## HUD、射线与安全区

- HUD 仅为本地玩家选择 Mobile 后显示；控制偏好手动持久化，键鼠/手柄/手机设备可并行输入。正式 Prefab 挂于 `UIManager.SafeAreaRoot`，行为绑定与视觉结构分离。
- 普通指向捕获层在按钮/攻击摇杆后，仅接收空白区域；左右区域比例来自 `LeftControlZoneRatio/RightControlZoneRatio`，改比例先释放触点。`FloatingMoveJoystick` 保存固定/浮动偏好；移动/指向摇杆取得触点后显示，攻击摇杆固定。
- `TouchControlsOpacityPercent` 仅作用玩法层及手机快捷栏，0% 仍可交互；常驻菜单完全可见。菜单/返回独立于可隐藏玩法层，抽屉入口在 Button.onClick 完整点击后脉冲；返回先关闭顶层面板，再抽屉，再设置。
- 控制根平时位于常驻 HUD 后，模态时沉底；抽屉仅无正式模态时置顶。快捷栏为玩法层兄弟，在背包/容器下保持拖放；模态时独立 Canvas 临时 overrideSorting，关闭后恢复。槽内选中框归属当前槽位并按内部兄弟顺序置底。
- `BasePanel` 的 GameplayInputBlocking 与手柄导航资格独立，常驻 HUD 显式关闭阻断，模态接管玩法输入。
- Android `mandatorySystemGestures` 补齐 safeArea 未覆盖的底部边距；强制系统手势区不可被 exclusionRects 屏蔽。
- 手机准线使用 Prefab 的非交互 `GamepadCursorGraphic`，HUD 在 `Canvas.willRenderCanvases` 按最终玩家/相机位置投影；普通距离来自交互模块，建筑来自放置距离，入世按 `Mod_TurnBack.currentDirection` 初始化，HUD 对缺失准线节点有一次性补齐路径。
