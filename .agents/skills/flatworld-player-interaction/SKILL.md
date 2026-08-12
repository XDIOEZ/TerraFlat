---
name: flatworld-player-interaction
description: "Use when: 定位或修改 FlatWorld 的玩家实体、输入系统、鼠标/手柄、虚拟光标、移动、摄像机、焦点、交互发送接收、管理员控制或玩家 Prefab。关键词：Player、GameController、InputBindingService、Mod_InteractSender。"
---

# FlatWorld 玩家、输入与交互定位

> 最后核对：2026-07-31。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/Entities/Item/Player.cs`：玩家 Item 实体。
2. `Assets/5_Scripts/5-1_Data/ItemData/Data_Player.cs`：玩家持久化数据。
3. `Assets/5_Scripts/5-3_GamePlay/Player/Controller/GameController.cs`：输入动作、鼠标/手柄、虚拟光标、输入锁定。
4. `Assets/5_Scripts/5-3_GamePlay/Player/Controller/InputBindingService.cs`：输入绑定服务。

## 关键功能
- 交互发送：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/Mod_InteractSender.cs`。
- 交互接收：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/Mod_InteractReciver.cs`。
- 管理员控制：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/PlayerAdminController.cs`。
- 移动/朝向：`Assets/5_Scripts/5-3_GamePlay/Entities/Move/`。
- 摄像机模块：`Assets/5_Scripts/5-3_GamePlay/Entities/Move/Mod_Cam.cs`。
- 世界焦点：`Assets/5_Scripts/5-3_GamePlay/Entities/Move/Mod_FocusPoint.cs`。

## 调用边界
```text
Input System / PlayerInputActions
→ GameController
→ 点击、移动、交互或模块事件
→ 玩家 Item 上的 Inventory / Skill / Building / Hand 等模块
```

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：`Mod_InteractSender.FreshWaterDrinking` 复用 E/手柄西键的按下与松开；仅持有淡水能力 Buff 时长按 1 秒开始饮水，之后每秒补 25 水，松开、离水或输入锁定立即停止，普通短按交互保持不变。
- 2026-08-12：奔跑 `饥饿2.0` Buff 新增独立 0.5 水分倍率，使奔跑期间水分总消耗减半；奔跑速度、耐力和其他营养消耗保持不变。
- 2026-08-12：TMP 输入框保留手柄焦点框；确认前通过 `GamepadInputFieldNavigationBridge` 允许方向离开，按 A/确认打开虚拟键盘后才进入正式编辑，不再强制取消输入框焦点。
- 2026-08-12：手柄焦点只能停留在当前最上层打开的导航面板；EventSystem 自动导航若命中背景 UI，会在帧末恢复顶层面板焦点，虚拟光标和虚拟键盘模式不受此约束。
- 2026-08-11：奔跑输入拆分为 `ToggleRun` 与 `Shift`（长按）两个可重绑动作；键盘默认 Shift 长按，手柄默认左摇杆按下切换，两个设备页都提供“切换奔跑/长按奔跑”独立槽位。

## 修改后自动测试
- 精简 Smoke：`Assets/GameTest/PlayerInteraction/PlayerWorldWrapSmokeTests.cs`；当前只保留玩家跨四边与角落环绕时速度和数据不丢失这一关键行为。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；玩家交互测试约定目录：`Assets/GameTest/PlayerInteraction/`；场景目录：`Assets/GameTest/Scenes/PlayerInteraction/`；冒烟分类：`PlayerInteraction.Smoke`。
- 新增输入、移动、摄像机、焦点、交互发送接收或玩家 Prefab 行为时必须增加系统测试；修复 Bug 时先增加回归测试。输入到移动或交互主流程变化时同步更新玩家冒烟场景。
- 奔跑与速度过渡回归位于 `Assets/GameTest/PlayerInteraction/MoverRunInputTests.cs`，分类为 `PlayerInteraction.Input`；覆盖切换奔跑、长按奔跑、两种模式共享状态，以及走路→奔跑→走路→松开的平滑速度变化。
- `Assets/GameTest/PlayerInteraction/InputBindingServiceTests.cs` 覆盖单项清除绑定后的空路径、`未绑定` 显示、变更事件和持久化。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；输入测试必须使用可注入输入，不能依赖真实鼠标、键盘或手柄操作。

## 修改后维护本 Skill
改变 Input Action 名称、玩家 Prefab、控制器模块、交互协议、摄像机/焦点路径或本地玩家解析方式后，必须更新本 Skill；影响 UI、网络或 Item 注册时同步更新对应 Skill。

## 玩家环绕契约（2026-08-06）
- `Player.prefab` 根节点挂载 `PlayerWorldWrapController`；仅 `Player.IsLocalProfile` 且当前拓扑为 Wrapped 时处理 Rigidbody2D 越界。

## 玩家物理碰撞契约（2026-08-06）
- `Player.prefab` 根节点及其非 Trigger 实体碰撞体固定使用 Layer 10 的 `Player` 层；不得递归覆盖模块或攻击 Trigger 的专用 Layer。
