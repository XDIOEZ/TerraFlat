---
name: flatworld-networking
description: "Use when: 定位或修改 FlatWorld 的 Mirror/KCP 联机启动、主机客户端、会话抽象、网络玩家、世界快照、Chunk 流送、Item 状态同步、建筑网络事务、联机 UI 或测试场景。关键词：NetworkGameBootstrap、FlatWorldGameNetworkManager、GameNetwork。"
argument-hint: "联机、同步或网络 UI 问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 联机系统定位

> 最后核对：2026-07-27。修改时严格区分本地权威实体与远程视觉副本。

## 修改前先读

1. `Assets/5_Scripts/5-4_Networking/Gameplay/NetworkGameBootstrap.cs`：主菜单场景自动装配 Mirror + KCP。
2. `Assets/5_Scripts/5-4_Networking/Gameplay/FlatWorldGameNetworkManager.cs`：正式网络管理器与世界准备。
3. `Assets/5_Scripts/5-4_Networking/Core/GameNetwork.cs`：网络会话静态入口。
4. `Assets/5_Scripts/5-4_Networking/Gameplay/NetworkWorldPlayer.cs`：网络玩家实体。

## 分层

- Core 抽象：`Assets/5_Scripts/5-4_Networking/Core/`
  - `INetworkSession.cs`、`INetworkEntityContext.cs`、`NetworkRole.cs`、`NetworkSessionState.cs`、`NetworkStartResult.cs`。
- Mirror 适配：`Assets/5_Scripts/5-4_Networking/Mirror/`
  - `FlatWorldNetworkManager.cs`、`MirrorNetworkEntityContext.cs`。
- Gameplay：`Assets/5_Scripts/5-4_Networking/Gameplay/`。

## 同步链路

- 世界快照：`SaveDataMgr.CreateCompressedNetworkSnapshot()` / `ApplyCompressedNetworkSnapshot()`。
- Chunk 流送：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkChunkStreamingCoordinator.cs`。
- Item 状态：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkItemStateCoordinator.cs`。
- Item 序列化桥：`Assets/5_Scripts/5-3_GamePlay/Item/ItemNetworkStateSerialization.cs`。
- 玩家视觉：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkPlayerVisualState.cs`。
- 消息定义：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkGameMessages.cs`。

## 联机 UI

- 会话控制：`NetworkModeUIController.cs`。
- UI 状态：`NetworkModeUIController.UI.cs`。
- 动态视觉树：`NetworkModePanelView.cs`，实际类型为 `NetworkModeUIController` partial。

## 资源与测试

- 网络玩家 Prefab：`Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab`。
- 测试脚本：`Assets/5_Scripts/5-4_Networking/Tests/`。
- 测试 Prefab：`Assets/2_Prefabs/NetworkingTest/`。
- 测试场景：`Assets/3_Scenes/NetworkTest.unity`。

## 权威边界

- 远程视觉副本不得进入本地 Item Tick、AI 感知或本地存档索引。
- 客户端不重复结算伤害、死亡、建筑放置或世界生成；应用服务端权威结果。
- 局部导航图跟随本地 owned 玩家；Chunk 流送按所有观察者并集。
- `NetworkGameBootstrap` 从 `Resources/Networking/FlatWorldNetworkPlayer` 加载 Prefab；移动后同步常量与本 Skill。

## 近期变更

- 2026-07-27：联机 UI 使用 `NetworkModeUIController` 三个 partial 文件分离会话、UI 状态和动态视觉树。
- 2026-07-27：本地导航窗口跟随 owned 玩家；远程副本继续排除出本地 Tick/感知/存档。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Networking/NetworkingSmokeTests.cs`；当前基础覆盖网络启动器、正式管理器、玩家 Prefab 与网络测试场景入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；网络测试约定目录：`Assets/GameTest/Networking/`；场景目录：`Assets/GameTest/Scenes/Networking/`；冒烟分类：`Networking.Smoke`。现有独立进程 Harness 位于 `Assets/5_Scripts/5-4_Networking/Tests/`，不得重复实现。
- 新增 Host/Client、会话、网络玩家、世界快照、Chunk、Item 或建筑同步行为时必须增加系统测试；修复 Bug 时先增加回归测试。核心连接与同步流程变化时同步更新网络测试场景和现有 Harness。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；端口、临时存档和生成进程必须隔离，测试结束必须关闭测试实例并清理状态。
- 完成修改后检查 Unity 编译和 Console，再运行 `Networking.Smoke`；涉及核心生命周期、玩家、地图、Item/Module、建筑或存档时同步运行对应系统测试。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变网络入口、端口/传输、Prefab 路径、消息、会话状态、玩家权威边界、Chunk/Item 同步、建筑事务、MOD 校验或联机 UI 文件后，必须更新本 Skill；同时更新受影响的 Item、Map、Navigation、UI 或 Modding Skill。
