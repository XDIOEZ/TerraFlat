---
name: flatworld-networking
description: "Use when: 定位或修改 FlatWorld 的 Mirror/KCP 联机启动、主机客户端、会话抽象、网络玩家、世界快照、Chunk 流送、Item 状态同步、建筑网络事务、联机 UI 或测试场景。关键词：NetworkGameBootstrap、FlatWorldGameNetworkManager、GameNetwork。"
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

## 同步链路
- 世界快照：`SaveDataMgr.CreateCompressedNetworkSnapshot()` / `ApplyCompressedNetworkSnapshot()`。
- Chunk 流送：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkChunkStreamingCoordinator.cs`。
- Item 状态：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkItemStateCoordinator.cs`。
- 天气状态：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkWeatherStateCoordinator.cs`。
- Item 序列化桥：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/ItemNetworkStateSerialization.cs`。
- 玩家视觉：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkPlayerVisualState.cs`。

## 联机 UI
- 会话控制：`NetworkModeUIController.cs`。
- UI 状态：`NetworkModeUIController.UI.cs`。
- Prefab 加载：`NetworkModePanelView.cs`，实际类型为 `NetworkModeUIController` partial；运行时只调用 `GameRes.InstantiatePrefab("UI_NetworkMode")`，不得创建视觉节点。
- 联机面板 Prefab：`Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NetworkMode.prefab`；位于 Addressables 的 `Assets/2_Prefabs` 文件夹条目下，由 `GameRes` 的 `Prefab` 标签预加载。

## 资源与测试
- 网络玩家 Prefab：`Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab`；根下必须预制名为 `玩家名称` 的 `TextMeshPro`，`NetworkWorldPlayer` 只更新文字与颜色，不得运行时创建名称视觉。
- 自动化测试：`Assets/GameTest/Networking/`，统一使用正式联机入口与资源，不再维护独立测试场景和测试 Prefab。

## 权威边界
- 远程视觉副本不得进入本地 Item Tick、AI 感知或本地存档索引。
- `ItemMgr.LoadNetworkPlayer()`、`PromoteNetworkPlayerToLocal()` 与 `ConfigureRemoteNetworkReplica()` 必须显式维护 `Player.SetProfileContext()`；远程副本 `IsLocalProfile=false`，提升成本地时保留原始 `WasProfileDataCreated`。
- Player 自言自语和新手引导都以 `IsLocalProfile` 为硬门；远程副本即使挂载正式 Player Prefab 组件也不得启动调度、贡献有效教程 Facts 或持久化教程进度。
- 客户端不重复结算伤害、死亡、建筑放置或世界生成；应用服务端权威结果。

## 高耦合联动
只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；网络面板纯布局或文案变化只交给 UI 验证。
| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| 会话状态、Host/Client 世界进入或本地/远程 Player 提升 | `flatworld-core`、`flatworld-player-interaction` | 世界生命周期只执行一次，本地档案与远程视觉副本严格隔离 | `Core.Smoke`、`PlayerInteraction.Smoke` |
| 世界快照、Item 状态消息、协议版本或序列化桥 | `flatworld-data-save`、`flatworld-item-module` | 服务端权威状态可往返，客户端不进入本地保存/Tick | `DataSave.Smoke`、`ItemModule.Smoke` |
| Chunk 观察者并集、本地导航窗口或世界坐标同步 | `flatworld-map`、`flatworld-navigation` | Chunk 按全部观察者流送，`WorldNavigationGrid` 的本地窗口只跟随 owned 玩家 | `Map.Smoke`、`Navigation.Smoke` |
| 建筑放置/拆除请求、accepted/reject 或库存剩余数量 | `flatworld-building` | 服务端校验和提交为唯一权威，拒绝路径无副作用 | `Building.Smoke` |
| MOD 集合哈希、兼容握手或存档 MOD 记录 | `flatworld-modding` | 不兼容集合在加入世界前被拒绝 | `Modding.Smoke` |

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-09：矿洞出口配对指纹纳入既有 `GenerationFingerprint`，因此地图生成协议的设置哈希会同时校验冻结地表 Profile、地表种子和拓扑；联机仍拒绝不同纯生成结果。
- 2026-08-07：删除已被正式游戏联机链路和统一 GameTest 取代的早期胶囊人双进程测试 Harness、`NetworkTest` 场景与测试 Prefab；正式构建仅保留 `GameStartScene`，联机验证统一归入 `Assets/GameTest/Networking/`。
- 2026-08-09：地图生成协议升至 6；`NetworkMapGenerationProtocol.CalculateSettingsHash()` 改纳入完整 `GenerationFingerprint`，覆盖洞穴房间/隧道、矿脉规则和天然传送门参数。Host/Client Profile 不一致时拒绝不同纯生成结果；维度切换本身仍保持离线限制。
- 2026-08-05：Gameplay 协议升到 9，地图生成协议升到 2；快照设置哈希新增三通道噪声、稳定 BiomeId、分阶段管线与结构目录签名，旧协议明确拒绝。
- 2026-07-31：明确维度切换首版仅离线可用，联机会话主动拒绝地表/矿洞迁移；后续需新增完整服务器权威协议后再开放。

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/Networking/NetworkingSmokeTests.cs`；联机回归统一在正式游戏程序集与正式资源上验证。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；网络测试约定目录：`Assets/GameTest/Networking/`；场景目录：`Assets/GameTest/Scenes/Networking/`；冒烟分类：`Networking.Smoke`。不得重新引入独立胶囊人测试 Harness、专用测试 Prefab 或正式 Build Settings 中的测试场景。
- 新增 Host/Client、会话、网络玩家、世界快照、Chunk、Item 或建筑同步行为时必须增加系统测试；修复 Bug 时先增加回归测试。核心连接与同步流程变化时同步更新正式场景资源与统一 GameTest。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；端口、临时存档和生成进程必须隔离，测试结束必须关闭测试实例并清理状态。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Networking.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类。
- 远程 Player 教程/自言自语隔离由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。

## 修改后维护本 Skill
改变网络入口、端口/传输、Prefab 路径、消息、会话状态、玩家权威边界、Chunk/Item 同步、建筑事务、MOD 校验或联机 UI 文件后，必须更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。

## 有限环绕世界协议契约（2026-08-06）
- Gameplay 协议为 `10`，地图生成协议为 `5`；快照和生成设置 hash 必须包含 `TopologyMode` 与生态配置指纹，旧协议必须拒绝。
