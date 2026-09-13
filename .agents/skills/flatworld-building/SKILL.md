---
name: flatworld-building
description: "Use when: 定位或修改 FlatWorld 的建筑放置预览、安装拆除、召唤器/世界建筑角色、建筑快照、占地、门、堆肥、结构生成、建筑 Prefab 或结构编辑器。关键词：Mod_Building、BuildingShadow、BuildingOccupancyRegistry。"
---

# FlatWorld 建造

## 入口

- 主链：`Assets/5_Scripts/5-3_GamePlay/World/Building/{Mod_Building,BuildingShadow,BuildingOccupancyRegistry}.cs`
- 建筑 JSON：`Assets/StreamingAssets/GameConfig/Items/shells/{building_summoners,building_bodies}.json`；召唤器统一使用 `BuildingSummonerShell`，动态本体统一使用 `BuildingBodyShell`。
- 建筑 Shell/模块迁移：`Assets/Editor/FlatWorld/ContentTools/Migrations/BuildingShellMigrationTool.cs`；差异玩法位于 `Assets/2_Prefabs/Gameplay/Modules/Building/`。
- 门/堆肥：同目录 `Mod_Door.cs`、`Mod_CompostBin.cs`
- 结构：`World/Map/Structures/{ChunkGenerator_Structures,StructureData,StructureItemAuthoring}.cs`
- 结构资源：`Assets/4_ScriptObjects/World/Structures/`

## 核心链与不变量

`Summoner → BuildingShadow 校验 → PlacedBuilding → 注册占地 → 导航脏格`；拆除反向生成带 Snapshot 的 Summoner，成功后才删除建筑。

- 以 `BuildingRole` 区分 Summoner/PlacedBuilding，禁止用血量或位置推断。
- 无快照的新放置必须通过 `GameRes.CreateItemData(BuildingPrefabId)` 创建本体 JSON 数据；禁止再克隆 Summoner 数据后改 ID。拆除快照仍由 Summoner 携带并优先恢复。
- 可手持操作的设施仍使用真实 Summoner 与独立 BuildingBody；以 `RequiresPlacementRequest` 从正式面板显式进入放置模式，功能模块先调用 `TryHandlePlacementAction` 仲裁使用，禁止同一次动作同时建造、装水或开面板。两端通过 `Building_Data.SharedModuleIds` 声明需双向转移的模块，由 `BuildingModuleStateTransfer` 按唯一稳定 ID 深拷贝；拆回必须在载体 `Load` 前注入，重放旧建筑快照后必须再覆盖当前载体的共享状态，避免手持期间的水量/库存修改丢失。转移仅允许单件载体，失败不得修改或消费源物品。
- 便携设施若由共享模块驱动状态贴图，Summoner 与 PlacedBuilding 都必须在各自当前 Item 定义中声明对应 `visual.spriteStates`；运行时视觉解析只读取当前载体 ID 的定义，不能假设落地本体会自动继承手持物的状态图。
- 当便携设施的 Summoner 与 PlacedBuilding 使用不同稳定 Item ID 时，`Building_Data.Role` 必须与当前载体 ID 对齐；模块加载、联机数据应用和面板绑定都要按 `SummonerPrefabId/BuildingPrefabId` 校正当前实例，禁止一个已落地实例的角色状态污染另一个同类手持物的“放到地上”按钮。
- 建筑 Summoner 一旦进入有效放置模式，玩家普通世界交互必须让位于放置：附近 `IInteractable` 不得因交互键、鼠标点选或同帧多输入被打开，交互描边也应隐藏；退出放置模式后再恢复普通目标选择。
- 通用建筑本体只提供 `Item + SpriteRenderer + BoxCollider2D`；伤害由 JSON `health` 注入，门、容器、工作台等反馈由独立 `IInteractable` Module 提供，不再依赖通用 `Mod_InteractReciver` 转发。
- 火把的手持职责与落地建筑本体保持分离：落地后统一使用 `Torch_Building`，`Torch_Summoner` 只能指向后者；手持攻击、命中 Buff 与光源均由 JSON 组合通用模块，禁止为火把保留专用运行时 Shell。独立手持物 `Torch` 不得内嵌 `Mod_Building`。
- 动态建筑保持 GameObject + Collider，但 Collider 只服务交互、受击等运行时物理；放置冲突与导航占地统一由 `BuildingOccupancyRegistry` 的离散世界格层维护，不得写入地形 `TileData`，也不得用 Physics2D Overlap/Collider Bounds 推导能否放置。
- `Module_Building` 不得再携带独立物理 Collider；其 `boxCollider2D` 运行时统一绑定所属 Item 根节点由 `visual.collider` 定义的碰撞体，避免模块默认框与建筑实体框叠加后产生额外阻挡或错误光照遮挡。
- 平台铺设由 `Tile_Block.groundPlacement` 与 `TileBuildingSystem.GroundPlacement` 负责，写入 `TerrainSupportLayer`，不替换底层水格、不占用 Blocking 层；预览、角色水域效果、建造和导航必须读取有效支撑面。扣料失败回滚支撑值，主动拆除撤销支撑而不重建水格。
- 静态岩壁/结构墙才使用 Blocking Tile；例如 `Wall_Stone`、`Wall_Wood` 只有 Summoner JSON，不创建动态本体定义。Tile 栈只通过 `Data_TileMap` API 读写。
- 新版 WorldModel 的玩家格子建筑虽使用 `ChunkTerrainData.BlockingTileId`，仍必须接入存档的运行时区块差量；不能只依赖 `MapSave.items`。
- 新版格子建筑的耐久或累计损伤必须与 `BlockingTileId` 一起保存在 `ChunkTerrainData`，并进入 `RuntimeTileDeltas`；否则区块回收或重载后会回满。
- 新版 WorldModel 的动态建筑 Item 不挂旧 `Chunk.RunTimeItems`；必须按 `Mod_Building` 的角色筛选，在 `ChangedItems` 中保存完整 `ItemData`，并在区块就绪后恢复模块状态/耐久。
- `BuildingShadow` 的 `sourceRenderer` 与 `sourceRoot` 必须来自同一对象层级；共享本体 Shell 资源本身没有 Sprite，预览应从 `RuntimeItemDefinition` 创建无模块的纯视觉轻量预览源，禁止给虚影恢复 Rigidbody/Collider，也禁止回退到召唤器图标或共享 Shell 默认图片。
- 手持物的根 Transform 会被拖拽、倾倒等表现逻辑临时旋转；创建 PlacedBuilding 时必须把世界建筑根旋转重置为 `Quaternion.identity`，禁止继承 Summoner 或拆除快照的根旋转。建筑自身需要的固定朝向应写在最终视觉资源/Renderer 的局部变换中。
- 建筑本体 JSON 的 `visual.collider` 只描述最终实体的物理碰撞范围，不再参与放置合法性或导航占地计算；当前动态可交互建筑和格子墙一样以吸附后的世界格作为放置槽。
- 带 `Building_Data.TileBlockId` 的建筑最终由 `TileBuildingSystem` 写入 Tilemap；预览根与一格占地以格心为锚点，图片直接读取目标静态 `Tile` 的 Sprite 和 transform，禁止继承动态本体或召唤器图标的偏移。高墙图片应将 Pivot 放在底部占地格的中心，避免用 Tile transform 平移同时移动 Grid Collider；虚影同步 Tile 的缩放与旋转。
- 新增玩家格子墙需同时配置召唤器 `TileBlockId`、`Tile_Block` 伤害与掉落、Unity Tile、生成 Profile 的 `tile.block.*` 和 `ChunkTilePaletteSO`；权威落地生命来自 `Tile_Block.damageProfile`，不能按召唤器的 `health` 推断。保留的迁移源 Prefab 也需声明 `Data.TileBlockId` 与 `BitData.TileBlockId`，并在 `BuildingShellMigrationTool` 标记为 Tilemap 建筑，否则重新导出会恢复动态本体链路。
- 资源装配脚本只是编辑器工具，新增格子建筑交付时必须包含实际生成的 Tile、Tile_Block、图片导入设置、Profile/Palette 映射与 Addressables 注册，不能只交付脚本和可制作的召唤器。`GameRes.TileBlockDict` 在资源会话中加载；补齐资源后可返回主菜单按 F5 重载，已有生成快照不会自动增加新 Tile 映射，仍须新建世界验证新映射。建筑关系的权威配置位于模块 `data.BitData`；`BuildingResourceCatalogValidator` 与静态目录检查必须读取该字段，不能只查 `parameters.Data`。
- 手机准星可以停在最大建造半径；格心吸附会产生每轴最多半格的偏差，距离校验应按目标格最近边缘判断，禁止直接用吸附后格心距离或 `Ceil` 取整决定预览与放置资格。
- 动态建筑相邻放置只比较 `BuildingOccupancyRegistry` 中的离散格记录；相邻格永远不因实体 Collider 接触而互相否决，同一格则由占用层直接拒绝。
- Summoner 只能由快捷栏的真实手持实例放置：`IsItemInInventory`、`BuildingShadow` 与源槽扣减都依赖 `InHand + Owner + CurrentSelectItemSlot`；库存菜单不得临时实例化 Summoner 后直接 `Act`。
- Summoner 成功放置后的源槽扣减必须继续走 `Inventory_Data.TryConsumeFromSlot/TrySetSlotItemAmount`，并在事务成功后按真实快捷栏槽位索引显式调用 `Inventory_HotBar.RefreshUI`；不能只依赖通用库存事件或仅在数量归零时同步手持物，否则部分堆叠放置后快捷栏数量可能延迟刷新。联机权威数量回写同样遵守这一收尾规则。
- `GamePlay` 程序集不能反向引用已依赖它的 `FlatWorld.Dialogue`；放置失败等玩家反馈由玩法层发布语义事件，再由 Dialogue 表现桥接，并且只能在实际 `Install` 提交失败时发布，禁止从逐帧虚影校验中触发。
- 建筑模块对 `DamageReceiver` 等模块的依赖必须在加载阶段从 `ItemMods` 注册表解析；禁止序列化嵌套模块 Prefab 的组件引用，模块缺失修复后原引用可能成为无效组件。
- 占地算法或安装/拆除顺序变化时联动 `flatworld-navigation` 与 `flatworld-map`。

- 主动拆平台同时检查前后建筑层、设施占地和角色／世界物品占用；返还物完成装配后才撤销支撑。返还装配异常必须清理未完成物件，不能遗留可捡返还物又保留原平台。

## 验证

- 检查预览与最终占地一致、注册/注销成对、失败路径无残留、快照可还原。
- 默认做静态诊断、编译和 Console；需要时运行 `Building.Smoke`。
- 测试入口：`Assets/GameTest/Building/BuildingSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
