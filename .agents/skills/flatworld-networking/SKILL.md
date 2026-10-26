---
name: flatworld-networking
description: "FlatWorld 的 Mirror/KCP 会话、玩家权威、世界快照、区块与物品同步。"
---

# FlatWorld 联机

## 入口

- `SpaceNetworkIntent` 的 UsePlayerItem/Attack/HarvestNatural 同时覆盖普通地表；请求身份来自连接，WorldKey/RealmRevision 与迁移门禁拒绝旧世界操作。Natural 收获走真实实体 GUID/位置/距离校验，成功广播最终 ItemData 或 removed 状态，客户端通过正式生态差量和表现接口应用，不能本机重复采集/掉物。

代码根目录：`Assets/5_Scripts/5-4_Networking/`。
- `Core/GameNetwork.cs`：会话门面；`Core/` 为传输抽象，`Mirror/` 为适配。
- `Core/NetworkConnectionEndpoint.cs`：原始地址明确填写的端口必须在 1～65535；只在缺省时使用备用端口。IPv6 方括号内的冒号不是端口分隔符，不用 `Uri.IsDefaultPort` 判断用户是否显式填写端口。
- `Gameplay/{NetworkGameBootstrap,FlatWorldGameNetworkManager,NetworkWorldPlayer}.cs`：启动、连接与玩家。
- `Gameplay/Network{ChunkStreaming,ItemState,WeatherState}Coordinator.cs`：区块、物品、天气复制。
- `Gameplay/NetworkSpaceStateCoordinator*.cs/NetworkWorldPlayer.Space.cs/NetworkSpaceInterestManagement.cs`：太空输入、限额分片快照、真实距离观察和投射物表现；Gameplay 协议为 26。
- `SaveDataMgr.CreateCompressedNetworkSnapshot/ApplyCompressedNetworkSnapshot`：入世快照。
- `Gameplay/NetworkModeUIController*.cs`：面板；资源为 `Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NetworkMode.prefab`、`Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab`。
- 假人回归：`Gameplay/Testing/GameplayTestNetworkBots*.cs` 和 `GameplayTestBotTransport.cs`，Lua 入口 `GameplayTests/Systems/Networking/gameplay-suite.json`；同进程独立连接走正式 Mirror 收发和服务端规则，覆盖入世/身份/输入/断线重连/拾取租约，不代表真实 Client 库存、Host 本地接管或 LAN 通过。

## 权威与坐标

- `NetworkChunkStreamingCoordinator` 将观察者并集交给正式 WorldModel 窗口；外管中的 `Mod_ChunkLoader` 只发刷新请求，本机锚点决定蒙版和导航，失去观察者只撤网络窗口，不清已交回单人的窗口。
- 本机观察窗口与雾窗共用本机预测逻辑位置，远端观察者仍取服务器位置；预测非法时回退权威位置。建造/拆除通过世界门禁拒绝时立即发送同 token 的真实原因回执，迁移、未就绪或过期代际不得静默等到超时。规则 `networking/local_observer_window、world_request_gate`。
- 区块窗口签名与真实 `GameplayWorldRuntime` 实例共同校验：同名世界重建要重新提交窗口；无观察者的世界在准备完成后清理缓存。本地导航锚点按对应 `WorldNavigationManager` 实例缓存，离开地表后回到同一格也需刷新。

- 服务端结算生成、伤害、死亡、建筑及持久状态；远程视觉副本不进入本地 Item Tick、AI 感知、教程、对话或存档索引。只有 owned 玩家驱动本地输入、导航窗口和 HUD；玩家加载、提升和远程配置显式设置 ProfileContext。
- 地表翻滚经 `NetworkWorldPlayer.Roll.cs` 请求服务器，校验角色状态、冷却与体力后才启用无敌；远端服务器只推进动作态，不获取 Mover 物理租约或解除远端移动锁。owned 收到许可后推进剩余移动，其他副本按同一 double 时标旋转与恢复碰撞；网络动作序号在 NetworkIdentity 内保持递增。扣费交接期间保护 Stamina 模块，确认 applied 回执后恢复既有地表状态流；普通地表生命、体力、营养、氧气与环境模块同样由服务器持续结算，客户端输入/动画/HUD保留本机驱动。
- 出生集中检查当前连接引用、认证、已通过版本/MOD 的 Join 和本连接的 WorldReady；Mirror AddPlayerMessage 不能提前出生。远端名字分配同时避开现有 Player_DIC 档案，本地主机可以接管自己的离线角色。
- 断线清理须核对连接对象身份：复用相同 connectionId 后，旧连接的延迟回调不得删除新连接握手、出生预约或太空输入/攻击状态。
- 协议、存档和权威判定使用规范逻辑坐标。`NetworkWorldPlayer.AuthoritativeLogicalPosition` 是服务端玩家位置；Host 中远端 Transform 可能已是本机镜像。`WorldLocalPresentation` 在各客户端选择 owned 玩家附近的表现镜像，远端插值先保存逻辑目标。区块流送消费 `ObserverLogicalPosition`，按观察者并集加载。
- GM 地表 TP 经 `PlayerTeleportService → NetworkWorldPlayer.Teleport` 的连接所属命令与回执；服务器同时重定位角色、乘员和权威坐标，本机等回执期间停发旧位置，确认后重置速度采样/插值并立即刷新观察窗口。普通移动保留单步限幅，不按显示名识别 GM；太空传送仍沿用原限制。
- 本机动画只读自身移动状态，`ApplyRemoteVisualState` 拒绝 owned 角色；迁移就绪先刷新正式观察者再核对世界与代际，防止 Host 同帧重生确认被旧代际吞掉。回归并入 `networking/bots_teleport`。
- GM 地表 TP 与翻滚 Command 均携带玩家 `RealmRevision` 并在服务端处理前拒绝旧代际/迁移中请求；拒绝时回执释放客户端待确认动作，不消耗该请求序号。远端 TP 的坐标归一化、模块位置及观察者更新必须处于连接所属 `NetworkGameplayWorldScope`，Mirror 假人命令签名同步更新。
- 建造消息校验连接与真实快捷栏材料，`BuildingPlacementService` 处理放置、扣料和回滚；缺少网络发布器时事务回滚。Host 确认回调不重复发布建造进度。
- 放置结果显式携带 Item、Machine、TileCell 或 ShipId；平台/地板/格子墙走 `NetworkItemStateCoordinator.Terrain`，船上部件交正式船体快照，不能把无 Item 的成功结果误判为缺发布器。`SaveDataMgr` 的核心地形和支撑记录只通知脏格，事务结束后捕获最终状态，失败回滚不广播幽灵候选。
- 地形协议按 WorldKey/Dimension/RealmRevision 隔离，可靠批次最多64格；客户端每格修订号防旧快照覆盖新差量，完整补拉按请求 token 和唯一格计数。新/重载区块与世界代际变化必须补拉，未就绪消息不生成或常驻缓存区块；Host 不回写共享地形。核心地形、损伤、草与独立支撑同步，不覆盖液体、天气或农业层。规则 `networking/terrain_replication、terrain_wire`；编辑器外 `python Tools/Tests/TerrainNetworking/verify.py`，不代替 Mirror Weaver 和真实双端验收。
- 区域天气、雨雪反馈由权威端推进；客户端按 WorldKey、规范地址及修订号复制，首次雷电同步只建立序号基线。地表批次只传水肥和雪层，复制序号独立于 Terrain.Revision；可靠消息按通道大小分片，待就绪区块缓存最多 256 个地址，淘汰后补拉快照，消息本身不生成地形。
- 单人也是正式私有Host；本机连接不启动KCP listener，公开房间才按所选端口启动监听，关闭公开只断远端并停止监听。每玩家独立维度/重生走共享权威服务、目标9区块准备、限额分片世界资料、RealmRevision与presentation-ready；入世核对生成指纹和MOD集合。
- 太空用 `SpaceSectorPosition` 的编号+区内 double 米坐标，观察、拾取、机器和攻击按规范距离判断，跨边界不按 Scene 名隔离。迁移只切本机呈现，不用 `ServerChangeScene` 停掉其它玩家；`RealmRevision` 使同地表复活也重定位。
- 太空船与乘员表现共用 SpaceSession.Presentation 的样本和进度；客户端按本地收包时钟插值，NetworkWorldPlayer 在 PresentationSynchronized 后只同步显示坐标，普通 Update 只推进一次朝向/动画。支撑乘员的太空归属读船体 Flight，不能仅用 IsInSpace；显示坐标不写回权威位置。
- 太空桥只接受连接绑定的 profile 与输入/意图；服务器玩家加载结算和真实库存，客户端只提交选中槽位，生命、体力和数量由服务器 `authoritativeItemState` 回传。断线清输入及攻击；地表保留原复制流程，普通地表物品包不发太空观察者，返回地表补拉。
- 普通地表和太空统一服务器武器/Use结算：起始同步真实库存基线，意图只带瞄准与选槽；结算期间整理库存/制作按忙碌拒绝。玩家生存模块以IPlayerStateAuthorityModule声明服务器结算，客户端只合并权威状态并呈现；武器停下后以已发送快照hash回执结束库存交接，生存状态流继续推进。
- 地表投射物姿态带 WorldKey、地表坐标与弧线表现；服务器不在该世界时只查询该世界船体，不借当前场景物理造成串世界伤害。船体高度与空地攻击资格读权威 Flight，不能由客户端 Collider/排序层反推。

## 快照与复制

- `NetworkGameplayProtocol.CurrentVersion=30`，天气地表雪格同时携带 `IceMeltProgress`；客户端仅恢复权威进度，不执行融冰。

- 玩家公开快照经 `Item.ModuleSavePublicNetworkState` 调用模块保存：实现 `IPublicNetworkStateSaver` 的模块仅刷新公开字段，未实现的模块仍执行完整保存。`Mod_Inventory` 跳过私有槽位保存和装备收纳袋拆装；正式存档仍走完整 `ModuleSave`。回归 `networking/public_network_save_route`。

- 私有 Host 无远端 Ready 连接时不做周期性世界物品捕获或地形差量组包；新远端入世仍由完整物品扫描与区块请求补齐。Host 不向自己请求地形快照；物品捕获、待应用状态、生成候选、扫描及地形阶段有 `FlatWorld.Network.Items.*` Profiler Marker。
- Host 本机玩家的脏状态与定时回退共用 `NetworkWorldPlayer.PublishServerPlayerState`，客户端提交入口只供远端拥有者；强制事务发布立即执行。常规发布在本帧动作完成后执行，捕获时借连接所属世界，捕获中新产生的脏标记不能清掉。发送端只读借用 `CaptureCachedNetworkPayload`，不能修改旧版本缓冲；`FlatWorld.Network.Player.*` 固定 Marker 不依赖统计开关。
- 地表物品/机器/容器/建造请求带RealmRevision，连接所属world作用域和目标Item场景双重校验；旧epoch请求不能在新维度同坐标执行。目标世界快照仅合入指定Planet/差量/生态/机器/散物，不替换活跃SaveData根或PlayerData引用。
- Item 使用 FWI1 实例快照；捕获不改活跃 ItemData、库存或位姿。公开世界捕获上下文为线程局部作用域，不能跨 await。
- 地表/太空拾取由 `NetworkItemStateCoordinator.Pickup` 统一预留：客户端只读计划，服务器真实入包后按实际原份数扣同一来源；余量保留，Host 不重复入包。回执按连接引用、请求号和租约令牌去重，客户端成功合并库存才确认；确认前拒绝旧全量库存包，满包预检不申请租约，非库存生存状态继续复制。未提交超时/断线释放来源，已结算只重发回执，不重新入包。门禁经通用网络库存事务查询桥进入原生操作，测试夹具只单向调用正式消息。
- 热快照按当前配置合并状态，不迁移模块布局；未变模块保留，非重载/远程白名单通过 `BindSnapshotData` 绑定。`CaptureRuntimeStateBaseline` 在合并前捕获基线，`ApplyMergedRuntimeStates` 只刷新原代际中改变的模块；回调回收或换代后停止旧实例处理。嵌套库存仅为新冷数据补配置。
- `ItemNetworkCaptureCache` 在 ModuleSave 后计算状态指纹，按实例代际、数据引用和捕获上下文隔离；`Data_Player` 与 `PlayerInstanceSnapshot` 逐字段对应，公开和私有库存按正式快照边界区分。未变模块复用冻结快照，未知派生类型/codec 完整捕获；MOD 可实现 `IModuleInstanceStateFingerprintProvider`，公共 Capture 返回独立缓冲。热栏保存仍先调用手持 ModuleSave，仅在完整库存指纹、源/模块/代际和现有字节内容均未变时复用二进制载荷，加载/卸载或 codec 变更失效。
- 状态与位姿分别复制：普通世界状态包不带位姿，`NetworkItemPoseMessage` 独立递增版本；出生/完整包带当前姿态版本，客户端拒绝旧位姿。DTO 协议号由 `NetworkGameplayProtocol` 管理。
- 天气雷击复制同时携带最新 LightningCell、LightningStrength 和序号，客户端只播放同落点的视觉/雷声，不重算落点或伤害；初始同步只建基线。
- 机器走 `MachineWorld` 快照、删除消息和转速增量，不进普通 Item 状态流。完整重同步清空旧节点；拆除先发布召唤器再删节点；客户端库存交互只发命令。
- 通用机器操作沿现有响应包回传 token、实体、操作、参数与真实结果，UI按本次请求匹配；世界门禁拒绝立即带原因回执，8秒超时或断连只报未确认，库存回执排序保留原语义。
- 机器快速转移成功回执携带同次源/目标库存快照及最多 256 个精确收货槽/数量/目标 GUID，客户端仅匹配未超时 token、Accepted 且两端快照成功应用后发布动画反馈；不从加工/其他玩家引起的数量增量猜收货，远端请求服务端执行范围抑制 Host 本地动画。新增消息字段须同步提升 Gameplay 协议。

## UI 生命周期

玩家头顶名字由 `NetworkWorldPlayer.ApplyDisplayName` 按 `isLocalPlayer` 控制，仅显示其他玩家；本地玩家启动回调再次刷新，改名和角色重建沿用同一规则。

菜单新游戏与读档均在世界/角色Ready后自动建立私有Host；工具隔离测试用 `NetworkGameBootstrap.SuppressAutomaticSession()` 显式租约阻止自动启动，不能停止用户现有会话。主菜单只加入，游戏内设置经 `GameNetwork.SettingsRequested` 打开同一个常驻面板。`PrepareHostWorld` 只接管已就绪世界与稳定 ProfileId；Host 不回放自身快照或重发玩家进入事件，关闭公开保留同一本机角色和正式联机流送。客户端经 `EnterWorldWithExternalPlayer` 复用正式进入流程；退出使进入代次失效并停网络，客户端不落盘主机存档。

`NetworkModeUIController` 和面板挂常驻根，在场景加载及 `UIManager.InteractionSurfaceChanged` 后幂等重绑。`RuntimeUIPrefabKeys.NetworkMode` 必须进入 `GameRes.StartupPrefabKeys`，联机面板和入口只等 `IsStartupReady`；公开/加入按钮仍等完整 `isLoadFinish`。Prefab 根 CanvasGroup Alpha 为 1、默认关闭交互与射线，运行时以 `BasePanel.InitClosed()` 初始化隐藏。
