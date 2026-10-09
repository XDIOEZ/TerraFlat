---
name: flatworld-data-save
description: "Use when: 定位或修改 FlatWorld 的数据模型、MemoryPack 存档、自动保存、区块差量、玩家数据、星球数据、Addressables 或 JSON 配置。关键词：SaveDataMgr、GameSaveData、ItemData、ModuleData、MapSave、PlanetData。"
---

# FlatWorld 数据与存档

## 入口

- 磁盘与快照：`Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.cs`
- 根数据：`World/Map/Data/GameSaveData.cs` 及 `GameSaveData.*.cs`
- Item/Module：`Assets/5_Scripts/5-1_Data/{ItemData/ItemData,ModData/ModuleData}.cs`
- 地图/星球：`World/Map/Data/{MapSave,PlanetData,EcologyWorldSaveData}.cs`
- 工业两相库存/大气：`World/Fluids/Core/FluidState.cs`、`Atmosphere/{AtmosphereState,PlanetData.Atmosphere}.cs`。
- JSON：`Assets/StreamingAssets/GameConfig/`；Addressables：`Assets/AddressableAssetsData/`

## 核心不变量

- 开发阶段只读取当前 Envelope 版本，不保留旧版本自动升级路径。实例快照格式为 FWD9/25，外层为 FWD5/12；格式头必须在反序列化内部对象之前校验，旧格式明确拒绝但不删除或覆盖原文件。
- `SerializableTimeData` 保存自转/公转周期、倾角、离心率、公转秒制起点、Profile、限时边界与月相；日长、年长和季长是只读派生值，不重复入档。季节历史按绝对游戏秒冻结当时的物理参数；恢复经统一物理入口重建派生状态，`EnsureTimeSystemDefaults()` 不承担旧存档兼容。
- 正式存档只写 `Application.persistentDataPath/Saves/LocalSaveData/`，并使用临时文件/原子替换；失败不得伪装为成功恢复。
- `GameSaveData.PlayerData_Dict` 的键是不可变角色 ID，`Data_Player.Name_User` 仅是可修改的显示名；旧档保留原字典键作为兼容 ID，新角色分配独立 ID。改角色名只写 `Name_User`，改存档名要同步 `GameSaveData.saveName`、磁盘文件名和最后退出时间元数据，失败保留旧档。
- `ItemSpecialDataJsonStore` 按命名空间更新并保留未知根属性；教程、任务、维度、出生点不得互相覆盖或改 `Data_Player` 布局。
- Item/Recipe JSON 是唯一内容真源；Manifest 不自动扫描目录。移动资源还要核对 Address、标签和运行时字典键。
- Tile JSON 同样由显式 Manifest 加载，定义保存稳定字符串 ID、世界整数 `runtimeTileId`、资源键及行为参数；不改变 `TileData` 的 MemoryPack 布局，也不保存角色或格子的运行时状态。已发布的地块整数编号不能重排、复用或被 Patch 修改；JSON 行为工厂注册不等同于新增 MemoryPack Union 注册。
- Actor JSON 位于 `GameConfig/Actors`，使用独立 Manifest；外壳/Sprite/Animator 的 `flatworld.actor.*` 地址由 GUID 跟随移动，`sourcePrefab` 仅供编辑器。
- Android/Player 构建必须启用 Addressables 随 Player 自动构建（`m_BuildAddressablesWithPlayerBuild: 1`），否则 `GameRes` 在真机包内可能拿到缺失或过期的本地 Catalog。
- 编辑器通过 `AddressableAssetSettings` 新增或修改条目后，必须显式保存条目所属的 `AddressableAssetGroup`；只保存 Settings 可能让条目停留在内存，未进入 Git 与后续构建。
- Editor Fast Mode 的 `AddressableAssetSettingsLocator` 会缓存键索引；外部工具直接修改 `Assets/AddressableAssetsData` 后，仅执行 `AssetDatabase.Refresh()` 不会触发 `Settings.OnModification`。导入回调必须补发 `BatchModification`（只通知、不再次写盘）使运行中的 Locator 失效并重建，否则 F5 资源会话仍可能解析旧目录并对新地址抛 `InvalidKeyException`。如果 Addressables 条目先写入、对应源资源后导入，导入回调还要在该资源 GUID 已存在条目时刷新 Locator，不能只监听 `Assets/AddressableAssetsData/` 自身的导入事件。
- Sprite 地址只有在源图是 Multiple 切片时才追加 `[子资源名]`；单 Sprite 图必须使用主资源地址。Unity 2022.3 + Addressables 1.22.3 Fast Mode 遇到无效子资源地址可能在 `AssetDatabaseProvider.LoadAssetSubObject` 抛空引用，编辑器加载路径需先做资源存在性和子资源名称校验。
- Tile 栈只通过 `Data_TileMap` API 读写；区块差量保留基线、ChangedItems、删除 GUID 与确定性 ID 语义。
- 新版 WorldModel 的格子建筑写入 `ChunkTerrainData.BlockingTileId`，不会进入 `MapSave.items`；必须按“确定性生成基线 → RuntimeTileDeltas 差量 → 表现绑定”的顺序持久化和恢复。
- `RuntimeChunkBaseline` 随同一份 `ChunkRuntime` 保留在缓存时，存档差量只能应用一次；视野往返不得再次覆盖运行中的地形改动。地址复用但生成了新的 `ChunkRuntime` 时重新建立基线并恢复。
- `RuntimeTileDeltas` 与基线保存完整 `TerrainCell`（地表、背层、阻挡层、群系、导航代价、Flags）及建筑损伤；水上平台另外通过 `SupportCells` 保存支撑层，底层水属性本就应保留。嵌套 MemoryPack 布局变化必须在解析嵌套数据前拒绝旧格式，可更换外层格式头，不能依赖完整反序列化之后才比较版本。
- 新版 WorldModel 的动态建筑 Item 不属于旧 `Chunk.RunTimeItems`；建筑存档要复用区块 `ChangedItems` 差量，按 `Mod_Building` 角色清理旧记录并在区块数据就绪后实例化恢复。
- 运行时 GameObject AI 远距休眠时先保存完整 `ItemData` 到区块 `ChangedItems`，再从 `ItemMgr` 卸载；自动保存跨帧采集期间暂停生态出生、休眠和唤醒。活动 AI 快照合并时须保留同区块的休眠居民，成功唤醒后才移除旧记录与休眠索引。
- 由自然物宿主维护的临时 AI 应由宿主模块保存成员 GUID，成员自身记录宿主 GUID 并排除独立运行时 AI 快照；宿主卸载只撤回实例，死亡补位才分配新身份，避免重新装载时重复生成或改变存活成员身份。
- 自动/手动保存可分帧采集，但后台只处理不可变快照；旧任务不得覆盖更新的手动/退出保存。
- `IRuntimeDataLifecycle.Save()` 只抓取持久化快照，禁止解绑事件、停止行为或释放资源；Item 退出、移除模块与回池统一调用 `Unload()`，重新加载前也必须先卸载旧运行态。
- 玩家体力存档只保存 `Mod_Stamina.StaminaData.CurrentStamina/MaxStamina` 的已结算权威值；划船加速、拉弓、奔跑、游泳等消费来源和按键状态均属于瞬时运行态，不进入存档。自动存档允许在这些动作持续期间抓取当前值，禁止为了存档暂停操作或序列化“正在消耗”状态。
- 地表 `WorldKey=PlanetId`；非地表用 `PlanetId__dimension__DimensionId`。`TopologyMode` 的当前默认值为 `Infinite=0`，世界字段按当前版本统一读写。
- 任务 `flatworld.quests` 等未来版本必须拒绝写回；未知 MOD 记录应保留。
- 玩家创建 JSON 位于 `StreamingAssets/GameConfig/Players`，不进入 MemoryPack 存档；创建期属性只在无存档阶段注入，并在模块加载前同步到 `Data_Player.ModuleDataDic`。`core.heatConductionRate` 是当前内容配置，重新加载已有玩家时仍覆盖存档中的该静态速率，其他玩家状态仍以存档为准。

- `ChunkSaveRecord.HasChanges` 必须计入独立的农业和平台状态；农业存档只保存需要持久化的水肥与作物，未完成锄地是纯运行时视觉/输入进度，不进入农业差量；真正耕完后由 `RuntimeTileDeltas` 保存正式 `Tile_Farmland`。恢复支撑必须在导航及表现绑定之前，不能只有当前帧可行走、重载后丢失平台。
- 伪 Z 轴由 `ChunkSaveRecord.GroundLayerCells` 稀疏保存已采天然层数、玩家覆盖栈及表面草层，恢复必须在地形/草层差量之后、表现绑定之前；同地块 ID 的连续石层也必须保留记录。新增字段仅追加到 MemoryPack 类末尾，缺失的列表可能为 null，不能把初始值当成旧记录的缺省。
- 玩家雪层使用独立 `SnowCells` 差量，记录编辑标记、实际厚度与编辑时季节覆盖；空雪格仍算有效变化，必须计入 `HasChanges`，并在表现绑定前恢复，不能用 Ground 差量替换原地形。
- 地块污染使用 `ChunkSaveRecord.ContaminationCells` 保存偏离定义默认值的稀疏差量；污染定义 ID 与数值一起持久化，恢复时必须要求当前本体/MOD 已注册该定义，禁止静默丢弃未知污染状态。
- 时间保存同时复制季节配置和历史区间；积雪、植物冷热暴露、自然补位年份、陶罐水质／加工进度、盐分负担各有独立状态，不能在渲染绑定或 UI 打开时重置。
- MemoryPack 追加可选数值字段时，不能依赖字段初始化值表达缺省；若 0 也是有效配置，使用可空数值并在转为运行时快照时解析缺省，避免未记录字段被当成显式 0。
- 通用容器以 `LiquidContainerState.Composition` 保存各稳定 LiquidId 的真实份数，`Amount/LiquidId` 只是派生读数；不足一份的尾量、各口 `PendingOutputs` 和随机状态必须保留，不能用取整或 epsilon 清空正数。
- `FluidInventoryState` 保存各 FluidId 的 decimal 气液 mol、总内能、随机状态及每口预留；预留已经计入库存，恢复或合并不能再加一份。温度、压力、比例仅派生；份数换算以 `LiquidDefinition.LitersPerServing` 为准。
- `PlanetData.Atmosphere` 只在首次创建时按 Profile 初始化；固定容量和 decimal 实际余量一起保存，同星球各维度共用地表储量，重进、打开 UI 和资源更新不能补满。待结算压力爆炸及组合罐释放预算随 `MachineArchive` 保存。
- ItemData 及派生类的 MemoryPack 持久化只通过 InstanceSnapshot；Unity/JSON 字段是配置适配外壳，不直接入档。冷数据不读取 GameRes，入世/入包先按当前定义原位恢复重量、标签、耐久与模块组合，再合并实例状态；保留真实槽位的 ItemData 引用，已删除模块不能复活，未知扩展负载继续保留。
- `InstanceSnapshot` 捕获发生在外层 MemoryPack 写入期间；内部负载和扩展 `IModuleInstanceStateCodec.Capture` 使用 `ItemSnapshotSerialization.SerializePayload` 独占缓冲及引用状态，禁止嵌套调用默认 `Serialize(value)` 覆盖同线程外层缓冲，避免读到错误的 Union 标记。
- FastCloner 3.3.10 对带 `FastClonerIgnore` 的可写属性会调用 setter 写入默认值；`InstanceSnapshot` 这类无后备字段的计算属性不要加该标记，避免克隆配置时触发空快照恢复。自动属性与实际字段仍按需标记。
- ItemInstanceDataFactory.Compile 冷路径冻结共享配置与纯内存默认状态，内建 Item/Module、库存与分堆直接创建独立运行态，不经快照往返；MOD 可注册 IModuleRuntimeDataFactory，未迁移类型保留独立模块快照回退。CloneRuntime 保留冷数据标记，分堆调用方分配新 GUID；临时模板用 DetachModuleData 移交并解绑集合。库存快照只保存内容及用户状态，恢复保留当前 UI/输入/槽位标签，并为新冷物品补当前定义。不透明 BitData 的配置与进度仍由模块负责区分。
- 冷数据可从当前 JSON 或 Prefab-only 模板恢复；活跃库存只补新冷物品配置，不能重建已挂接定义的 ModuleData 引用。模块自有库存也使用 InventoryInstanceSnapshot，禁止把 MemoryPack 往返当作热实例克隆器；同 GUID 跨槽移动继续复用原 ItemData。
- 派生实例属性通过 IItemInstanceStateRestorer 注册纯数据恢复规则，统一在模块状态恢复后执行；不能在快照层硬编码具体玩法。编解码器收发缓冲与快照隔离，释放注册租期前先完成状态调用及实例清理。
- `ItemData.CraftedDurabilityMultiplier` 是制作材料赋予的实例品质，作为追加字段持久化；恢复时先用当前 ItemDefinition 的基础耐久重建，再应用倍率并保留原耐久百分比，禁止把历史 `MaxDurability` 直接当成当前基础值。
- `GameSaveData.WorldGenerationConfigMode` 是每个存档自己的生成规则策略：`Frozen` 保持 `PlanetData.Ecology` 冻结 Profile；`FollowCurrent` 跳过冻结 Profile 并允许存档跟随当前游戏版本。玩家从冻结切到跟随当前时清除所有维度的冻结 Profile，但必须保留 `EcologyWorldSaveData.Chunks` 内的删除 GUID、状态覆盖和恢复年份；重新冻结由下一次正式世界生成捕获当前 Profile。

## 工作流与验证

- 太空权威快照保存在 `GameSaveData.SpaceStateJson`；抓取必须在正式核心存档封装前完成，同时捕获船上 `MachineArchive` 和实际 ItemInstanceSnapshot，视图不成为第二份持久化所有者。
- 保存星体种子/时间、船体与接口身份、局部乘员、真实舱气、导航目标、下落高度/速度/随机结果及未结算爆炸和液体释放预算。迁移中保存后按飞行阶段恢复目标场景；离线和加载阶段不追补模拟。
- 未加载星球上的落地损毁复用冻结地形和最新 Chunk 差量，修改内存存档及实际机器库存，由正常保存链写盘；冷路径和活动路径按同一 GUID 去重，不能为处理撞击加载一次地表 Scene。

- `CompactSaveEnvelope.DroppedItems` 承载独立版本的 ECS 掉落快照；`GameSaveData.DroppedItems` 必须保持 MemoryPackIgnore，不能改变旧核心对象布局。按实际世界场景名（维度 WorldKey）隔离载荷，存稳定 ID、库存状态和未完成轨迹，不存 Entity 地址、GameObject 或显示批次。
- 旧生产者移交必须先于区块快照；退出保存完成后才释放掉落 World，不能在更早的 GameWorldExit 通知中清空。缺失定义的记录保留原快照，避免下次保存静默丢物；恢复库存载荷仍按当前 ItemDefinition rebase。

1. 先确定权威数据、持久化位置和当前版本，再修改模型；不要新增旧版本迁移分支。
2. 使用隔离存档进入真实 Play Mode，实际覆盖写入、退出、重进和恢复；不得触碰正式玩家存档。编译与 Console 只作为运行门禁和故障定位。
3. 联动：生命周期→Core，Item/Module→Item，Chunk 差量→Map，协议快照→Networking，内容 Def→对应领域 Skill。

- 世界液体差量使用 `ChunkSaveRecord.LiquidCells`，每项只有局部坐标、LiquidId 和 LiquidDepth；空格用空 ID 与零深度。生成值变化后才记录，恢复生成值要移除差量；恢复前校验全部坐标、重复项和液体身份，缺失 MOD 液体必须明确拒绝，不能擦除原存档。
- 液体先生成再覆盖差量，覆盖必须早于表现和导航绑定。Ground 使用完整 TerrainCell.Equals 比较；液体写入不修改地面字段，只更新 `LiquidCells`，不能产生 `RuntimeTileDeltas`。农业水分只属于土壤，不推测旧水地块编号、盐度或农业 Water 字段来恢复液体；不能把 LiquidTypeIndex 写入存档。

## Skill 维护原则

- 液体模拟按 Chunk 调用 `RecordLiquidBatch`，只更新内存中的 `LiquidCells` 并复用已有记录；全部权威写回后再发布批次通知。稳定 ID、零深度和恢复生成值时删除差量的规则不变；实验开关、活动集合、生成高度缓存及动态流向不增加存档字段。关闭实验不能撤销已经保存的模拟结果。

- 机械及工作方块统一用 `MachineArchive` 和外层 `CompactSaveEnvelope.MechanicalNetworks`；`GameSaveData.Mechanical` 保持 `MemoryPackIgnore`。落地机器不能在 Item 与机器存档中双写；无表现节点也进入独立存档，保存完成后才能释放 `MachineWorld`。
- 落地机械节点的 `ItemData` 仅作冷快照，不创建运行时 Item；加工库存、拓扑、朝向和耐久随机械专用归档保存，拆回 Summoner 时先捕获完整快照，成功提交后才移除节点。区块表现回收不能影响节点存续。
- 二进制加工库存由 `RecipeProcessor/MachineInventory` 恢复时重新挂接当前 ItemDefinition；不能假设通用 `Inventory_ModuleData` 递归会访问专用二进制载荷。缺失机器 MOD 定义的原快照仍保留在对应世界档案中。

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
