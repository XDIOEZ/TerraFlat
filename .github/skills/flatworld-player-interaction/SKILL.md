---
name: flatworld-player-interaction
description: "Use when: 定位或修改 FlatWorld 的玩家实体、输入系统、鼠标/手柄、虚拟光标、移动、摄像机、焦点、交互发送接收、管理员控制或玩家 Prefab。关键词：Player、GameController、InputBindingService、Mod_InteractSender。"
argument-hint: "玩家输入、移动或交互问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 玩家、输入与交互定位

> 最后核对：2026-07-29。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Item/Player.cs`：玩家 Item 实体。
2. `Assets/5_Scripts/5-1_Data/ItemData/Data_Player.cs`：玩家持久化数据。
3. `Assets/5_Scripts/5-3_GamePlay/Controller/GameController.cs`：输入动作、鼠标/手柄、虚拟光标、输入锁定。
4. `Assets/5_Scripts/5-3_GamePlay/Controller/InputBindingService.cs`：输入绑定服务。

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

## 调用边界

```text
Input System / PlayerInputActions
→ GameController
→ 点击、移动、交互或模块事件
→ 玩家 Item 上的 Inventory / Skill / Building / Hand 等模块
```

- UI 上方点击由 `GameController.IsPointerOverUI()` 拦截。
- 濒死、过场或联机准备期间使用输入锁定，不要通过禁用整个玩家对象规避输入。
- 玩家运行时引用优先从 `ItemMgr.User_Player` / `UserPlayerTransform` 获取，兼容单机与联机本地玩家。
- `Player.IsLocalProfile`、`IsNewProfile`、`WasProfileDataCreated` 与 `ProfileContextChanged` 仅为运行时档案上下文，不进入 `Data_Player` 序列化布局；新玩家判定来自数据是否创建，禁止用出生位置或本地控制权猜测。
- 聊天按键契约：裸 `T` 打开聊天，`Enter` 提交，`Esc` 取消；打开期间同时设置 `GameController.SetGameplayInputLocked(true)` 并挂起 `InputBindingService` 的 Win10 Action Map，关闭时恢复之前的锁状态。
- 管理员传送使用 `Ctrl+T`，且管理员快捷键尊重 `IsGameplayInputLocked`；聊天控制器忽略带 Ctrl 的 T，避免同时打开聊天和传送。
- 玩家移动耐力不得直接修改 `Mod_Stamina.CurrentValue`；`Mover` 统一调用 `AddStamina()`，由该入口应用自定义难度的耐力消耗/恢复倍率。

## 近期变更

- 2026-07-29：Player Prefab 接入本地聊天输入；聊天期间暂停玩法输入，管理员传送由裸 T 改为 Ctrl+T，并隔离远程 Player。
- 2026-07-29：玩家步行/奔跑耐力消耗改走 `Mod_Stamina.AddStamina()`，与攻击和食物恢复共享难度倍率入口。
- 2026-07-28：Player 增加本地/新建档案运行时上下文；Player Prefab 根节点接入 `NewPlayerGuideController`，远程副本不获得教程或本地自言自语资格。
- 2026-07-27：输入中心同时支持键鼠与手柄虚拟光标，业务模块应从 `GameController` 获取统一指针世界坐标。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/PlayerInteraction/PlayerInteractionSmokeTests.cs`；当前基础覆盖玩家实体、输入控制器、绑定服务与玩家 Prefab 入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；玩家交互测试约定目录：`Assets/GameTest/PlayerInteraction/`；场景目录：`Assets/GameTest/Scenes/PlayerInteraction/`；冒烟分类：`PlayerInteraction.Smoke`。
- 新增输入、移动、摄像机、焦点、交互发送接收或玩家 Prefab 行为时必须增加系统测试；修复 Bug 时先增加回归测试。输入到移动或交互主流程变化时同步更新玩家冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；输入测试必须使用可注入输入，不能依赖真实鼠标、键盘或手柄操作。
- 完成修改后检查 Unity 编译和 Console，再运行 `PlayerInteraction.Smoke`；涉及 UI、Item/Module、建筑、地图或联机玩家时同步运行对应系统测试。
- Player 教程资格、Prefab 接线与远程隔离由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 玩家聊天的本地资格、输入锁恢复、Prefab 接线与按键冲突由 `Assets/GameTest/Dialogue/PlayerChatSmokeTests.cs` 覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变 Input Action 名称、玩家 Prefab、控制器模块、交互协议、摄像机/焦点路径或本地玩家解析方式后，必须更新本 Skill；影响 UI、网络或 Item 注册时同步更新对应 Skill。
