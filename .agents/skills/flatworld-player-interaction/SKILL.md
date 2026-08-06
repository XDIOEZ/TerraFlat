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

1. `Assets/5_Scripts/5-3_GamePlay/Item/Player.cs`：玩家 Item 实体。
2. `Assets/5_Scripts/5-1_Data/ItemData/Data_Player.cs`：玩家持久化数据。
3. `Assets/5_Scripts/5-3_GamePlay/Controller/GameController.cs`：输入动作、鼠标/手柄、虚拟光标、输入锁定。
4. `Assets/5_Scripts/5-3_GamePlay/Controller/InputBindingService.cs`：输入绑定服务。
5. `Assets/PlayerInput/PlayerInputActions.inputactions`：键鼠/手柄动作、Control Scheme 与稳定 Binding 的权威来源。

## 关键功能

- 交互发送：`Assets/5_Scripts/5-3_GamePlay/Controller/Mod_InteractSender.cs`。
- 交互接收：`Assets/5_Scripts/5-3_GamePlay/Controller/Mod_InteractReciver.cs`。
- 管理员控制：`Assets/5_Scripts/5-3_GamePlay/Controller/PlayerAdminController.cs`。
- 移动/朝向：`Assets/5_Scripts/5-3_GamePlay/Move/`。
- 摄像机模块：`Assets/5_Scripts/5-3_GamePlay/Move/Mod_Cam.cs`。
- 世界焦点：`Assets/5_Scripts/5-3_GamePlay/Move/Mod_FocusPoint.cs`。
- AI 焦点：`Assets/5_Scripts/5-3_GamePlay/Move/Mod_FocusPoint_AI.cs`。
- 玩家 Prefab：`Assets/2_Prefabs/Player/`。
- Player 根 Prefab 当前包含 `CharacterSoliloquyController`、`ConfiguredSpeechProvider`、`HungerSpeechProvider`、`ScreenSpaceSpeechBubblePresenter` 与唯一一个 `NewPlayerGuideController`。
- Player 根 Prefab 还包含唯一一个 `PlayerChatInputController`；仅 `IsLocalProfile=true` 的玩家监听聊天输入。
- 维度入口：`Assets/5_Scripts/5-3_GamePlay/Dimension/DimensionPortal.cs`，通过现有 `IInteractable`/E 键链请求 `DimensionManager` 切换并传递所属入口 Item 上下文。

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
- 濒死、过场或联机准备期间使用输入锁定，不要通过禁用整个玩家对象规避输入。
- 模态 UI 使用 `AcquireGameplayInputLock(owner)` / `ReleaseGameplayInputLock(owner)` 叠加锁定，避免子窗口恢复时误解锁其他系统。
- 玩家运行时引用优先从 `ItemMgr.User_Player` / `UserPlayerTransform` 获取，兼容单机与联机本地玩家。
- `Player.Act()` 是显式安全空行为：玩家操作由 `GameController` 与功能模块驱动，不得回退到 `Item.Act()` 触发普通物品 `OnAct` 使用链。
- `GameController.Load()` / `Save()` 不持有世界存档数据；按键覆盖由 `InputBindingService` 通过 `PlayerPrefsInputBindingStore` 独立加载和保存。
- `InputBindingService` 按 `KeyboardMouse` / `Gamepad` 分组提供可编辑条目；重绑候选只能来自当前分页设备，Button 与 Vector2 分别限制控制类型，冲突只在同设备组内检测，恢复默认只清除当前分页覆盖。
- `Player.IsLocalProfile`、`IsNewProfile`、`WasProfileDataCreated` 与 `ProfileContextChanged` 仅为运行时档案上下文，不进入 `Data_Player` 序列化布局；新玩家判定来自数据是否创建，禁止用出生位置或本地控制权猜测。
- 聊天按键契约：裸 `T` 打开聊天，`Enter` 提交，`Esc` 取消；打开期间同时设置 `GameController.SetGameplayInputLocked(true)` 并挂起 `InputBindingService` 的 Win10 Action Map，关闭时恢复之前的锁状态。
- 管理员传送使用 `Ctrl+T`，且管理员快捷键尊重 `IsGameplayInputLocked`；聊天控制器忽略带 Ctrl 的 T，避免同时打开聊天和传送。
- 玩家移动耐力不得直接修改 `Mod_Stamina.CurrentValue`；`Mover` 统一调用 `AddStamina()`，由该入口应用自定义难度的耐力消耗/恢复倍率。
- 完整维度切换通过 `ItemMgr.ReleasePlayerForWorldTransition()` 注销旧世界玩家，再由现有加载链重建；不得只移动 Transform 后保留旧 Chunk、Item 索引或场景归属。
- `Mod_InteractSender` 使用碰撞体所在 GameObject 的 `GetComponent<IInteractable>()`；矿坑入口/出口的 Trigger 与 `DimensionPortal` 必须同节点。`MineEntrance_Summoner` 虽复制入口组件，也必须由建筑角色检查拒绝交互。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-06：正式 Player 根实体碰撞体固定使用 Layer 10 的 `Player` 物理层；Physics 2D 仅关闭 Player↔Player，玩家仍与地图、建筑、怪物和伤害层碰撞，单机与联机核心角色共用该规则。
- 2026-08-04：GM Buff 分发使用本地玩家 `GameController` 的左键事件选择世界目标；只处理带 `BuffManager` 的对象，避免向不兼容对象运行时注入模块。
- 2026-07-31：正式矿坑交互传递入口 Item GUID；只有已安装 `MineEntrance` 可进入矿洞，`CaveExit` 返回绑定地表入口，Summoner 交互会被拒绝。
- 2026-07-31：手柄 Binding 与双 Control Scheme 固化进输入资产；绑定服务新增键鼠/手柄分页、Button/Vector2 重绑、同设备冲突检测和分页恢复默认，并保留设备切换事件与可嵌套玩法输入锁。
- 2026-07-31：玩家交互链接入 `DimensionPortal`；跨维度时保存每世界位置、安全释放旧玩家，并在目标动态世界重建后恢复位置。
- 2026-07-30：玩家 `Act()` 改为显式安全空行为；`GameController` 明确不负责按键持久化，继续由 `InputBindingService` 独立管理。
- 2026-07-29：Player Prefab 接入本地聊天输入；聊天期间暂停玩法输入，管理员传送由裸 T 改为 Ctrl+T，并隔离远程 Player。
- 2026-07-29：玩家步行/奔跑耐力消耗改走 `Mod_Stamina.AddStamina()`，与攻击和食物恢复共享难度倍率入口。
- 2026-07-28：Player 增加本地/新建档案运行时上下文；Player Prefab 根节点接入 `NewPlayerGuideController`，远程副本不获得教程或本地自言自语资格。
- 2026-07-27：输入中心同时支持键鼠与手柄虚拟光标，业务模块应从 `GameController` 获取统一指针世界坐标。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/PlayerInteraction/PlayerInteractionSmokeTests.cs`；当前覆盖玩家实体、输入控制器、绑定服务、玩家 Prefab、Player 物理层与自碰撞屏蔽、双 Control Scheme、关键手柄 Binding、键鼠/手柄分页条目、Vector2 手柄控制和分页恢复默认隔离。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；玩家交互测试约定目录：`Assets/GameTest/PlayerInteraction/`；场景目录：`Assets/GameTest/Scenes/PlayerInteraction/`；冒烟分类：`PlayerInteraction.Smoke`。
- 新增输入、移动、摄像机、焦点、交互发送接收或玩家 Prefab 行为时必须增加系统测试；修复 Bug 时先增加回归测试。输入到移动或交互主流程变化时同步更新玩家冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；输入测试必须使用可注入输入，不能依赖真实鼠标、键盘或手柄操作。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category PlayerInteraction.Smoke`；无需视觉模型或测试工具卡片。涉及 UI、Item/Module、建筑、地图或联机玩家时追加对应分类；只有光标、相机或交互反馈最终观感变化才做定向截图。
- Player 教程资格、Prefab 接线与远程隔离由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 玩家聊天的本地资格、输入锁恢复、Prefab 接线与按键冲突由 `Assets/GameTest/Dialogue/PlayerChatSmokeTests.cs` 覆盖。
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
