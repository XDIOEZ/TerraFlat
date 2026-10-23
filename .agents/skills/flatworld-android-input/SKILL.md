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
- 空手触屏按住有物品的槽位 1 秒复用 `InventoryItemTooltip`，按下与等待期间都检查真实手部库存；松手保留、窗外再按收起，不吞窗外点击。已打开介绍的本次手势不再取放或拖拽；移动、换物、重绑、禁用、关闭面板与失焦取消等待或清理介绍，手持物原长按保持原规则。
- 鼠标与触屏库存甩动共用识别和提交入口，复用 `ItemSlot_UI` 直接拖拽与正式 quick 事务，通过独立 `OnFlickTransfer` 按源起点和净方向选择可见库存；消费前先检查方向是否落在真实目标窗口范围及边缘 10° 内，范围外按普通拖拽拿到手部；范围内 `InventoryFlickTargets` 按槽中心夹角优先，满格/不接收时只在同范围候选中跳过，部分接收后余物留源，不改变 PC Shift 默认目标。净位移和末段速度同时过线，`InventoryFlickGesture` 按 Canvas 比例归一化；默认距离 `64/1.5`、最近 0.1 秒速度 `800/1.5`，末尾 0.04 秒仍需达到主速度门槛的一半。正式槽位/玩法拖放目标优先，拆半拖拽不甩动；抬起保留轨迹到 EndDrag，动画从松手位置直接飞向目标槽；起点只在本次来源事务携带，联机请求单独保存到回执。其他触点不接管，失败留原槽。
- 甩动门槛由 `InventoryFlickUserSettings` 读取设备级偏好，游戏操作页共用两条25%～200%的速度/距离门槛滑条（默认100%，越低越易触发）；角度限制默认开启，关闭仅绕过窗口角度，仍需其他可见库存，正式槽位拖放优先与满格留源保持原规则。手机与PC使用同一份偏好和设置会话。
- 中间丢弃面只在手持物存在时接收射线，位于玩法控件下；库存/制作面板通过 `BasePanel.SetHeldItemWorldDropAllowed` 登记能力和输入锁拥有者，允许时仅丢弃面移至常驻层底部，正式窗口/槽位优先。全部打开的阻断面板与输入锁均须允许，设置/确认、显式锁和加载继续阻断；按下与提交时复查资格，手势仍为轻点单件、长按整组。
- 双指捏合只占左右控制区之间的中间区域，按两个 touchId 的间距变化识别：分开拉近、合拢拉远，同向平移无缩放。统一调用 `Mod_Cam.ApplyPinchZoom`，灵敏度为 `PinchZoomSensitivity` 的 0～300（0 关闭、100 基准）。

## HUD、射线与安全区

- HUD 仅为本地玩家选择 Mobile 后显示；控制偏好手动持久化，键鼠/手柄/手机设备可并行输入。正式 Prefab 挂于 `UIManager.SafeAreaRoot`，行为绑定与视觉结构分离。
- 触屏布局编辑器的正式 `UI_MobileControlLayoutEditor.prefab` 与 `BuildMobileControlLayoutEditor` 同步维护节点/组件契约；尺寸编辑具备“尺寸文本、缩小按钮、放大按钮”，透明度使用“触屏控件透明度、触屏控件透明度数值”，草稿仅保存时写入。初始化失败返回 false 并销毁临时面板，成功后才登记活动实例。
- 三种摇杆复用 `MobileControlLayoutNode.ConfigureJoystick` 配置触控区、视觉底座与职责；移动/朝向/攻击尺寸分别持久化，浮动摇杆只缩放底座，输入统一在底座局部坐标换算，不随尺寸缩放左右捕获区。预览显示按住才出现的移动/朝向底座，固定控件可拖动。
- 普通指向捕获层在按钮/攻击摇杆后，仅接收空白区域；左右区域比例来自 `LeftControlZoneRatio/RightControlZoneRatio`，改比例先释放触点。`FloatingMoveJoystick` 保存固定/浮动偏好；移动/指向摇杆取得触点后显示，攻击摇杆固定。
- `TouchControlsOpacityPercent` 仅作用玩法层及手机快捷栏，0% 仍可交互；常驻菜单完全可见。菜单/返回独立于可隐藏玩法层，抽屉入口在 Button.onClick 完整点击后脉冲；返回先关闭顶层面板，再抽屉，再设置。
- 控制根平时位于常驻 HUD 后，模态时沉底；抽屉仅无正式模态时置顶。快捷栏为玩法层兄弟，在背包/容器下保持拖放；模态时独立 Canvas 临时 overrideSorting，关闭后恢复。槽内选中框归属当前槽位并按内部兄弟顺序置底。
- 返回统一从 `FlatWorldUI/Cancel → EventSystemGuard.CancelRequested → Mod_SettingCanvas` 分发；手机 settings 也绑定 UI Cancel，不再单独监听 Win10/ESC 打开设置。`BasePanel.OnCancel` 尊重同帧消费，避免一次按键打开后立即关闭或连关两层。
- `BasePanel` 的 GameplayInputBlocking 与手柄导航资格独立，常驻 HUD 显式关闭阻断，模态接管玩法输入。
- Android `mandatorySystemGestures` 补齐 safeArea 未覆盖的底部边距；强制系统手势区不可被 exclusionRects 屏蔽。
- 手机准线使用 Prefab 的非交互 `GamepadCursorGraphic`，HUD 在 `Canvas.willRenderCanvases` 按最终玩家/相机位置投影；普通距离来自交互模块，只有实际进入 `IsPlacementModeActive` 的建筑才用放置距离，木桶等便携容器未放置时沿用普通范围。无限建造映射为有限视野距离，`PlayerAimCursorSystem` 在插值前过滤非有限距离/力度；入世按 `Mod_TurnBack.currentDirection` 初始化，HUD 对缺失准线节点有一次性补齐路径。
