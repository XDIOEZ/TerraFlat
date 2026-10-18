---
name: flatworld-data-save
description: "FlatWorld 实例快照、核心存档、区块差量、资源配置与 Addressables 所有权。"
---

# 数据与存档

## 代码入口

代码根为 `Assets/5_Scripts/5-3_GamePlay/`：

| 目录 | 入口与职责 |
| --- | --- |
| `Core/Save/` | SaveDataMgr*、AutoSaveController：采集、差量与磁盘写入 |
| `World/Map/Data/` | GameSaveData*、MapSave、PlanetData、EcologyWorldSaveData：根档案 |
| `World/Fluids/Core/`、`Atmosphere/` | FluidState、AtmosphereState、PlanetData.Atmosphere：两相库存/大气 |
| `World/Space/`、`World/Machines/` | SpaceSessionState、MachineArchive：太空与机器独立载荷 |

Item/Module 数据在 `Assets/5_Scripts/5-1_Data/{ItemData,ModData}/`；内容在 `Assets/StreamingAssets/GameConfig/`，Addressables 在 `Assets/AddressableAssetsData/`。

## 快照与磁盘边界

- `GameManager.SaveGame` 为设置按钮和帐篷共用的手动保存入口：主线程立即采集完整快照、后台原子写盘，保存状态等待真实任务结果；自动保存单独走 `SaveGameInBackgroundCoroutine` 的分帧预算。睡眠先记重生点并保存，再应用临时时间倍率。

- 当前外层 FWD6/15，直接包含 CompactSaveEnvelope/32，避免多层整档字节复制；同步、自动和网络快照共用格式。地形区块逐条冻结，生态动态块的 ChangedItems 逐物品独立冻结，不进入核心整档重捕获；载入时在主线程恢复 Item/扩展状态。格式头先校验，仅支持当前格式，拒绝的文件保留。磁盘载入失败清空当前档案和角色，完整解析与 MOD 验证成功后才替换区块差量；普通损坏仍依次尝试备份和临时文件。正式位置为 persistentDataPath/Saves/LocalSaveData，临时文件/原子替换提交。
- ItemData 的持久化边界是 InstanceSnapshot，JSON/Unity 字段是配置外壳。冷数据不查 GameRes；入世/入包先按当前定义恢复重量/标签/耐久/模块，再合并稳定模块实例状态，未知扩展负载保留。
- ItemInstanceDataFactory 冷编译共享配置/默认状态，CloneRuntime 创建独立运行态，不经 MemoryPack 往返；同 GUID 跨槽保留 ItemData 引用，分堆分配新 GUID。活跃库存只 rebase 新冷物品，专用二进制库存由持有模块显式逐槽处理。
- InventoryInstanceSnapshot 只保存内容/用户状态，恢复保留当前槽标签、UI、输入。不透明 BitData 的配置/进度由模块区分；IItemInstanceStateRestorer 在模块状态恢复后恢复派生属性。
- 外层 MemoryPack 写入期间的嵌套负载使用 ItemSnapshotSerialization.SerializePayload 独占缓冲；IModuleInstanceStateCodec 同样隔离收发缓冲。FastClonerIgnore 的可写属性会被 setter 写默认值，InstanceSnapshot 计算属性不使用该标记。
- CraftedDurabilityMultiplier 保存实例品质；恢复按当前基础耐久重算，再保持损伤比例。IRuntimeDataLifecycle.Save 只捕获，Unload 才清事件/资源。
- 自动保存共用 AutoSaveWorkBudget：每片目标 0.5ms/最多16单元，耗尽让出两帧再计时；单区块建筑合并保持原子，玩家/机器/船体/掉落联合采集也不跨帧。玩家时钟与 ModuleSave 通过 captureCoreState 在最终联合冻结点执行，不能提前到分帧区块扫描之前，否则快捷栏字节会保留转移前库存。主线程冻结独立字节，后台只封装和原子写盘，不调用 Item codec/Lua。开始采集即登记 revision，并固定场景/WorldRuntime/Epoch；换世界、退出或新保存使旧任务失效。保存完成后才释放 MachineWorld/掉落运行态；旧生产者移交早于区块快照。
- PlayerData_Dict 用不可变角色 ID，Name_User 仅显示名。改档名同时更新 saveName、磁盘名与退出元数据；特殊 JSON 状态按命名空间保留未知根属性，未来版本扩展拒绝写回。
- 生态存档 ItemData 是生产者移交的独立快照；分帧先缓存每个快照的冻结字节，最终玩家联合采集点重读当前生态成员、删除 GUID/恢复年份，仅补冻新增/替换快照，避免期间采收导致重生。缓存不得用于活跃可变 ItemData；新增原位修改存档快照的生产者时须同步调整冻结契约。
- 已有角色实例化前必须从 `Data_Player.transform` 取得保存位置，并同步刚体初始位置；不能先以世界原点创建玩家再期望异步物理/模块加载纠正。
- 保存退出先在世界退出事件/输入解绑之前执行 `ItemMgr.SavePlayer`，世界事件结算后再写盘；`SaveDataMgr.Save_And_WriteToDiskAndRecordExitTime` 返回实际成功状态，退出回到菜单不能代替写盘成功。

## 区块与世界数据

`确定生成基线 → 地形/环境/实例差量 → 支撑恢复 → 表现/导航绑定`

- 地表 WorldKey=PlanetId，维度为 PlanetId__dimension__DimensionId。RuntimeChunkBaseline 对同一 ChunkRuntime 只应用一次差量，新运行体重新建立基线。
- RuntimeTileDeltas 保存完整 TerrainCell 与格墙损伤；ChangedItems 保存动态建筑及 AI；SupportCells 保存平台。Tile 栈由 Data_TileMap 读写，农业/支撑等独立列表均计入 HasChanges。
- 普通建筑装船前经 RemoveRuntimeBuildingRecord 立即撤掉地面 ChangedItems 并记录移除身份，不能仅卸载表现；落地或回滚恢复走普通建筑记录，清同身份移除标记。机器归属通过作用域移交，只在当前所有者的 MachineArchive 保存。
- AgricultureCells 仅保存水肥/真实作物，未完成锄地为会话状态。GroundLayerCells 保存采层/覆盖栈/草层；SnowCells 保存玩家厚度与天气基线，ContaminationCells 保存稳定污染 ID/数值；天气批写按格合并并保留作物字段。
- LiquidCells 只存局部坐标、稳定 LiquidId、深度；生成后覆盖、绑定前恢复，回到生成值删除差量。写液体不产生地形/农业差量，RecordLiquidBatch 先写权威内存再通知；缺失液体/污染定义明确拒绝恢复。
- AI 休眠先写完整 ChangedItems 再卸载，跨帧保存冻结出生/睡醒迁移，合并活动快照保留休眠居民。宿主维护的临时 AI 由双向宿主/成员 GUID 保存，排除独立 AI 快照。
- Frozen 使用冻结 Ecology Profile；FollowCurrent 使用当前生成配置。切换清 Profile 时保留生态删除 GUID、状态覆盖和恢复年份。
- RegionalWeatherStates 按共享天气区原点保存首次登记冻结的代表气候与可扩展要素档案（稳定 ID、概率、强度、持续秒数）、各要素当前事件、检查游标、雷电序号/最新落点/强度及手动覆盖时限；恢复、保存索引和雷击基线使用同一规范区域键。MemoryPack 只依赖存档专属 Record，恢复复制回纯模型，旧雷击只建基线不重结算。SerializableTimeData 保存物理周期/轨道参数和绝对起点，日年季长度为派生值；历史冻结当时参数，不保存镜头/粒子/输入动作。

## 独立载荷

- CompactSaveEnvelope.MechanicalNetworks 保存 MachineArchive；GameSaveData.Mechanical 为 MemoryPackIgnore。落地机器不在 Item 档双写，冷 ItemData 不创建运行 Item，缺失 MOD 机器保留原快照；专用加工库存恢复当前定义。
- 外层 DroppedItems 保存独立版本掉落快照，GameSaveData.DroppedItems 为 MemoryPackIgnore；按实际维度 WorldKey 隔离，存稳定 ID/库存/轨迹，不存 Entity 地址或显示对象，缺失定义保留记录。
- SpaceStateJson v2 在核心封装前捕获星区目录、唯一模拟时钟、船/接口身份、局部乘员、实际 ItemInstanceSnapshot、船作用域 MachineArchive、舱气、导航/下降阶段及未结算预算。恢复先校验版本和星区身份，按乘员真实星区/飞行阶段选场景；游戏运行中无人星区继续推进纯数据，离线/加载不补模拟。冷地表撞击读取目标冻结地形与最新差量，按 GUID 去重。
- SpaceLooseItemState 是独立散落物保存模型；Scale 用字段专属 JSON 转换器只读写 x/y/z，不能直接展开 Unity Vector3 的计算属性。SpaceCruise 离线回归覆盖缩放及完整会话快照往返，不操作 Play Mode。
- LiquidContainerState.Composition 保存真实份数、尾量、PendingOutputs/随机态；FluidInventoryState 保存 decimal 气液 mol、内能和各口预留，预留已计入库存，温压/比例派生。
- PlanetData.Atmosphere 仅首次按 Profile 初始化，保存固定容量与实际余量，同星球维度共用；PressureExplosionQueue 和组合罐待释放预算进入机器档案。

## 内容与资源

- Item/Recipe/Tile/Actor 都由显式 Manifest 发现。Tile 保存稳定字符串 ID 与 runtimeTileId，数字编号不重排/复用，行为工厂与 MemoryPack Union 是不同注册边界；Actor 资源地址随 GUID，sourcePrefab 仅编辑器。
- 玩家创建模板只在新角色模块加载前注入，不入存档。
- Addressables 条目变更保存所属 Group；外部目录/源资源导入经 BatchModification 使 Fast Mode Locator 失效，AssetDatabase.Refresh 本身不够。Sprite 单图用主地址，Multiple 才加子资源名。
- Player 构建启用随 Player 构建 Addressables；`AddressablesPlayerBuildGuard` 在资源构建失败时终止打包。SBP 的 `slimWriteResults` 固定为 false，避免构建报告临时改写配置遇到 Windows 文件映射占用；APK 的 `Succeeded` 不能代替资源构建成功。MemoryPack 结构变更与格式头配套，追加可选数值缺省不能靠字段初始化值；0 有效时用可空值区分缺省。
