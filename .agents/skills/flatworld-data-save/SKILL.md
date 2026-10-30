---
name: flatworld-data-save
description: "FlatWorld 实例快照、核心存档、区块差量、资源配置与 Addressables 所有权。"
---

# 数据与存档

- 存档/角色改名的 `out reason` 由存档服务按真实校验与写盘阶段给出，UI原样呈现；资源重载结果区分目录发布前后，保留实际错误而不一概宣称旧资源仍在运行。
- 正式存档为 SQLite v1；`SaveDataMgr.SaveData` 换根时装配业务规则并隔离旧存储会话，规则装配失败保留旧根。开发期 `.bytes` 不迁移且保留原文件；Compact/FWD6 仅保留内部冻结载荷测试入口，不再走磁盘或首次联机。

## 代码入口

代码根为 `Assets/5_Scripts/5-3_GamePlay/`：

| 目录 | 入口与职责 |
| --- | --- |
| `Core/Save/` | SaveDataMgr*、AutoSaveController：采集、差量与磁盘写入 |
| `World/Map/Data/` | GameSaveData*、MapSave、PlanetData、EcologyWorldSaveData：根档案 |
| `World/Fluids/Core/`、`Atmosphere/` | FluidState、AtmosphereState、PlanetData.Atmosphere：两相库存/大气 |
| `World/Space/`、`World/Machines/` | SpaceSessionState、MachineArchive：太空与机器独立载荷 |

`SaveDataMgr.cs` 协调生命周期与保存入口；`DiskStorage` 管目录/名称/删除与请求修订；`SqliteArchive/SqliteSpaceRecords` 管游戏记录映射、会话、缓存与联机快照。`Shared/Persistence` 的 `ISaveArchiveStore` 隔离 SQLite 驱动；`SnapshotPayload` 管分帧与联合冻结。地形、农业、生态、建筑/AI 保留各自差量 partial，测试 partial 留在 `Development/Testing`。

Item/Module 数据在 `Assets/5_Scripts/5-1_Data/{ItemData,ModData}/`；内容在 `Assets/StreamingAssets/GameConfig/`，Addressables 在 `Assets/AddressableAssetsData/`。

## 快照与磁盘边界

- `GameManager.SaveGame` 为设置按钮和帐篷共用的手动保存入口：主线程立即采集一致快照、后台 SQLite 事务提交，保存状态等待真实任务结果；自动保存单独走 `SaveGameInBackgroundCoroutine` 的分帧预算。睡眠先记重生点并保存，再应用临时时间倍率。

- 每档固定为 `persistentDataPath/Saves/LocalSaveData/<ArchiveId>/save.sqlite`；显示名、UTC 时间只作元数据，改名不换目录，复制通过 SQLite backup 重分配身份。全局、世界、区块、天气区、实体/作用域使用独立带 schema 的记录；地形与生态合成一条 chunk，机器/掉落与太空集合按原 GUID 分行。启动不枚举远区 chunk/weather，实际后台模拟数据仍由机器、掉落、太空系统持有。
- ItemData 的持久化边界是 InstanceSnapshot，JSON/Unity 字段是配置外壳。冷数据不查 GameRes；入世/入包先按当前定义恢复重量/标签/耐久/模块，再合并稳定模块实例状态，未知扩展负载保留。
- ItemInstanceDataFactory 冷编译共享配置/默认状态，CloneRuntime 创建独立运行态，不经 MemoryPack 往返；同 GUID 跨槽保留 ItemData 引用，分堆分配新 GUID。活跃库存只 rebase 新冷物品，专用二进制库存由持有模块显式逐槽处理。
- InventoryInstanceSnapshot 只保存内容/用户状态，恢复保留当前槽标签、UI、输入。不透明 BitData 的配置/进度由模块区分；IItemInstanceStateRestorer 在模块状态恢复后恢复派生属性。
- 外层 MemoryPack 写入期间的嵌套负载使用 ItemSnapshotSerialization.SerializePayload 独占缓冲；IModuleInstanceStateCodec 同样隔离收发缓冲。FastClonerIgnore 的可写属性会被 setter 写默认值，InstanceSnapshot 计算属性不使用该标记。
- CraftedDurabilityMultiplier 保存实例品质；恢复按当前基础耐久重算，再保持损伤比例。IRuntimeDataLifecycle.Save 只捕获，Unload 才清事件/资源。
- 自动保存共用 AutoSaveWorkBudget（0.5ms/16 单元/让两帧）；地形先分帧冻结并记 `chunkDifferenceRevisions`，最终点补冻变化块。玩家时钟、库存、机器、船体、掉落及生态最终成员同帧捕获；之后只封装冻结字节，不再读 live Item。新写入口必须标记差量版本，捕获器不得在后台调用 Item codec/Lua/Unity。新保存意图和最终快照均登记修订，旧任务不能覆盖新提交。
- `PlayerData_Dict` 用不可变角色 ID，`Name_User` 和存档名称仅显示；名称不参与磁盘路径。改名、复制、删除和时间读取统一用存档服务；UI 一次取得目录元数据，逐行不重扫整个目录。
- 生态存档 ItemData 是生产者移交的独立快照，入世前复制；弱引用缓存按存档根/区块跨次复用记录。最终联合点仍重读成员、删除 GUID/恢复年份，仅补冻新增/替换快照和变化元数据；新增生态持久字段须同步 `EcologyFrozenChunkMetadata`。缓存不得用于活跃可变 ItemData；编解码器变更失效，公开角色策略单独捕获。
- 已有角色实例化前必须从 `Data_Player.transform` 取得保存位置，并同步刚体初始位置；不能先以世界原点创建玩家再期望异步物理/模块加载纠正。
- 保存退出先在世界退出事件/输入解绑之前执行 `ItemMgr.SavePlayer`，世界事件结算后再写盘；`SaveDataMgr.Save_And_WriteToDiskAndRecordExitTime` 返回实际成功状态，退出回到菜单不能代替写盘成功。
- 世界退出后写盘只借用已有 `ChunkMgr`；不得通过自动单例捡回待销毁或其他世界的管理器。

## SQLite 事务与按需契约

- 固定 `unity-sqlite-net 1.3.3`，每档一个 worker/连接：WAL、FULL、2MiB page cache、mmap=0、有界队列；直接打开真实数据库文件，不经资产接口载入整库。
- 卸载脏记录先 Stage 到 session/revision，成功后才能撤掉解码数据。正式 Commit 只纳入冻结点之前的暂存，之后的修改留下一轮；未提交暂存不作为崩溃后的正式进度。保留当前/上一逻辑提交，共享未变行，无变化只更新时间；世界永久退役通过同修订的 scope removal 清理，失败不发布。
- 生成与 chunk/weather 读取并行，全部就绪才 Commit Ready/恢复实体/申请表现；读取失败不能当作空差量。冷地表返回 Pending 时保留落地、爆炸和掉落的待结算预算。
- 可逐出缓存只留冻结字节：Android 8MiB/256、Windows 32MiB/1024，任一触限逐出；活跃模型与系统租约另计。并发读取共享工作，各调用者独立取消；最后等待者离开才撤掉未完成读取，旧会话/旧 state 不得发布。
- 表结构 `user_version` 与记录 schema 分离；`ArchiveSchemas` 登记必要 schema/迁移。发布 MemoryPack DTO 必须 VersionTolerant+固定 Order；新字段追加新编号并给显式缺省，已有编号/类型/含义变化走迁移，嵌套二进制/JSON 同样受约束。未知 `optional.*` 记录不解释、不删除，必要的未知/未来版本拒绝。
- 物品/模块持久化用 InstanceSnapshot。禁止直接保存运行 struct 布局；`Ex_ModData_MemoryPackable` 的 unmanaged 扩展必须登记带身份/版本的 codec。坐标、decimal 与 Unity 曲线键用明确保存字段，decimal 采用 invariant 文本。
- 数据库升级前走 SQLite backup API，日常自动保存不复制整库。v1 固定样本和覆盖范围见 `Tools/Tests/SaveArchiveContracts/coverage.md`；不能把 .NET 隔离通过当作 Unity/Android/IL2CPP 通过。

## 区块与世界数据

`确定生成基线 → 地形/环境/实例差量 → 支撑恢复 → 表现/导航绑定`

- 地表 WorldKey=PlanetId，维度为 PlanetId__dimension__DimensionId。RuntimeChunkBaseline 对同一 ChunkRuntime 只应用一次差量，新运行体重新建立基线。
- RuntimeTileDeltas 保存完整 TerrainCell 与格墙损伤；ChangedItems 保存动态建筑及 AI；SupportCells 保存平台。Tile 栈由 Data_TileMap 读写，农业/支撑等独立列表均计入 HasChanges。
- 普通建筑装船前经 RemoveRuntimeBuildingRecord 立即撤掉地面 ChangedItems 并记录移除身份，不能仅卸载表现；落地或回滚恢复走普通建筑记录，清同身份移除标记。机器归属通过作用域移交，只在当前所有者的 MachineArchive 保存。
- AgricultureCells 仅保存水肥/真实作物，未完成锄地为会话状态。GroundLayerCells 保存采层/覆盖栈/草层；SnowCells 保存玩家厚度与天气基线，ContaminationCells 保存稳定污染 ID/数值；天气批写按格合并并保留作物字段。
- 密集 AgricultureCells 运行态按局部格建立不序列化的索引，少于16格直接线性读取；追加、同数量替换和移除会使索引同步或失效，天气批量写与普通土壤提交共用 `GetOrAddAgricultureCell`，规则 `save/agriculture_cell_index`。
- LiquidCells 只存局部坐标、稳定 LiquidId、深度；生成后覆盖、绑定前恢复，回到生成值删除差量。写液体不产生地形/农业差量，RecordLiquidBatch 先写权威内存再通知；缺失液体/污染定义明确拒绝恢复。
- AI 休眠先写完整 ChangedItems 再卸载，跨帧保存冻结出生/睡醒迁移，合并活动快照保留休眠居民。宿主维护的临时 AI 由双向宿主/成员 GUID 保存，排除独立 AI 快照。
- 正常场景解绑先采集地形并休眠当前世界活体，View/天气收尾后再次采集；重绑时恢复原本活跃的居民。失败保留模型和待恢复快照；意外卸载后已销毁且未快照的实例无法补回。
- 所有世界固定首次冻结的生成 Profile；不保留 FollowCurrent 开关。`flatworld.generation.algorithmVersion=1` 随 Profile 保存，主生成/搜索/克隆/冷查询共用选择，未知算法明确拒绝；改变生成结果新增版本，不原位修改 v1。
- RegionalWeatherStates 按共享天气区原点保存首次登记冻结的代表气候与可扩展要素档案（稳定 ID、概率、强度、持续秒数）、各要素当前事件、检查游标、雷电序号/最新落点/强度及手动覆盖时限；恢复、保存索引和雷击基线使用同一规范区域键。MemoryPack 只依赖存档专属 Record，恢复复制回纯模型，旧雷击只建基线不重结算。SerializableTimeData 保存物理周期/轨道参数和绝对起点，日年季长度为派生值；历史冻结当时参数，不保存镜头/粒子/输入动作。

## 独立载荷

- 机械冻结用 `FrozenMachineArchivePayload/2` 与固定字段的 `MachineArchiveMetadataSnapshot`，逐机器字节按 GUID 落库，删除身份、压力事件与气液余量单独保存。同轮机器/船件共用捕获；变化跟踪节点分帧预冻，最终复核进度、归属、库存与槽布局。扩展状态修改调用 `StateChanged`，缺失 MOD 与压力事件不丢弃。
- DroppedItemArchive/2 把稳定实例载荷与数量、耐久、物质及运动状态分开；内部实例动态值归一化，已知模块按纯值指纹复用，未知 codec 重新捕获。冷 Data 按需独立恢复并在写盘前回填，跨世界复制轻量记录；后台实际运动前恢复当前定义。按 WorldKey 隔离，不存 Entity 或表现对象。
- SpaceStateJson v3 在核心封装前捕获星区目录、星球 Visual、唯一模拟时钟、船/接口身份、局部乘员、实际 ItemInstanceSnapshot、船作用域 MachineArchive、舱气、导航/下降阶段及未结算预算。恢复先校验版本和星区身份，按乘员真实星区/飞行阶段选场景；游戏运行中无人星区继续推进纯数据，离线/加载不补模拟。冷地表撞击读取目标冻结地形与最新差量，按 GUID 去重。
- `SpaceLooseItemState` 的 Scale 使用固定 JSON 数值 DTO 并保留未知属性；会话/船体等 JSON 的扩展数据随复制与乘员替换保留，不能展开 Unity Vector3 计算属性。契约样本见 `Tools/Tests/SaveArchiveContracts`，太空往返见 `Tools/Tests/SpaceCruise`。
- LiquidContainerState.Composition 保存真实份数、尾量、PendingOutputs/随机态；FluidInventoryState 保存 decimal 气液 mol、内能和各口预留，预留已计入库存，温压/比例派生。
- PlanetData.Atmosphere 仅首次按 Profile 初始化，保存固定容量与实际余量，同星球维度共用；PressureExplosionQueue 和组合罐待释放预算进入机器档案。

## 内容与资源

- 离散概率原表位于 `Resources/Config/Probability/discrete-probability-tables.json`，每档 `value/percent`，原表合计100%。`DiscreteProbabilityConfiguration.Modifying` 只调整写时复制的 `DiscreteProbabilityState`，统一归一化后得到不可变快照；抽样共用 `DiscreteProbability.Sample`。生成概率写入 Profile 数字参数，随既有冻结配置保存并参与生成/生态指纹，后台不得读全局实时难度或 JSON。

- Item/Recipe/Tile/Actor 都由显式 Manifest 发现。Tile 保存稳定字符串 ID 与 runtimeTileId，数字编号不重排/复用，行为工厂与 MemoryPack Union 是不同注册边界；Actor 资源地址随 GUID，sourcePrefab 仅编辑器。
- 玩家创建模板只在新角色模块加载前注入，不入存档。
- Addressables 条目变更保存所属 Group；外部目录/源资源导入经 BatchModification 使 Fast Mode Locator 失效，AssetDatabase.Refresh 本身不够。源资源路径及主地址不得含方括号，避免被当成子资源标记；Sprite 单图用主地址，Multiple 才加子资源名。
- Player 构建启用随 Player 构建 Addressables；`AddressablesPlayerBuildGuard` 在资源构建失败时终止打包。SBP 的 `slimWriteResults` 固定为 false，避免构建报告临时改写配置遇到 Windows 文件映射占用；APK 的 `Succeeded` 不能代替资源构建成功。MemoryPack 结构变更与记录 schema 配套，追加可选数值缺省不能靠字段初始化值；0 有效时用可空值区分缺省。
