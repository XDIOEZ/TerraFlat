---
name: flatworld-networking
description: "Use when: 定位或修改 FlatWorld 的 Mirror/KCP 联机启动、主机客户端、会话抽象、网络玩家、世界快照、Chunk 流送、Item 状态同步、建筑网络事务、联机 UI 或测试场景。关键词：NetworkGameBootstrap、FlatWorldGameNetworkManager、GameNetwork。"
argument-hint: "联机、同步或网络 UI 问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 联机系统定位

> 最后核对：2026-08-07。修改时严格区分本地权威实体与远程视觉副本。

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
- 天气状态：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkWeatherStateCoordinator.cs`。
- Item 序列化桥：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/ItemNetworkStateSerialization.cs`。
- 玩家视觉：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkPlayerVisualState.cs`。
- 消息定义：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkGameMessages.cs`。
- 当前协议：`NetworkGameplayProtocol.CurrentVersion=10`、`NetworkMapGenerationProtocol.CurrentVersion=4`。

## 联机 UI

- 会话控制：`NetworkModeUIController.cs`。
- UI 状态：`NetworkModeUIController.UI.cs`。
- Prefab 加载：`NetworkModePanelView.cs`，实际类型为 `NetworkModeUIController` partial；运行时只调用 `GameRes.InstantiatePrefab("UI_NetworkMode")`，不得创建视觉节点。
- 联机面板 Prefab：`Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NetworkMode.prefab`；位于 Addressables 的 `Assets/2_Prefabs` 文件夹条目下，由 `GameRes` 的 `Prefab` 标签预加载。
- Prefab 编辑器重建入口：`Assets/Editor/FlatWorld/PrefabBuilders/UI/NetworkModePrefabBuilder.cs`，菜单 `FlatWorld/UI/Rebuild Network Mode UI`；玩家可直接打开 Prefab 检查和编辑布局。
- 穿透端点解析：`Core/NetworkConnectionEndpoint.cs`；客户端地址支持域名、IPv4、IPv6、`域名:端口`、`kcp://` 与 `udp://`，地址内端口优先于 UI 默认端口。
- 当前传输为 KCP/UDP；内网穿透服务必须建立 UDP 隧道，TCP/HTTP 隧道会在连接前被拒绝。

## 资源与测试

- 网络玩家 Prefab：`Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab`；根下必须预制名为 `玩家名称` 的 `TextMeshPro`，`NetworkWorldPlayer` 只更新文字与颜色，不得运行时创建名称视觉。
- 自动化测试：`Assets/GameTest/Networking/`，统一使用正式联机入口与资源，不再维护独立测试场景和测试 Prefab。

## 权威边界

- 远程视觉副本不得进入本地 Item Tick、AI 感知或本地存档索引。
- `ItemMgr.LoadNetworkPlayer()`、`PromoteNetworkPlayerToLocal()` 与 `ConfigureRemoteNetworkReplica()` 必须显式维护 `Player.SetProfileContext()`；远程副本 `IsLocalProfile=false`，提升成本地时保留原始 `WasProfileDataCreated`。
- Player 自言自语和新手引导都以 `IsLocalProfile` 为硬门；远程副本即使挂载正式 Player Prefab 组件也不得启动调度、贡献有效教程 Facts 或持久化教程进度。
- 客户端不重复结算伤害、死亡、建筑放置或世界生成；应用服务端权威结果。
- 地图生成设置哈希必须包含 `TerrainGenerationSignature`：四阶段生成器顺序、三通道噪声、稳定 BiomeId/范围、河流写法、结构目录版本和生态倍率都必须参与。
- 客户端必须在应用快照前拒绝旧 Gameplay 协议、旧地图生成协议或不同生成设置哈希；不得用自动重建隐藏不兼容。
- 天气事件只由 `GameNetwork.HasStateAuthority` 为真的离线/Host/Server 调度；`NetworkWeatherStateCoordinator` 广播阶段、强度、绝对时间边界、随机游标和事件序号，普通 Client 只调用 `WeatherMgr.ApplyReplicatedWeatherState()`。
- 局部导航图跟随本地 owned 玩家；Chunk 流送按所有观察者并集。
- `NetworkGameBootstrap` 从 `Resources/Networking/FlatWorldNetworkPlayer` 加载 Prefab；移动后同步常量与本 Skill。
- `GamePlay.asmdef` 直接引用无引擎依赖的 `FlatWorld.Networking.Core`；`MonsterSpawnerManager` 使用 `GameNetwork.HasStateAuthority` 门控世界生物生成，客户端不得重复投放。
- 首版 `DimensionManager.TryBeginTransition()` 在 `GameNetwork.IsOnline` 为真时拒绝切换；在实现服务器权威目标地址、观察者迁移、Chunk 流送重建和玩家同步前不得解除此门禁。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；网络面板纯布局或文案变化只交给 UI 验证。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| 会话状态、Host/Client 世界进入或本地/远程 Player 提升 | `flatworld-core`、`flatworld-player-interaction` | 世界生命周期只执行一次，本地档案与远程视觉副本严格隔离 | `Core.Smoke`、`PlayerInteraction.Smoke` |
| 世界快照、Item 状态消息、协议版本或序列化桥 | `flatworld-data-save`、`flatworld-item-module` | 服务端权威状态可往返，客户端不进入本地保存/Tick | `DataSave.Smoke`、`ItemModule.Smoke` |
| Chunk 观察者并集、本地导航窗口或世界坐标同步 | `flatworld-map`、`flatworld-navigation` | Chunk 按全部观察者流送，GridGraph 只跟随本地 owned 玩家 | `Map.Smoke`、`Navigation.Smoke` |
| 建筑放置/拆除请求、accepted/reject 或库存剩余数量 | `flatworld-building` | 服务端校验和提交为唯一权威，拒绝路径无副作用 | `Building.Smoke` |
| MOD 集合哈希、兼容握手或存档 MOD 记录 | `flatworld-modding` | 不兼容集合在加入世界前被拒绝 | `Modding.Smoke` |

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-09：矿洞出口配对指纹纳入既有 `GenerationFingerprint`，因此地图生成协议的设置哈希会同时校验冻结地表 Profile、地表种子和拓扑；联机仍拒绝不同纯生成结果。
- 2026-08-07：删除已被正式游戏联机链路和统一 GameTest 取代的早期胶囊人双进程测试 Harness、`NetworkTest` 场景与测试 Prefab；正式构建仅保留 `GameStartScene`，联机验证统一归入 `Assets/GameTest/Networking/`。
- 2026-08-09：地图生成协议升至 6；`NetworkMapGenerationProtocol.CalculateSettingsHash()` 改纳入完整 `GenerationFingerprint`，覆盖洞穴房间/隧道、矿脉规则和天然传送门参数。Host/Client Profile 不一致时拒绝不同纯生成结果；维度切换本身仍保持离线限制。
- 2026-08-05：Gameplay 协议升到 9，地图生成协议升到 2；快照设置哈希新增三通道噪声、稳定 BiomeId、分阶段管线与结构目录签名，旧协议明确拒绝。

- 2026-07-31：明确维度切换首版仅离线可用，联机会话主动拒绝地表/矿洞迁移；后续需新增完整服务器权威协议后再开放。
- 2026-07-30：联机协议升级到 8，新增天气状态请求与服务器广播；初始世界快照仍携带 PlanetData，进入世界后再请求一次当前权威天气以避免加载期间漏状态。
- 2026-07-29：联机玩家名称固化到 `FlatWorldNetworkPlayer.prefab`；统一 Runtime UI 重建器负责生成该节点，网络运行时只查找并绑定现有 `TextMeshPro`。
- 2026-07-29：联机面板从运行时代码构建改为 `UI_NetworkMode.prefab`；`GameRes` 预加载后实例化，新增编辑器重建菜单并删除运行时视觉树代码。
- 2026-07-29：联机面板支持直接粘贴 UDP 内网穿透完整地址；Core 统一解析端点并校验协议、主机、端口，Mirror 客户端使用解析后的域名/IP 与外部端口连接。
- 2026-07-29：生态生成器接入 `GameNetwork.HasStateAuthority`，离线与 Host/Server 结算，普通客户端只应用权威世界状态。
- 2026-07-28：网络 Player 创建链增加显式本地档案上下文；远程副本隔离自言自语与新手教程，本地提升通过 `ProfileContextChanged` 恢复。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Networking/NetworkingSmokeTests.cs`；联机回归统一在正式游戏程序集与正式资源上验证。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；网络测试约定目录：`Assets/GameTest/Networking/`；场景目录：`Assets/GameTest/Scenes/Networking/`；冒烟分类：`Networking.Smoke`。不得重新引入独立胶囊人测试 Harness、专用测试 Prefab 或正式 Build Settings 中的测试场景。
- 新增 Host/Client、会话、网络玩家、世界快照、Chunk、Item 或建筑同步行为时必须增加系统测试；修复 Bug 时先增加回归测试。核心连接与同步流程变化时同步更新正式场景资源与统一 GameTest。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；端口、临时存档和生成进程必须隔离，测试结束必须关闭测试实例并清理状态。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Networking.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类。
- 远程 Player 教程/自言自语隔离由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变网络入口、端口/传输、Prefab 路径、消息、会话状态、玩家权威边界、Chunk/Item 同步、建筑事务、MOD 校验或联机 UI 文件后，必须更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。

## 有限环绕世界协议契约（2026-08-06）

- Gameplay 协议为 `10`，地图生成协议为 `5`；快照和生成设置 hash 必须包含 `TopologyMode` 与生态配置指纹，旧协议必须拒绝。
- 服务端用环面最短位移校验移动步长并发布规范坐标；远程插值也必须取最短环面目标。
- 多观察者 Chunk 窗口按规范坐标合并去重，销毁距离用最短环面位移；覆盖位于 `Networking.Smoke`。
