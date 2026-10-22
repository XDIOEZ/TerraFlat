---
name: flatworld-building
description: "FlatWorld 建造预览、放置与拆除事务、召唤器快照、离散占地及地形支撑层。"
---

# 建造

- 放置拒绝通过 `PlacementRejectedDetailed` 携带真实校验/权威回执原因给气泡，`PlacementRejected` 继续服务音效语义；不得把数量、支撑或事务失败统一覆盖为位置错误。

## 代码与内容

- 玩法根：`Assets/5_Scripts/5-3_GamePlay/`。
- `World/Building/`：Mod_Building、BuildingShadow、BuildingOccupancyRegistry、BuildingPlacementService、BuildingModuleStateTransfer；门使用 Mod_Door。
- `World/Map/Structures/`：ChunkGenerator_Structures、StructureData、StructureItemAuthoring；结构资源在 `Assets/4_ScriptObjects/World/Structures/`。
- JSON：`Assets/StreamingAssets/GameConfig/Items/shells/{building_summoners,building_bodies}.json`；通用 BuildingSummonerShell/BuildingBodyShell，玩法模块位于 `Assets/2_Prefabs/Gameplay/Modules/Building/`。
- 迁移：`Assets/Editor/FlatWorld/ContentTools/Migrations/BuildingShellMigrationTool.cs`。

## 放置与状态转移

`真实手持 Summoner → 当前准线/权威格校验 → 可撤销候选 → 扣源槽 → 提交/安装通知`

- BuildingPlacementService.TryPlace 为单机/服务端统一事务；后端提供撤销动作，地形写入前建立撤销位置，机器用 DiscardPlacementCandidate。失败先确认权威候选已撤销再退料；成功发布后的通知异常不撤销结果。
- `ValidateActorEligibility` 为预览与权威事务的共用玩家资格入口；隐藏电线按目录 `Electrical.Hidden` 检查真实工程视野，地表和船上均在创建候选、扣料前拒绝，失败原因交现有气泡反馈。
- BuildingShadow 只显示反馈，不提供安装资格/坐标。生存范围等于玩家实际交互半径（原两倍已减半），创造模式无限；预览与提交按目标格最近边缘校验。失败反馈由实际 Install 的语义事件发布，逐帧预览不发提示。
- Summoner 来自快捷栏真实 InHand/Owner/CurrentSelectItemSlot；成功扣减经 Inventory_Data 事务，并按实际槽索引 RefreshUI。放置模式拥有使用动作和普通世界交互的优先权。
- Host 成功回执复用服务端已经提交的共享库存，只收尾预览和同步手持；仅普通客户端按回执写入剩余数量，避免最后一份扣空后重复解析源槽。
- BuildingRole 区分载体/落地实体，并与当前 SummonerPrefabId/BuildingPrefabId 对齐。无快照从 CreateItemData(BuildingPrefabId) 创建当前本体；有快照先恢复，再用当前载体共享状态覆盖。
- 召唤器与本体的基础建筑模块必须 enabled=true，让 OnLoad 读取正式配对与快照；迁移不能沿用源 Prefab 的禁用旗标，目录检查拒绝禁用配置。
- SharedModuleIds 以唯一能力 ID 深拷贝单件载体的连续状态；返还载体在 Load 前注入。耐久品质 CraftedDurabilityMultiplier 双向转移，实际最大生命按当前定义重算；两端状态贴图各自声明。
- RequiresPlacementRequest 由正式入口 BeginPlacement 开启，TryHandlePlacementAction 仲裁；普通无显式入口召唤器直接 Install。世界根旋转初始化 identity，手持旋转不迁移；世界朝向/镜像是已放置状态，拆回清零。
- 拆回先捕获快照、成功生成返还物再删除原建筑。堆叠身份取规范化快照（去放置重写的 GUID/Transform）；IBuildingSnapshotRepackPolicy 可省默认快照，损伤/共享状态继续保留。
- SnapshotBase64 统一由 ItemNetworkStateSerialization.TrySerializeItemData 写入带 FWI1 包头的实例封包；船部件回收也复用此入口，不能直接写裸 MemoryPack。放置与 ShipMassReader 使用同一恢复契约，Tools/Tests/ShipMass 覆盖回收载体库存。
- 主动完整拆回与受伤摧毁分开：锤类致命伤返 Summoner，其它破坏按唯一精确配方随机返材料。拆平台先检查上下层、设施和对象占用，返还装配成功才撤支撑。

## 占地、碰撞与支撑

- 占地来自 FootprintWidth/Height，吸附格为左下锚点；逐格按拓扑归一化并登记/注销。visual.collider 是实体物理范围，health.collider 是独立受击范围，均不决定建造/导航格。
- BuildingOccupancyRegistry 是放置、通行、移速和动态 LOS 来源；Revision/CellChanged 驱动脏区，视线刷新不只看最终可走值。相邻格 Collider 接触不互斥，同格按层拒绝。
- 普通建筑通行由 IBuildingTraversalPolicy，机器读目录 BlocksMovement/PlayerMoveSpeedMultiplier；机器接触几何由区块合并静态 Collider 投影。机械通过 IBuildingPlacementExtension 声明层/候选/快照，IBuildingPlacementCommitted 后才登记世界副作用。
- 普通船地板由 Mod_ShipPart 声明独立占地层6和通行规则，可与管线/设备共格；船门复用正式 Door 模块。安装朝向来自候选数据，预览/占地按同一四向尺寸计算；Mod_Building.Orientation 补偿多格贴图和碰撞，卸载恢复，不继承手持旋转。
- 门绑定所属主 Sprite/根碰撞体，开门保留交互与占格，经 SetPassable 改通行；安装后再同步状态。模块碰撞体引用运行时绑定 Item 根，不额外叠加模块 Collider。
- PlacementLayer=Ground 占 Layer2，允许与 Layer0 共格；Trigger、不阻导航/LOS、无三类阴影。排序为 ground-building，Queue2991 位于地表覆盖后、Blocking2992 前。
- requiredGroundSupport 为当前定义配置，各格比较有效表面 loadCapacity，等值可放；支撑覆盖优先，其次液面 worldWater，再次干地 Tile。
- 平台/地板通过 Tile_Block.groundPlacement 写 TerrainSupportLayer，不换底层地形；平台要求有液体，地板要求 Walkable 且无液体，判断原始 LiquidDepth。有效覆盖统一供预览、导航和角色水域读取。
- 静态墙走 Blocking Tile，TileBlockId 将召唤器接入 TileBuildingSystem，图/transform 取目标 Tile，格心为占地锚点。落地生命取 Tile_Block.damageProfile；数字 TileId、Profile/Palette、Tile/Tile_Block/Addressables 构成完整内容链。

## 表现与持久化

- 通用本体为 Item/表现/根查询碰撞，能力由 JSON 组合，交互由独立 IInteractable 模块。门/帐篷等为 GameObject 建筑；机器/设施为 MachineWorld 数据节点，其求解与端口表现由机器系统负责。
- 预览从 RuntimeItemDefinition 创建无模块轻量视觉，sourceRoot/sourceRenderer 同层级，不创建刚体或碰撞。rendererLocalPosition/Scale、状态图与最终表现共用定义。
- 机器扩展在预览子图层应用安装朝向，BuildingShadow.ResolvePlacementRootRotation 让根只叠船角；无扩展船件由根应用完整朝向，移出船体也必须刷新根旋转。
- LightOcclusion 配置在 Mod_Building.LightOcclusion：Automatic 随真实 Light2D 裁自身光源以上轮廓，熄灯恢复；FullSilhouette/None 为完整/无轮廓。OwnLightClearance 默认0.0625，配置不入快照，更新接收层经 RefreshLightOcclusion。
- 动态建筑完整 ItemData 进入区块 ChangedItems，数据就绪后恢复；格墙 ID/损伤进入 RuntimeTileDeltas，平台进入 SupportCells。地块生成可冻结，玩家建造映射读取当前内容目录，建筑关系权威配置在模块 data.BitData。
