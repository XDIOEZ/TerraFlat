---
name: flatworld-networking
description: "Use when: 定位或修改 FlatWorld 的 Mirror/KCP 联机启动、主机客户端、会话抽象、网络玩家、世界快照、Chunk 流送、Item 状态同步、建筑网络事务、联机 UI 或测试场景。关键词：NetworkGameBootstrap、FlatWorldGameNetworkManager、GameNetwork。"
---

# FlatWorld 联机

## 入口

- 启动/管理：`Assets/5_Scripts/5-4_Networking/Gameplay/{NetworkGameBootstrap,FlatWorldGameNetworkManager,NetworkWorldPlayer}.cs`
- 抽象：`Assets/5_Scripts/5-4_Networking/Core/`；Mirror 适配：`.../Mirror/`
- 会话入口：`Core/GameNetwork.cs`
- 同步：`Gameplay/Network{ChunkStreaming,ItemState,WeatherState}Coordinator.cs`
- 快照：`SaveDataMgr.CreateCompressedNetworkSnapshot/ApplyCompressedNetworkSnapshot`
- UI：`Gameplay/NetworkModeUIController*.cs` 与 `Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NetworkMode.prefab`

## 权威边界

- 服务端结算世界生成、伤害、死亡、建筑与持久状态；客户端只应用权威结果。
- 远程视觉副本不得进入本地 Item Tick、AI 感知、教程/对话或存档索引。
- `LoadNetworkPlayer/Promote.../ConfigureRemote...` 显式维护 Player ProfileContext；只有 owned 玩家驱动本地输入、导航窗口与 HUD。
- 世界/Item 快照必须版本化、可往返；生成指纹或 MOD 集合不兼容时在入世前拒绝。
- Chunk 按观察者并集流送；本地导航窗口仍只跟随 owned 玩家。
- 网络 UI 从正式 Prefab 实例化，不运行时构造；网络玩家名称节点预制在 `Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab`。
- 维度切换当前仅离线；完成服务器权威迁移协议前不得解除。

## 验证

- 隔离端口、进程和临时存档；覆盖 Host/Client、拒绝路径、断线清理、消息往返和无重复结算。
- 按改动联动 Core/Player、Data/Item、Map/Navigation、Building 或 Modding。
- 默认不主动跑测试；需要时运行 `Networking.Smoke`。测试入口：`Assets/GameTest/Networking/NetworkingSmokeTests.cs`；不得恢复独立测试 Harness/Prefab/Build 场景。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
