---
name: flatworld-space
description: "Use when: 定位或修改 FlatWorld 的太空场景、固定星系布局、自转、飞船巡航、发射降落、SpaceMgr 或 Space Prefab。关键词：SpaceSession、UniverseSimulation、SpaceMgr、SpaceScene。"
---

# FlatWorld 太空与星球

## 入口

- 权威会话：`Assets/5_Scripts/5-3_GamePlay/World/Space/SpaceSession.cs`、`SpaceSessionState.cs`；`SpaceMgr.cs` 只投影星体与星空。
- 生成与航行：同目录 `Universe/SpaceCatalog.cs`、`UniverseSimulation.cs`、`SpaceSurfaceQuery.cs`；`PlanetData.cs` 与 `World/Map/Data/PlanetData.cs` 保存星体地表身份。
- 船体：同目录 `Ships/`，领域结构、气密与对接在 `Ships/Domain/`；建筑后端、运动、乘员、环境、导航、爆炸与散落物按 partial 分工。
- 固定船型由 `GameConfig/Structures/fixed-structures.json` 配置，`ShipFixedStructureBuilder` 消费统一结构目录并复用 `SpaceSession.AddPiece/CommitPiece/RollbackPiece`；不得在 GM 按钮中硬编码布局。资源补给按模板成员身份匹配，共享罐按真实组容量处理，不能重复给每个成员灌满整组。
- 生命周期：`Core/Lifecycle/GameManager.Space.cs`、`World/Dimension/DimensionManager.cs`；正式面板在 `Presentation/UI/ShipPanelSession.cs`、`SpaceLandingPanelSession.cs`。
- 场景/资源：`Assets/3_Scenes/SpaceScene.unity`、`Assets/2_Prefabs/World/Space/`

## 不变量

- 主链：正式世界进入 → SpaceSession 恢复稳定星体、船体与船上机器作用域 → 唯一固定时钟推进 → 当前场景投影视图。SpaceMgr 不再另推宇宙时间。
- 宇宙坐标用 double 米，ViewOrigin 只影响表现；地表逻辑坐标与循环镜像通过 WorldLocalPresentation 换算，不把太空坐标交给 Chunk 生成、地表寻路或 NormalizePosition。
- 玩家太空传送走 `SpaceSession.TryTeleportPlayerToSpace`，调用方提供宇宙坐标和速度；会话负责脱离乘员支撑、驾驶与下落状态、迁移及失败恢复。GM 星球旁传送使用静态星体坐标和零初速，禁止只改 Transform；场景迁移仍限单机。
- 固定步须等待 IsGameplayReady，并按 CaptureShipContactPoses → 运动 → StepShipContacts → CommitSurfaceContacts 顺序结算。Kinematic 碰撞体负责表现，实际平动和旋转反作用由权威扫掠解算；碰撞、无人船和冷地表损毁不依赖可见性。
- Landed/Floating 船体不参与动力积分与船间动态碰撞，机器、气密环境和自由乘员仍按原时钟运行；起飞判定前同步真实载荷。运动中质量计算复用会话工作区，不每次分配部件样本。
- `SpaceSession.FindPiece` 走权威部件索引：Load、放置、回滚、分裂和结束会话必须维护索引；结构变更时才强制重绑 `ShipPieceView`，不让逐帧表现重复查询机器节点。
- 星体接触与船体接触比较同一步时间 Fraction，先撞船后须从真实碰撞时刻重积剩余轨迹；不能提前切地表作用域导致漏碰撞。
- 地表切换复用 DimensionManager 的黑屏、玩家快照与失败恢复；读档场景依据已保存飞行阶段，避免迁移中旧 CurrentSceneName 覆盖目标。未进入就绪状态不推进下落。
- `PlanetData` 跨两个目录的 partial；序列化字段变化必须同时检查并联动 Data Skill。
- 星体位置由静态 `PlacementDistanceMeters/PlacementAngleRadians` 与星系种子在加载时确定，不进行公转；时间只驱动自转与地表环境，`GetRotationRadians` 支持逆向周期。
- 太空主体只使用 `SpaceMotionState` 的双精度位置和速度，`UniverseSimulation.StepMotion` 负责无引力直线移动及星球连续接触；船体 `Cruising` 阶段按配置速度、加速、刹车和转向上限驾驶，船间碰撞仍由 `ShipCollisionSimulation` 独立结算。
- 对接拓扑由 `ShipAssemblyCache` 按船体、接口与确认状态快照复用，运动和真实质量仍每步刷新；模拟与即时查询必须使用不同缓存，避免查询改写正在结算的组合。断裂、接口毁坏、重连与会话退出都不能沿用旧成员关系。
- 自动航行由 `ShipCruiseGuidance` 处理减速、转向及星体外侧航点，同一对接整体只保留最新任务，到点结束、手动输入接管、缺燃料制动等待；航线遇到星体先在近侧停靠，不冒充自动绕行或自动降落。部分燃料限制实际速度变化而非仅缩短期望速度；松键免费刹车不扫描推进设备。光纤查询复用会话工作区，但每次仍检查真实连接。
- `SpaceSession.LateUpdate` 先以玩家双精度位置更新 `ViewOrigin`，再统一投影星球、飞船和乘员；不要恢复五千米阈值跳变或让 `SpaceMgr.Update` 提前使用旧原点。
- Prefab 名经 `RuntimePlanetName/PrefabName` 和 GameRes 解析；移动/改名同步检查 Addressables。
- 星球旅行使用 `WorldAddress.PlanetId + DimensionManager`，不为每颗星球造一次性 Scene 链。冷地表查询与落地损毁使用冻结生成 Profile 加最新 Chunk 差量，禁止读取当前活动星球替代目标星球。
- 船内重力判定只读支撑地板与供电；密闭舱的环境查询不请求船外地表，外界查询优先使用已加载温度场。冷地表生成 Profile 按存档和资源会话缓存，资源原位更新后必须失效。
- 船体部件能力 ID 为 `船体部件模块`，Prefab 为 `Module_ShipPart`，MonoBehaviour 文件必须同名 `Mod_ShipPart.cs`；配置位于 `space_ships.json`。设备、太阳系和飞行参数分别在 `space_devices.json`、`GameConfig/Space/solar-system.json`、`Resources/Config/Space/space-gameplay.json`。
- 船体动态表现复用 `WorldSortingManager`：承载地板、普通管道、电线依次用 `vehicle` 的 -2/-1/0 偏移，机器及其它部件用 `building`，材质取物品定义，附属箭头继承主体排序层；禁止硬编码 Default 负 Order，否则地表会遮住地板。普通管道按本船正式端口掩码选择 `pipe0..pipe15`，节点状态/删除事件只在端口拓扑签名变化时刷新相邻管线，会话释放须退订。
- `UI_Ship` 只允许玩家在标准交互半径内靠近本船实际 Console 时打开，远离后关闭；地板和墙壁不承担控制台交互，也不注册到空间目标查询。各设备共用互斥的飞船面板会话，不能叠加多个窗口。
- 舱门只接受交互键开关，`ShipPieceView.CanPointerInteract` 必须拒绝舱门鼠标点选；交互只调用 `SpaceSession.TryToggleDoor`，禁止瞬移乘员。`DoorOpen` 随部件保存，步行、自由碰撞和视图 Collider 统一读取 `BlocksPassage`。双扇滑门只负责表现，开门仍保留交互 Trigger，通道有人时拒绝关闭；正常开关不改变 `SealsAtmosphere`。

## 规划约束

- 新玩法以 `开发文档文件夹/01_世界生成与环境/太空场景开发待办.md` 为准；管道、气体与气压防护为前置依赖，不把待办视为现成功能。
- 恒星系生成与各星体地表配置分离；首轮太阳系、玩家初始地球，行星和恒星均可游戏化登陆。气态巨行星气压极高，太阳“核聚变中”地块极热、强减速、高伤害；通过材料耐受表达普通船爆炸与后期存活，不硬编码恒星登陆必死。
- 地表 Lifting 只推进高度、垂直速度与燃料；船体物理坐标保持静态，靠 Sprite 偏移表现升空，点火时计算的载荷不需要每帧重复读取。SurfaceDescending 仍使用原有物理运动与碰撞；抵达预设高度及最低速度后进入巡航。
- 飞船地板四邻接，同船多控制台；独立船体必须通过墙壁类对接接口及双方控制台确认形成整体，普通接触、补地板和光纤不替代接口。任一方操作接口可解除该接口对并清除双方确认，重连须重新确认；按剩余有效连接路径重算整体，保留成员与接口身份，不复制库存或清零分离速度。分裂部分符合控制台连接、供电、燃料和引擎条件即可继续驾驶，库存与网络按船体本地格子作用域处理。
- 非驾驶移动、冲刺和宇航服推进按屏幕方向操纵玩家，驾驶按船体轴向操纵飞船；玩家能力不替代引擎。承载地板毁坏释放乘员并保留速度，失去支撑的设备转掉落物。
- 生物获得地面支撑后恢复直接移动；有效重力工作方块覆盖的舱内优先用地球重力，飞船落地后仍生效且不与当地重力叠加，出舱后用当地环境规则。内部人工重力与外部无引力巡航分开。
- 墙体边界闭合且内部地板完整才密闭，停供保留舱气，隔离门正常开关不泄压；破口跟随外界，修复不还原旧气，太空补舱仍真空须重新供气。墙体（含隔墙）只比较船体外部气压耐受范围；船体、墙壁、地板另配置外部温度耐受范围，越界距离乘各自系数计算伤害，归零统一摧毁并刷新密闭。结构损伤与生物冷热规则分开。
- 密闭舱自动维持正常舱温，无需温控工作方块；破舱随外界温度，补舱恢复正常舱温但不生成气体。完整承载地板阻隔危险地块对乘员的直接接触伤害，按支撑关系判断；船体仍承受外部温压损伤，撞击与爆炸照常结算，不在角色核心增加船型或地块特判。
- 密闭舱流体调用统一使用 `ShipAtmosphereService.MinimumCabinGasSpaceLiters` 作为极小正预留空间，不能把总容积重复传入 `FluidInventory` 的最小气相空间参数。
- 飞船及独自玩家复用下落选址，独自降落不计算宇航服推进；倒计时随预计剩余时间刷新，触地立即结束，未选或无人船附近随机并保存，按最终速度结算。飞船允许重新点火救船；落点中心地块决定整船陆海状态，海面浮船禁驾，再起飞复用现有条件，残骸攻击回收。
- 太空船与残骸运动不能因玩家回地表或视图卸载而冻结；船上必要网络保持活动，暂停和离线不补算。
- 结构冲击与燃爆复用唯一伤害／事件入口；燃料实际库存只释放或消费一次，不能复制气罐压力爆炸预算。
- 航空燃料是罐内接近 2:1 的氢氧气相库存用途，按 mol／统一标准气量判定，不新增替代组分的燃料物质；引擎按配方原子扣量，普通管口仍随机出气。宇航服推进与供氧分罐。

## 验证

- 静态星体位置与时间测试使用确定种子/步长；验证速度上限、持续移动、降落接触、Load/Save、Prefab 解析和场景清理。试玩运行中只静态修改，不暂停 Unity。
- `dotnet run --project Tools/Tests/SpaceCruise/SpaceCruise.Tests.csproj -c Release` 离线验证巡航、对接缓存与光纤算法；专项耗时和分配结果不代表 Unity 整场景帧率，也不替代起降与碰撞试玩。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
