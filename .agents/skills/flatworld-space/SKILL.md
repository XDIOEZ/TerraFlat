---
name: flatworld-space
description: "FlatWorld 太空会话、星区物理、船体航行/起降、对接、乘员与舱内环境。"
---

# 太空与星球

## 定位与按需读取

代码根：`Assets/5_Scripts/5-3_GamePlay/`，下表相对 `World/Space/`。

| 入口 | 职责 |
| --- | --- |
| `SpaceSession*`、`SpaceSessionState` | 权威会话、固定时钟、快照；`SpaceMgr` 负责表现 |
| `Universe/{SpaceCatalog,UniverseSectorFactory,SpaceSectorCoordinates,UniverseSimulation,SpaceSurfaceQuery}` | 目录、星区寻址、公转、冷地表查询 |
| `SpaceSession.Physics2D`、`SpacePhysicsWorld` | 独立 Unity 2D 物理场景 |
| `Ships/`、`Ships/Domain/` | 结构、机器、气密、乘员、碰撞、对接 |
| `ShipCruiseGuidance`、`ShipLandingGuidance`、`ShipAssemblyCache`（Ships 下） | 导航、起降、整体缓存 |

生命周期：`Core/Lifecycle/GameManager.Space.cs`、`World/Dimension/DimensionManager.cs`；面板：`Presentation/UI/{ShipPanelSession,SpaceLandingPanelSession}.cs`。PlanetData 与 `World/Map/Data/PlanetData.cs` 共用 partial。场景/Prefab：`Assets/3_Scenes/SpaceScene.unity`、`Assets/2_Prefabs/World/Space/`。

只读取当前改动涉及的参考，不批量加载：

- 星区、时钟、引力、坐标或碰撞：[physics-and-sectors.md](references/physics-and-sectors.md)。
- 装船/还原、推进、导航、对接、起降或乘员移动：[ships-and-flights.md](references/ships-and-flights.md)。
- 舱气、结构伤害、残骸或质量载荷：[cabins-and-damage.md](references/cabins-and-damage.md)。
- 渲染、镜头、船控交互、散物或音效：[presentation-and-interaction.md](references/presentation-and-interaction.md)。

## 共同边界

- 固定步：`IsGameplayReady → 机器/输入 → 地表起降 → 独立 PhysicsScene2D → CommitSurfaceContacts → 状态/表现同步`。权威模拟不依赖视图；卸载美术或玩家回地表不冻结无人船/残骸，暂停与离线不补算。
- 船、整体、自由物和星体保存所属 Sector；宇宙位置为整数星区 + 区内 double 米，显示原点不参与业务。太空坐标不交给地表 Chunk/NormalizePosition；地表镜像使用 WorldLocalPresentation。
- 整船、对接成员和残骸固定朝上且无角速度，散物可旋转，部件安装 QuarterTurns 保留。太空平移与接触由原生物理唯一推进，`ShipCollisionSimulation` 只管地表。
- 飞船、独立乘员和散物可穿过星球画面；进入地表只延续明确发起的起降流程。导航保留玩家真实目标，不自动改成停靠或降落。
- 地面船件始终复用普通 Item、机器、占地、管电网与存档；控制台打开不装船，点火才装配。落地成功恢复普通建筑并撤销船记录。机器/库存移交必须保留真实载荷，不以模板补造。
- 舱内由 `ShipInteriorWorldRuntime` 接通普通世界注册表、区块、独立物理和保存；普通 Item/机器/门/掉落/农业保持原后端，`ShipPieceState` 只作船体结构索引。起降/切分使用真实实例和可回滚的居民、掉落、地格移交；派生地板不重复画支撑层。联机沿用 `ChunkSaveRecord` 和普通物品原位同步；现场回归 `Systems/Space/ship-world-suite.json`，静态修改不代表运行验收。
- 新增普通机器、生物、地板沿原定义与能力自动进入船内，不维护船用物品名单或第二份实现；普通 `GroundPlacement` 即支撑地板能力。全新能力只扩一次通用接口，后续同类内容复用；船域只承担世界归属、坐标/表现投影和推进/对接等船体专属规则。
- 客户端只提交模板/控制台身份。测试船、补给、点火及船控操作均走正式权威事务；服务器按连接绑定玩家/世界校验位置、占地与资源，客户端等待正式回执再更新 UI。
- 点火 `ToggleIgnition(..., out reason)` 与 `RequestToggleIgnition` 传真实原因；超重含当前质量和可支撑质量。驾驶/退出/导航/降落/对接/目标气压走 `RequestShipOperation`，失败回原面板；对接区分等待双方与已连通，不能发包即成功或统一改成距离/缺电提示。
- 结构事务调用 `NotifyStructureChanged`：支撑、舱室、对接与几何以 StructureRevision 失效。Ships/Flights/Devices 的 `SpaceStateCollection` 权威版本与 ID 索引同步维护，加入前确定身份，恢复/卸载释放索引；只读乘员查询用 `FindPassenger`，不触发补建。
- 太空传送替换乘员状态时保留 `RealmRevision`；跨星区后正式观察者随世界身份递增版本，区内保持稳定。重置地表乘员须保留当前世界和位置，供联机观察与保存使用。现场回归 `space_gameplay`、`presentation_cache`。
- 太空电梯见 `SpaceSession.Elevators` / `SpaceElevatorState`：地面放置使用星球正式赤道地理带，地表 X 映射星球画面的倾斜赤道位置；站台锚点随星球平移、保持自身位置与朝向。两端独立仓库复用正式库存插槽，打包后货物归在途库存，到站自动入目的仓库；满仓余货保留在目的端接收缓冲，`CargoDestination` 独立于客运方向，待入库不阻断召回/乘坐，新批货待余货清空再发。快速回归 `Systems/Space/elevator_rules.lua`。

## 内容与回归入口

- `Assets/StreamingAssets/GameConfig/Space/solar-system.json`：主目录 (0,0)，`sectorSystems` 扩展星区，默认 (-1,0) 天狼星，其余为空域。
- 大型矿石小行星 `asteroid_iron/copper/rare` 复用正式起降和星球地表，`SurfaceRadiusTiles=128/112/96` 固定有限环绕范围，矿山规则在 `NaturalItems/surface-asteroids.json`；专用 Prefab `矿石小行星` 使用 `PlanetSurfaceStyle.Asteroid`。真空地表以 `weather.enabled=0` 关闭自然天气，低重力下降沿正式轨迹延长至重力允许的时长。
- 小型矿石由 `SpaceOreAsteroidCatalog` 读取 `Resources/Config/Space/ore-asteroids.json`，按中心星体与稳定身份确定矿簇、矿种及数量；`SpaceSessionState.OreAsteroids/GeneratedOreAsteroidSectors` 保留伤害和耗尽，不能随卸载重生。`ShipCombatBridge` 将岩石与船体/乘员统一排序，`SpaceSession.OreAsteroids` 先备齐冷矿物载荷再提交击碎，掉落复用 `SpaceLooseItemState` 拾取；碰撞仅用近身独立原生物理代理，外观复用矿石贴图。
- 小行星规则在 `Systems/Space/rules.lua` 的 `ore_asteroid_rules/ore_asteroid_profiles`，离线回归 `Tools/Tests/OreAsteroids/verify.py`；真实掉矿/拾取和大型降落采矿分别由太空玩法的 `ore_asteroid_drops/ore_asteroid_landing` 采样，规则或外部编译不能替代现场验收。
- `Assets/Resources/Config/Space/space-gameplay.json`：玩法；`Items/shells/space_ships.json`、`space_devices.json`：部件设备；`GameConfig/Structures/fixed-structures.json`：固定船型。ShipFixedStructureBuilder 经 ShipGroundBuildingStore 生成普通建筑。
- 船件能力 ID“船体部件模块”，Prefab `Module_ShipPart`，脚本 `Mod_ShipPart`；`SpaceSession.ResolvePartConfiguration` 也识别普通机器与线路，不能以无显式模块判未注册。
- 规划在 `开发文档文件夹/01_世界生成与环境/太空场景开发待办.md`，不把待办当已实现。
- 玩法入口 `Assets/StreamingAssets/GameplayTests/universe-suite.json`；`--cases ship_landing_gameplay` 聚焦正式 GM、起降与管路加油。执行/截图读 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md)，覆盖见 `Systems/Space/Gameplay/README.md`；`test.space` 巨大星区编号按字符串比较。
- 离线项目 `Tools/Tests/{ShipMass,SpaceCruise,ShipContacts}` 分别覆盖载荷/回收、精度/航行、跨界接触。领域样本及耗时不代表 Unity 原生物理、真实管电网、联机或画面通过；试玩中只静态修改，不操作 Play Mode。
