---
name: flatworld-android-input
description: "定位、修改和验证 FlatWorld 的 Android/移动端输入系统。Use when: 处理触摸、多点触控、虚拟摇杆、右半屏指向、攻击摇杆、FlatWorldMobileDevice、Mobile 控制方案、手机 HUD、安全区、Android 返回键、移动端输入锁清理，或排查手机交互/使用误触发攻击、移动与攻击卡住、触控被 UI 吞掉、刘海遮挡等问题。关键词：FlatWorldMobileDevice、MobileInputRuntime、MobileVirtualJoystick、PlayerMobileControlsHUD、MobileAim_Player、MobileAttackAim_Player。"
---

# FlatWorld Android 输入系统导航

## 执行流程

1. 先读取 [运行时导航图](references/runtime-map.md)，沿“触摸控件 → 虚拟设备 → Input Actions → `GameController` → 玩法消费者”定位问题，不要从症状处复制一套输入状态。
2. 修改前检查 `git status --short` 与目标文件 diff，保留用户已有改动；定位到明确入口后停止泛化搜索。
3. 按职责修改最小层级：触控所有权留在控件层，设备状态留在 `MobileInputRuntime`，输入语义留在 `GameController`，具体效果留在对应玩法模块。
4. 涉及 HUD、Prefab、安全区或 EventSystem 时同时使用 `flatworld-ui`；涉及武器攻击时使用 `flatworld-combat`；涉及快捷栏、物品菜单、丢弃、种植或工具时使用 `flatworld-inventory-crafting`；涉及建筑放置时使用 `flatworld-building`。
5. 完成后读取 [验证导航图](references/validation-map.md)，默认只做静态诊断、Android 脚本编译与 Unity Console 检查；仅在用户明确要求时运行 Unity Test Runner 或 Golden Path。

## 必须保持的不变量

- 保持 `FlatWorldMobileDevice` 为独立 `InputDevice`，不得继承或伪装成 `Gamepad`，否则会误启用手柄焦点导航和手柄光标模式。
- 仅选择手机控制方案时不要提前创建 `FlatWorldMobileDevice`；由首次触控写入按需创建，避免 Unity 域重载尝试恢复虚拟设备时产生布局重建警告。
- 只编辑 `PlayerInputActions.inputactions` 作为 Action/Binding 真相；`PlayerInputActions.cs` 是生成文件，不要手工维护。
- 保持攻击、交互、使用三条语义分流：手机攻击只产生 `AttackStarted`/`AttackEnded`；交互复用 `E`；使用复用 `RightClick`。不得让手机攻击回落到 `LeftClick`。
- 手机 `RightClick` 使用事件来自 HUD 按钮，`GameController` 与快捷栏消费端都不能再用 `IsPointerOverUI()` 拦截；仅 Mobile 绕过，桌面右键仍保持 UI 遮挡保护。
- 保持普通指向只更新朝向和最后有效力度，松手后准线保留最后世界位置；攻击按下立即开始，未出死区时沿普通朝向，拖出死区后使用攻击方向，松开后恢复普通朝向。
- 普通指向的最终方向同时作为交互目标选择依据；交互优先命中该方向前方目标，找不到时才回退到距离排序。
- 所有需要世界坐标的交互、放置、种植、锄地、工具和丢弃路径统一调用 `GameController.GetPointerScreenPosition()` 或 `GetMouseWorldPosition()`，不得直接新增 `Mouse.current`、`Input.mousePosition` 或 `Camera.ScreenToWorldPoint` 读取。
- 每个摇杆和按住型按钮独立持有自己的 `pointerId`。不得使用单个全局触摸、`Input.GetTouch(0)` 或共享“当前手指”。
- `Inventory_Hand` 有有效物品时，`MobileVirtualJoystick` 不得取得触摸所有权；已有摇杆必须立即 `ResetOwnership`，避免拖拽丢弃期间生成浮动摇杆。
- 快捷栏拖拽进入世界空白区后的长按检测必须复用 `ItemSlot_UI` 的独立 `pointerId` 与 EventSystem 射线；不得把其它 UI 区域当作世界落点。
- 在输入锁、模态面板、暂停、失焦、后台、控件禁用、方向/尺寸变化、玩家销毁时，同时释放触摸所有权、移动、攻击按钮和攻击语义；清理必须幂等。
- 保持 `EventSystemGuard` 的 `UIPointerBehavior.AllPointersAsIs` 与逐触点 UI 绑定；不要将多点触控合并为单指。
- 保持右侧普通指向层位于功能按钮和攻击摇杆之后，只有空白区域能取得普通指向所有权；不要用全屏透明层遮住按钮射线。
- 手机控制根正常游戏时必须排在同级常驻 HUD 后方，让任务追踪等真实按钮优先接收射线；菜单抽屉展开时也属于临时交互层，必须把包含抽屉的控制根临时提到最上层，关闭抽屉或模态面板后必须恢复到底层。
- 手机 HUD 的菜单/返回入口必须放在独立的常驻控制层，不能和移动、指向、攻击、交互、使用、奔跑一起放入玩法控制层；模态玩法面板打开时菜单仍可展开作为背包/制作等并行入口，Android 返回键或 Escape 才优先关闭最上层可取消面板。
- 左手移动摇杆的固定/浮动偏好统一由 `UIUserSettings.FloatingMoveJoystick` 持久化；切换模式必须先释放当前触点，浮动模式覆盖左半屏并在按下点出现，固定模式复用同一摇杆实例与底座坐标算法。
- 正式手机视觉以 `UI_MobileControls.prefab` 为真相并挂到 `UIManager.SafeAreaRoot`；运行时只绑定行为和现有 HUD，不拼装另一套视觉。
- 手机快捷栏锚点必须与玩法控制层同级，不能成为玩法层子节点；模态背包只隐藏摇杆和玩法按钮，快捷栏需保持可见并提升到面板之上参与拖放。
- `UI_HotBar.prefab` 自带独立 Canvas；模态容器打开时仅调整手机 HUD 根节点兄弟顺序不足以保证快捷栏获得射线，必须临时启用快捷栏 Canvas 的 `overrideSorting` 并提升排序，关闭容器后恢复原始值。
- 快捷栏槽内的选中框切换时必须重新挂到目标槽位并按槽位兄弟顺序置底；手机模态提升的是快捷栏整体 Canvas，不能用整体排序覆盖槽内物品数量文本。
- 面板接管玩法输入必须独立于手柄导航资格：`BasePanel` 默认阻断玩法输入，快捷栏、手部库存和状态条等常驻 HUD 必须显式调用 `SetGameplayInputBlocking(false)`；不能因为某个面板未调用 `PrepareForGamepadNavigation` 就让手机摇杆继续生效。
- 只为本地玩家在设置页手动选择 Mobile 控制方式时显示 HUD；真实触屏、键鼠或手柄输入不得自动切换控制方式。不要让远程玩家或未选择 Mobile 的实例创建手机控制层。
- 保持键鼠、手柄与存档格式兼容；新增移动端行为不得修改桌面端原有合成语义。
- `PlayerInputActions` 资产、`Win10` 玩法 ActionMap 与运行时 UI ActionMap 都不得设置设备组 `bindingMask`；控制偏好只决定 HUD/指针呈现，键盘、手柄和手机虚拟设备必须能并行输入。
- 当前输入设备变化只更新指针/UI状态，不得调用 `MobileInputRuntime.ResetAll()` 或清空触摸所有权；触摸清理只能由输入锁、模态面板、生命周期和真实控件失效触发。
- 手机方案下 Shift 等键盘修饰键只由对应玩法模块消费，不参与设备切换；真实鼠标或 Device Simulator 转发的 Touchscreen 点击才退出手柄 UI/虚拟光标模式。不得切走手机 HUD、清空触摸或继续使用旧的虚拟光标位置判断 UI 命中。
- 手机最终径向朝向的准线复用 `GamepadCursorGraphic`，正式节点必须位于 `UI_MobileControls.prefab` 并由 `PlayerMobileControlsHUD` 定位；不得只修改手柄 UI 虚拟光标。
- 手机准线的世界距离必须按当前手持物动态取值：空手或非建筑复用 `Mod_InteractSender.maxInteractDistance`，建筑召唤器复用 `Mod_Building.Data.maxVisibleDistance`。
- `PlayerMobileControlsHUD` 允许仅在旧 Addressables/缓存 Prefab 缺少正式准线节点时补建兼容节点，正常视觉仍以 `UI_MobileControls.prefab` 为准。
- 手机进入玩法时准线要按 `Mod_TurnBack.currentDirection` 初始化并立即显示，不能要求玩家先触摸普通指向区才出现。

## 跨系统边界

- 修改输入设备、Action、设备切换、径向指向或清理生命周期：同时读取 `flatworld-player-interaction`。
- 修改可见文案：同时读取 `flatworld-localization`，将文本写入 `FlatWorldUI` 中英文表，不在脚本里新增硬编码玩家文案。
- 修改 Android 启动、平台配置或全局生命周期：同时读取 `flatworld-core`。
- 修改确定性的真实单人运行时行为并需要自动化覆盖：同时读取 `flatworld-golden-path` 与 `flatworld-test-automation`，但仍遵守“未经用户明确要求不运行测试”的项目规则。

## Skill 维护

- 只补充后续维护可复用的入口、易错点、隐含约束和验证方式。
- 不记录日期、提交、近期变更或“这次改了什么”的流水账；代码已经清楚表达的实现细节不要重复抄入 Skill。
