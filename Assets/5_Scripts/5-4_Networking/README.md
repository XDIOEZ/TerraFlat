# FlatWorld 联机系统

当前后端为 **Mirror 96.11.0**，默认使用 KCP/UDP 传输。

## 运行入口

- `Gameplay/NetworkGameBootstrap.cs` 在 `GameStartScene` 自动装配正式联机入口。
- `Gameplay/FlatWorldGameNetworkManager.cs` 负责 Host、Client、世界快照与玩家进入流程。
- `Core/GameNetwork.cs` 是游戏系统访问会话状态和权威判断的统一入口。
- `Resources/Networking/FlatWorldNetworkPlayer.prefab` 是正式网络玩家 Prefab。

## 分层约束

- 普通游戏系统只依赖 `FlatWorld.Networking.Core`，不得直接依赖 Mirror。
- Mirror 类型只允许出现在 `Mirror` 与 `Gameplay` 适配层。
- 世界状态修改必须由 `GameNetwork.HasStateAuthority` 门控；离线、Host 与 Server
  拥有状态权威，普通 Client 只应用服务端结果。
- 本地输入使用实体的 `HasInputAuthority` 判断，远程玩家只作为视觉副本。

## 验证

- 检查正式联机入口、状态权威、输入权威和断线清理。
- 游戏运行链路不依赖独立冒烟测试程序集。
