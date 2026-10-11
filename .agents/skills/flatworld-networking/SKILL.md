---
name: flatworld-networking
description: "FlatWorld 的 Mirror/KCP 会话、玩家权威、世界快照、区块与物品同步。"
---

# FlatWorld 联机

## 入口

代码根目录：`Assets/5_Scripts/5-4_Networking/`。
- `Core/GameNetwork.cs`：会话门面；`Core/` 为传输抽象，`Mirror/` 为适配。
- `Gameplay/{NetworkGameBootstrap,FlatWorldGameNetworkManager,NetworkWorldPlayer}.cs`：启动、连接与玩家。
- `Gameplay/Network{ChunkStreaming,ItemState,WeatherState}Coordinator.cs`：区块、物品、天气复制。
- `SaveDataMgr.CreateCompressedNetworkSnapshot/ApplyCompressedNetworkSnapshot`：入世快照。
- `Gameplay/NetworkModeUIController*.cs`：面板；资源为 `Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NetworkMode.prefab`、`Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab`。

## 权威与坐标

- 服务端结算生成、伤害、死亡、建筑及持久状态；远程视觉副本不进入本地 Item Tick、AI 感知、教程、对话或存档索引。只有 owned 玩家驱动本地输入、导航窗口和 HUD；玩家加载、提升和远程配置显式设置 ProfileContext。
- 协议、存档和权威判定使用规范逻辑坐标。`NetworkWorldPlayer.AuthoritativeLogicalPosition` 是服务端玩家位置；Host 中远端 Transform 可能已是本机镜像。`WorldLocalPresentation` 在各客户端选择 owned 玩家附近的表现镜像，远端插值先保存逻辑目标。区块流送消费 `ObserverLogicalPosition`，按观察者并集加载。
- 建造消息校验连接与真实快捷栏材料，`BuildingPlacementService` 处理放置、扣料和回滚；缺少网络发布器时事务回滚。Host 确认回调不重复发布建造进度。
- 区域天气、雨雪反馈由权威端推进；客户端按 WorldKey、规范地址及修订号复制，首次雷电同步只建立序号基线。地表批次只传水肥和雪层，复制序号独立于 Terrain.Revision；可靠消息按通道大小分片，待就绪区块缓存最多 256 个地址，淘汰后补拉快照，消息本身不生成地形。
- 维度迁移目前仅离线；联机入世会核对生成指纹和 MOD 集合。

## 快照与复制

- Item 使用 FWI1 实例快照；捕获不改活跃 ItemData、库存或位姿。公开世界捕获上下文为线程局部作用域，不能跨 await。
- 热快照按当前配置合并状态，不迁移模块布局；未变模块保留，非重载/远程白名单通过 `BindSnapshotData` 绑定。`CaptureRuntimeStateBaseline` 在合并前捕获基线，`ApplyMergedRuntimeStates` 只刷新原代际中改变的模块；回调回收或换代后停止旧实例处理。嵌套库存仅为新冷数据补配置。
- `ItemNetworkCaptureCache` 在 ModuleSave 后计算状态指纹，按实例代际、数据引用和捕获上下文隔离；未变模块复用冻结快照，未覆盖的 codec 完整捕获。MOD 可实现 `IModuleInstanceStateFingerprintProvider`；公共 Capture 返回独立缓冲。
- 状态与位姿分别复制：普通世界状态包不带位姿，`NetworkItemPoseMessage` 独立递增版本；出生/完整包带当前姿态版本，客户端拒绝旧位姿。DTO 协议号由 `NetworkGameplayProtocol` 管理。
- 机器走 `MachineWorld` 快照、删除消息和转速增量，不进普通 Item 状态流。完整重同步清空旧节点；拆除先发布召唤器再删节点；客户端库存交互只发命令。

## UI 生命周期

`NetworkModeUIController` 常驻，场景 UI 在 `GameStartScene` 加载及 `UIManager.InteractionSurfaceChanged` 后幂等重绑。联机面板等待完整资源就绪；Prefab 根 CanvasGroup Alpha 为 1、默认关闭交互与射线，运行时以 `BasePanel.InitClosed()` 初始化隐藏。
