---
name: flatworld-player-interaction
description: "Use when: 定位或修改 FlatWorld 的玩家实体、输入系统、鼠标/手柄、虚拟光标、移动、摄像机、焦点、交互发送接收、管理员控制或玩家 Prefab。关键词：Player、GameController、InputBindingService、Mod_InteractSender。"
argument-hint: "玩家输入、移动或交互问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 玩家、输入与交互定位

> 最后核对：2026-07-31。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Entities/Item/Player.cs`：玩家 Item 实体。
2. `Assets/5_Scripts/5-1_Data/ItemData/Data_Player.cs`：玩家持久化数据。
3. `Assets/5_Scripts/5-3_GamePlay/Player/Controller/GameController.cs`：输入动作、鼠标/手柄、虚拟光标、输入锁定。
4. `Assets/5_Scripts/5-3_GamePlay/Player/Controller/InputBindingService.cs`：输入绑定服务。
5. `Assets/PlayerInput/PlayerInputActions.inputactions`：键鼠/手柄动作、Control Scheme 与稳定 Binding 的权威来源。

## 关键功能

- 交互发送：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/Mod_InteractSender.cs`。
- 交互接收：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/Mod_InteractReciver.cs`。
- 管理员控制：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/PlayerAdminController.cs`。
- 移动/朝向：`Assets/5_Scripts/5-3_GamePlay/Entities/Move/`。
- 摄像机模块：`Assets/5_Scripts/5-3_GamePlay/Entities/Move/Mod_Cam.cs`。
- 世界焦点：`Assets/5_Scripts/5-3_GamePlay/Entities/Move/Mod_FocusPoint.cs`。
- AI 焦点：`Assets/5_Scripts/5-3_GamePlay/Entities/Move/Mod_FocusPoint_AI.cs`。
- 玩家 Prefab：`Assets/2_Prefabs/Player/`。
- Player 根 Prefab 当前包含 `CharacterSoliloquyController`、`ConfiguredSpeechProvider`、`HungerSpeechProvider`、`ScreenSpaceSpeechBubblePresenter` 与唯一一个 `NewPlayerGuideController`。
- Player 根 Prefab 还包含唯一一个 `PlayerChatInputController`；仅 `IsLocalProfile=true` 的玩家监听聊天输入。
- 维度入口：`Assets/5_Scripts/5-3_GamePlay/World/Dimension/DimensionPortal.cs`，通过现有 `IInteractable`/E 键链请求 `DimensionManager` 切换并传递所属入口 Item 上下文。

## 调用边界

```text
Input System / PlayerInputActions
→ GameController
→ 点击、移动、交互或模块事件
→ 玩家 Item 上的 Inventory / Skill / Building / Hand 等模块
```

- UI 上方点击由 `GameController.IsPointerOverUI()` 拦截。
- F4 GM 的 Buff 点选模式订阅本地 `GameController.LeftClick.DynamicCalls`，因此沿用统一鼠标/虚拟光标坐标与 UI 点击拦截；模式由 GM 的确认、取消与清除按钮管理，场景切换时自动解除订阅。
- `PlayerInputActions.inputactions` 同时声明 `Keyboard&Mouse`、`Gamepad` Scheme；禁止在 `GameController` 运行时注入手柄 Binding，修改后由 Unity 自动生成 `PlayerInputActions.cs`。
- 当前手柄基础映射：左摇杆移动、右摇杆虚拟光标、RT/A 主要操作、LT/LB 次要操作、X 交互、Y 丢弃、B 背包、十字键上装备/下手工制作/左右切快捷栏、Start 设置、Select 营养面板。
- `GameController` 在有本地 Player 时负责切换 `EventSystemGuard` 的输入设备模式；主菜单等无 Player 阶段由 `GamepadUIRuntimeController` 直接检测键鼠/手柄。`FlatWorldUI/Navigate` 只接收手柄，键盘 W/A/S/D 必须始终只驱动玩家移动。
- `FlatWorldUI/Cancel` 的键鼠取消键固定为 Esc；键盘 B 只走 `Win10/B` 的背包开关，避免 EventSystem 与玩家库存模块同时消费同一次输入。
- 濒死、过场或联机准备期间使用输入锁定，不要通过禁用整个玩家对象规避输入。
- 模态 UI 使用 `AcquireGameplayInputLock(owner)` / `ReleaseGameplayInputLock(owner)` 叠加锁定，避免子窗口恢复时误解锁其他系统。
- 玩家运行时引用优先从 `ItemMgr.User_Player` / `UserPlayerTransform` 获取，兼容单机与联机本地玩家。
- `Player.Act()` 是显式安全空行为：玩家操作由 `GameController` 与功能模块驱动，不得回退到 `Item.Act()` 触发普通物品 `OnAct` 使用链。
- `GameController.Load()` / `Save()` 不持有世界存档数据；按键覆盖由 `InputBindingService` 通过 `PlayerPrefsInputBindingStore` 独立加载和保存。
- 自动保存只能采集 Player/Item 状态；不得调用 `SetGameplayInputLocked`、禁用 `Mover`/Rigidbody2D 或改写 `Time.timeScale`，后台写盘由 `AutoSaveController` 轮询完成。
- `InputBindingService` 按 `KeyboardMouse` / `Gamepad` 分组提供可编辑条目；重绑候选只能来自当前分页设备，Button 与 Vector2 分别限制控制类型，冲突只在同设备组内检测，恢复默认只清除当前分页覆盖。
- `Player.IsLocalProfile`、`IsNewProfile`、`WasProfileDataCreated` 与 `ProfileContextChanged` 仅为运行时档案上下文，不进入 `Data_Player` 序列化布局；新玩家判定来自数据是否创建，禁止用出生位置或本地控制权猜测。
- GM 自定义移速统一调用 `PlayerAdminController.TrySetAdminMoveSpeedMultiplier`；输入范围为 `0.1–100x`，替换上一次管理员倍率并保留 Buff、装备等其他乘法修饰，禁止直接覆写 `Mover.Speed.MultiplicativeModifier`。
- GM 玩家页的“管理员无敌”统一调用 `PlayerAdminController.TryToggleAdminInvincibility`；仅管理员可操作，开启时监听 `DamageReceiver.OnDamageReceived` 即时回满生命，并由 `Mod_PlayerDeathState` 拦截/恢复濒死。状态只保留当前运行时，启动或域重载后默认开启，不写入世界存档。
- 聊天按键契约：裸 `T` 打开聊天，`Enter` 提交，`Esc` 取消；打开期间同时设置 `GameController.SetGameplayInputLocked(true)` 并挂起 `InputBindingService` 的 Win10 Action Map，关闭时恢复之前的锁状态。
- 管理员传送使用 `Ctrl+T`，且管理员快捷键尊重 `IsGameplayInputLocked`；聊天控制器忽略带 Ctrl 的 T，避免同时打开聊天和传送。
- 玩家移动耐力不得直接修改 `Mod_Stamina.CurrentValue`；`Mover` 统一调用 `AddStamina()`，由该入口应用自定义难度的耐力消耗/恢复倍率。
- 奔跑输入统一由 `Mover.HandleRunInputPressed/Released()` 管理：Shift 按住时进入奔跑，松开后恢复普通移动；体力不足或输入锁定仍会强制结束奔跑。
- `Mover` 以 `speedTransitionDuration` 平滑切换走路、奔跑和转向速度，松开方向则使用更短的 `stopTransitionDuration`（默认 0.07 秒）减速；体力与饥饿 Buff 仍仅按方向输入结算，移动动画以实际 Rigidbody2D 速度结束为准。`IsGameplayInputLocked` 仍立即清零速度，避免模态 UI 打开后角色滑动。
- 完整维度切换通过 `ItemMgr.ReleasePlayerForWorldTransition()` 注销旧世界玩家，再由现有加载链重建；不得只移动 Transform 后保留旧 Chunk、Item 索引或场景归属。
- `Mod_InteractSender` 使用碰撞体所在 GameObject 的 `GetComponent<IInteractable>()`；矿坑入口/出口的 Trigger 与 `DimensionPortal` 必须同节点。`MineEntrance_Summoner` 虽复制入口组件，也必须由建筑角色检查拒绝交互；新版自然 `CaveExit` 通过 `ConfigureGenerated()` 走同格确定性目标，不读取旧玩家锚点。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-09：启用玩家交互范围时同步物理并主动扫描当前半径，补偿自然物/传送门在玩家到位后完成绑定的动态区块时序；避免首次按 E 依赖遗漏的 `OnTriggerEnter2D`。
- 2026-08-09：`Mod_InteractSender` 复用 `GameController.LeftClick` 与指针世界坐标，按距离解析 `IInteractable`，石门可直接左键开关且不绕过 UI/输入锁。
- 2026-08-09：玩家交互确定性天然 `CaveExit` 后，`DimensionPortal` 走专用生成入口分支；完整释放旧 Player/Scene、在新维度同格重建 Player，并等待 WorldModel 目标 ChunkView 表现完成后解锁输入，避免跨 Scene 保留旧 Chunk/Item 上下文。
- 2026-08-09：玩家走路、奔跑、转向都改为目标速度平滑过渡；松开方向后仅保留 0.07 秒的极短惯性，体力/饥饿结算不延长，动画在实际停下后结束；玩法输入锁仍立即停止。
- 2026-08-09：键鼠模式下 `FlatWorldUI/Navigate` 已删除 W/A/S/D 绑定；背包/菜单的橙色焦点框可保留，但 W/A/S/D 只移动玩家，手柄导航保持可用。
- 2026-08-09：键盘 B 已从 `FlatWorldUI/Cancel` 移除，保留给 `Win10/B` 背包开关；手柄 B/Start 的 UI 返回映射保持不变。
- 2026-08-09：自动保存改为分帧快照与后台写盘；保存期间 `GameController.IsGameplayInputLocked`、Mover/Rigidbody2D 与 `Time.timeScale` 必须保持原值，避免玩家无法移动。
- 2026-08-09：主菜单退出也统一使用 `ItemMgr.ReleasePlayerForWorldTransition()` 注销本地 Player，确保 `Player_DIC`、运行时 Item 注册与感知索引同步移除；禁止直接销毁 Player GameObject 后再等待场景卸载。
- 2026-08-09：F4 GM 玩家页新增“管理员无敌：开/关”按钮；关闭后生命、理智、饥饿与死亡恢复正常结算，重新开启会立即满状态并取消已进入的濒死。
- 2026-08-09：主菜单存档流程的手柄焦点按“世界→角色/名称→进入世界”交接；动态列表重建前会释放旧焦点，避免 EventSystem 指向已销毁条目。
## 修改后自动测试

- 精简 Smoke：`Assets/GameTest/PlayerInteraction/PlayerWorldWrapSmokeTests.cs`；当前只保留玩家跨四边与角落环绕时速度和数据不丢失这一关键行为。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；玩家交互测试约定目录：`Assets/GameTest/PlayerInteraction/`；场景目录：`Assets/GameTest/Scenes/PlayerInteraction/`；冒烟分类：`PlayerInteraction.Smoke`。
- 新增输入、移动、摄像机、焦点、交互发送接收或玩家 Prefab 行为时必须增加系统测试；修复 Bug 时先增加回归测试。输入到移动或交互主流程变化时同步更新玩家冒烟场景。
- 奔跑与速度过渡回归位于 `Assets/GameTest/PlayerInteraction/MoverRunInputTests.cs`，分类为 `PlayerInteraction.Input`；覆盖按下进入奔跑、短/长按松开恢复普通移动、重复按键不形成常驻状态，以及走路→奔跑→走路→松开的平滑速度变化。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；输入测试必须使用可注入输入，不能依赖真实鼠标、键盘或手柄操作。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category PlayerInteraction.Smoke`；无需视觉模型或测试工具卡片。涉及 UI、Item/Module、建筑、地图或联机玩家时追加对应分类；只有光标、相机或交互反馈最终观感变化才做定向截图。
- 管理员无敌的完整运行时回归位于 `Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathScenarios.PlayerMovement.cs`，在 `OnWorldReady` 验证关闭后受伤、重新开启后拦截致死伤害，并在 Cleanup 恢复玩家状态。
- 自动保存的玩家可操作性回归位于 `Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathScenarios.AutoSave.cs`，确认后台写盘结束后输入锁、Mover/Rigidbody2D 与时间缩放均未被保存链改变。
- Player 教程资格、Prefab 接线与远程隔离由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 玩家聊天的输入锁、Prefab 接线与按键冲突不再属于精简 Smoke 集合，修改聊天系统时按需运行或补充专项测试。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变 Input Action 名称、玩家 Prefab、控制器模块、交互协议、摄像机/焦点路径或本地玩家解析方式后，必须更新本 Skill；影响 UI、网络或 Item 注册时同步更新对应 Skill。

## 玩家环绕契约（2026-08-06）

- `Player.prefab` 根节点挂载 `PlayerWorldWrapController`；仅 `Player.IsLocalProfile` 且当前拓扑为 Wrapped 时处理 Rigidbody2D 越界。
- 环绕必须保留速度、Z 与越界余量，同步 `Data_Player.transform.position`，并通知 Chunk/导航窗口立即刷新。
- URP 边缘镜像相机必须作为主 Base Camera 的 Overlay camera stack 渲染；独立 Base 相机会清空颜色缓冲，在边界截图中形成黑块。镜像相机不渲染 UI、没有 Collider/AudioListener，退出世界时从 stack 移除。
- AI、掉落物、投射物等其他动态实体不在一期范围；四向与无限模式回归位于 `PlayerWorldWrapSmokeTests`。

## 玩家物理碰撞契约（2026-08-06）

- `Player.prefab` 根节点及其非 Trigger 实体碰撞体固定使用 Layer 10 的 `Player` 层；不得递归覆盖模块或攻击 Trigger 的专用 Layer。
- Physics 2D Layer Collision Matrix 只关闭 `Player ↔ Player`；玩家之间可以重叠，不产生推挤或阻挡。
- `Player` 必须继续与 `Default`、`Collider`、`DamageReciver` 和 `DamageSender` 碰撞；攻击、治疗、拾取与交互由原系统继续结算。
- `FlatWorldNetworkPlayer.prefab` 保持无 Collider 的网络代理；本地和远程实体统一由核心 Player Prefab 提供碰撞规则，无需增加 Mirror 同步字段。
