---
name: flatworld-space
description: "FlatWorld 太空会话、椭圆星系、船体航行/接触、对接、乘员、舱内环境及场景迁移。"
---

# 太空与星球

## 代码与内容

代码根目录：`Assets/5_Scripts/5-3_GamePlay/`；下表入口相对 `World/Space/`。

| 入口 | 职责 |
| --- | --- |
| SpaceSession*、SpaceSessionState | 权威会话/时钟/快照；SpaceMgr 只投影视图 |
| Universe/SpaceCatalog、UniverseSectorFactory、SpaceSectorCoordinates、UniverseSimulation、SpaceSurfaceQuery | 星区目录、整数寻址与区内运动、冷地表查询 |
| Ships/、Ships/Domain/ | 船结构、气密、运动、碰撞、乘员、导航与对接 |
| Ships/ShipCruiseGuidance、ShipLandingGuidance、ShipAssemblyCache | 惯性推进/巡航、手动降落范围/实际姿态、对接整体缓存 |

- 生命周期：`Core/Lifecycle/GameManager.Space.cs`、`World/Dimension/DimensionManager.cs`；面板：`Presentation/UI/{ShipPanelSession,SpaceLandingPanelSession}.cs`。
- SpaceMgr 激活/维护星区时登记时间与采光，全部星区引用 `SpaceScene` 的统一时间源；跨区不重置昼夜，也不依赖重建玩家触发世界进入事件。UI 根由持久 UIManager 持有，星区卸载不销毁 HUD。
- PlanetData 是太空目录与 `World/Map/Data/PlanetData.cs` 的 partial；场景/Prefab 在 `Assets/3_Scenes/SpaceScene.unity`、`Assets/2_Prefabs/World/Space/`。
- 内容：`GameConfig/Space/solar-system.json` 的主目录在 `(0,0)`、`sectorSystems` 扩展其他星区，默认 `(-1,0)` 为天狼星，其余为空域；`Resources/Config/Space/space-gameplay.json`、Items/shells/space_ships.json 和 space_devices.json 配置玩法。固定船型在 `GameConfig/Structures/fixed-structures.json`，ShipFixedStructureBuilder 经 ShipGroundBuildingStore 逐件生成普通地面建筑，不提前创建飞船。
- 功能规划来源：`开发文档文件夹/01_世界生成与环境/太空场景开发待办.md`，未完成项与运行系统分开。

## 时钟、坐标与接触

`世界进入/恢复 → SpaceSession 固定时钟 → 机器与输入 → 地表起降 → 独立 Unity 2D 物理 → 手动地表提交 → 数据与画面同步`

- 固定步等待 IsGameplayReady；SpaceSession.Physics2D 只登记输入、质量与形状，SpacePhysicsWorld 使用真实 Dynamic Rigidbody2D/Collider2D 和独立 PhysicsScene2D 推进太空飞船、残骸、自由玩家及散落物，随后读取结果到业务状态。不得再对这些物体调用自研积分、扫掠或冲量；ShipCollisionSimulation 仅处理地表作用域。地表起降和手动落地继续原流程，太空经过星体不自动进入地表。
- 物理代理不依赖画面加载；稀疏网格只分配局部物理岛，近处物体共用场景，场景及代理复用。碰撞圆支持物品之间、物品与玩家及船体互撞；支撑乘员的圆归整船，质量已在整船内，独立玩家和散落物不重复加入船重。地板等非阻挡部件对自由圆忽略碰撞，舱壁与关闭的舱门保留阻挡。原生接触回报只结算损伤，不再追加自研反作用。
- ShipColliderGeometry 按本地格去重、阻挡优先并合并同类连续矩形，保留洞和分离区域；StructureRevision 或门开关改变才重建。太空计算整体使用阻挡/非阻挡两个 CustomCollider2D，支持乘员圆归阻挡组；几何未变不重新提交。地表纯数学接触不走这条合批链。
- 采用 Unity 2022.3 的 velocity/drag API；局部物理以米为单位，读回原生位移差保留双精度坐标余量，同一引力源中的物理岛以星体公转为平移参考。每固定步至少4个物理子步；只在独立场景 Simulate 期间临时提高平移/旋转上限，并在 finally 恢复，不能永久改地面物理。视图刚体只显示结果，所有权在权威端；会话重载、结束和销毁释放计算场景。
- SpaceSession 跨场景保留，但 Single 加载会卸载独立计算场景；物理岛复用前检查 Scene 有效/已加载及 PhysicsScene2D 有效，失效时清理代理与碰撞体缓存，按权威业务状态重建场景和碰撞过滤。
- 宇宙位置是 `SpaceSectorId(long X/Y)` 加所属区内 double 米，星区边长256000米、局部范围 `[-128000,128000)`；跨区用 Normalize/Relative，先算整数星区差再转局部距离，禁止合成巨大绝对 double。Ship/Assembly/Motion/Body 与接触快照显式保存 Sector；成员、重心、点速度与接触成对转换到指定参考区，不用全局静态上下文。
- ViewSector/ViewOrigin 只影响显示；LateUpdate 先更新玩家投影，换区保持同一显示平面，离原点4096米才整体平移并通知镜头。创建自由物用 UnprojectSectorPosition，投影视图传 SectorPosition；地表坐标/镜像经 WorldLocalPresentation，不交太空坐标给 Chunk 或 NormalizePosition。
- 太空船的可见距离围绕本地乘员权威位置计算，不围绕浮动原点；支撑乘员姿态和驾驶镜头按船的当前世界归属更新，不能因美术裁剪解除驾驶或回退到旧 FreeMotion 坐标。
- 飞船可见性、乘员投影和 `GetPassenger` 热查询共用无捕获分配的 `FindPassenger`；只读路径不创建乘员，正式 `GetPassenger` 仍可补建。
- 武器坐标与乘员升空表现的可选组件热查询用 `TryGetComponent`，缺失仍按原规则回退；不缓存缺失，保留运行时添加组件的生效时机。
- SpaceSessionState 版本2保存 Sectors 与 SimulationSeconds，Universe 只是主星区运行别名；所有目录由同一个固定时钟推进。BodyId/PlanetId使用全局实例身份，TemplateBodyId用于所属区模板查询；主地球PlanetId沿用当前存档主世界名，其余星体不能复用本地earth/moon身份。
- 星体按配置的半长轴、偏心率、近日点方向和恒星周期做 Kepler 椭圆公转，默认天文时钟加速720倍；父星先更新，正常固定步复用前后位置与速度，自转读权威时钟。会话载入由 SpaceCatalog.RebindUniverse 应用当前目录，保留身份、种子、初始相位与时钟，不迁移船体坐标。
- SpaceGravity 按半径和表面重力推导质量，W半径按质量立方根缩放，边界释放的基准中心速度按质量平方根缩放，参考速度默认8米/秒（原80的10%）；W 内只选质量最大的一颗，星体自身仍按目录公转。游戏代码只给原生刚体施加中心引力、相对大气阻力和公转随动；AtmosphereThickness 为玩法厚度，0 无阻尼，正值加速衰减且保持欠阻尼，近中心低速时停止相对漂移。离开 W 取消引力与阻力，不随自转、不依赖父子节点。参数在 space-gameplay.json、solar-system.json，随会话重建缓存；解析运动函数只作离线数学参考，不参与运行时。
- Landed/Floating 不积动力、不做船间动态碰撞，机器/气密/自由乘员继续；起飞先同步真实载荷。地表 Lifting 只推进高度/垂直速度/燃料，物理坐标静态、Sprite 偏移显示；下降继续原运动碰撞。
- 视图卸载或玩家回地表不冻结无人船/残骸，暂停与离线不补算。FindPiece 权威索引随装载/放置/回滚/分裂维护，结构改变才重绑视图。

## 船体、导航与乘员

- 地面船件与手放建筑共用 Item/地表机器、占地、管电网和存档；打开控制台不装船，ShipGroundAssembly 在点火时收集四邻连通地板和完整承载建筑，批量移交真实机器后复用 AddPiece/起飞。落地按最近格线和90°统一变换，恢复普通建筑并撤销船记录，地面无 ShipView/ParkedPieces。外部管路按正式端口/阀门供料，不新增自动灌油规则；未加载地表延迟到场景就绪再物化，落点冲突保留整船阻挡并报告。
- 四邻接地板定义船体，普通接触/补地板/光纤不形成对接。独立船通过墙类接口和双方控制台确认形成整体；断开清双方确认，按剩余连接重算成员，保留身份/库存/分离速度。
- ShipAssemblyCache 复用拓扑，质量/运动每步更新；模拟与即时查询使用独立缓存。机器/库存按船本地格子作用域运行，分裂后满足控制台/电力/燃料/引擎条件即可驾驶。
- 对接整体只保留最新导航任务，手动接管/到点结束；断电、停机或缺燃料保留任务与平移惯性，恢复动力后续航，不能无燃料自动制动。导航保留玩家选择的真实目标，可到星体画面上方或穿过星体，不自动改为外侧停靠或触发降落。真实剩余燃料限制速度变化，光纤查询检查实际连接。航空燃料为罐内近2:1氢氧气相用途，按真实 mol 原子扣量，不另造燃料物质。
- 手动 WASD 在已有速度上累加推力，松键继续滑行，反向输入渐进制动；转船头不抹掉已有侧向速度。太空操纵检查点火与真实动力，起飞进入太空保留点火开关；无输入不耗推进燃料，碰撞造成的超速也不能被无动力限速静默清除。
- 巡航/下降平移由 ShipThrustAllocation 按所需速度变化选择朝向：向左推用右侧向外喷嘴，四向/斜向及导航制动共用规则。成员船角度与安装 QuarterTurns 决定实际推力方向；同向引擎按推力容量平均供油比例，缺某侧引擎/燃料只失去该方向，不借其它喷嘴。Q/E 选相对整体重心力矩同号的喷嘴，保留直接角速度；无偏心推力则不能转向。
- 引擎本地 +Y 为气相燃料输入、-Y 为喷口，朝向随安装旋转；每台只从本船正式气相供给路径取料，端口/阀门/选择器及逐段共享吞吐限制供给，不从未接管的罐或对接船跨网扣量。GM测试船模板保留舱门到控制台前两格中央通道，设备与供电改位同步更新。
- 驾驶 Q/E 直接控制整船角速度，`CruiseTurnDegreesPerSecond` 默认 45°/秒；松键停转、反向立即换向，同向多控制台不叠加超速、反向抵消。导航共用转速上限，转向仍需有效供电、可用引擎和真实燃料，不按力矩与转动惯量累加按键角加速度。
- 非驾驶失重玩家用 WASD 按屏幕方向累加推力；`Mod_Mover.SmoothSpaceVelocity` 松键保持当前速度，没有摩擦减速，反向输入逐渐制动。FreeWalkVelocity 只记录主动移动分量，其差量合入 FreeMotion 总速；离船继承船上所在点速度（含旋转）与步行速度，12米每秒上限只管主动分量，不消除继承/碰撞速度。太空禁跑，WASD 不消费推进气体；人工重力直接步行，驾驶按船轴向，支撑毁坏释放乘员。已有支撑和步行后支撑均用所属船的 LocalX/LocalY 校验地板，不依赖滞后的表现坐标或场景可见性；自由乘员再探测新支撑。
- 太空玩家本体与镜头固定朝上，`Mod_Mover.SetSpaceUpright` 清零本体/刚体转角和角速度，同时保留 Mod_TurnBack 的鼠标左右翻面和手持物瞄准。驾驶只跟整船中心，Q/E 仍转飞船；CameraFollowRotation 关闭跟随旋转时恢复绝对零角。
- 地面支撑恢复直接移动；有供电人工重力的舱内使用地球重力，落地仍有效且不叠当地重力，外部用当地规则。船内查询只读支撑/供电/舱气，密闭舱不请求地表环境。

## 环境与迁移

- 闭合墙边界及完整内部地板决定密闭；停供保留舱气，正常舱门不泄压，破口随外界，修复不返旧气。密闭舱维持正常舱温，补舱只恢复温度，仍需真实供气。
- 舱室环境一次索引查地板与房间，敞开舱用 WithOutside 补真实外温压；不可变真空源共用单例，结构环境每船缓存压力资格委托并在每次结算前更新外压。
- 墙体压力耐受比较船外气压；真空占位温度不作为气体传热伤害，密闭舱内设备读舱温，有气外界仍按材料耐温结算。压力沿持续断裂线损伤，破坏后重取线；触地、船间冲击和燃爆共用局部断裂线及真实能量预算。结构实际掉血由统一事件聚合红字；Cabin FluidInventory 最小空间使用 MinimumCabinGasSpaceLiters。
- 生物与结构环境损伤各有职责，星体可登陆性由材料耐受表达。冲击/燃爆复用唯一伤害入口，真实燃料库存和压力预算只释放/消费一次。
- 船体与残骸均按部件映射结构血量，不设共享血池。PhysicalDefense 从部件配置或真实定义读取；DisableHealth/MaxDisableHealth 单独累计功能损伤，归零停工但保留结构，Health 归零才剥离。正式动力、导航和生命支持共用 IsOperational/IsPowered。
- 普通攻击冻结首次命中：设备/墙优先线路，再地板；SelectAtContact 把边线接触钳进真实候选占地，避免伤到邻格。摧毁上层部件的同一发不穿透地板。冲击/燃爆先均分冻结沿线名单的原始预算，再逐件扣防御，不按部件数放大伤害。
- 地板四邻分裂后有可用控制台和引擎的部分保留飞船，其他部分成为 ShipWreck；先移交乘员、点火、导航与端口归属再冷封残骸。每块一个无原设备模块的 Item 保存各部件耐久与库存，攻击具体部位还能再次拆分；WreckGuid 只标记外壳身份。失去支撑的部件按邻接合成残骸，普通散落物仍走 SpaceDropView 正式拾取事务。
- 每个星区一个动态表现 Scene，身份用 SpaceSectorCoordinates.SceneKey；判断太空统一 SpaceMgr.IsSpaceScene。SpaceSectorStreaming 仅提前请求行进方向的目标区，越界后保留源区直到离开目标侧过渡带，边界可自由移动与折返；美术晚到不阻挡飞行。多人共享区场景及租约，跨边界仍可见、攻击、碰撞与交互；无玩家区域仍由独立计算场景推进物理，不加载对应美术。
- TryTeleportPlayerToSpace 接收所属星区的坐标/速度，负责脱支撑/驾驶/下落状态及失败恢复。地表/太空维度迁移复用 DimensionManager 的玩家快照和原有加载页；普通星区跨界只调整租约及当前Scene，不调用黑屏维度迁移。读档按归属星区/飞行阶段恢复，未就绪不下落。
- 太空死亡以 SpaceMgr.IsSpaceScene 判断来源，回主地球的固定主世界出生点；SpaceSession.Respawn 在完整维度重生事务前解除驾驶/支撑/下降状态，迁移期间用中立乘员与 transitionRunning 防止重建太空归属，成功清记录、失败恢复。星区Scene不能补写为主世界出生点。
- 飞船可自由经过星球画面，不自动撞毁或切地表；只有控制台 `land`/降落按钮登记 RequestedLandingBodyId，有效范围含船半径及 max(100米,星体半径/4)。CommitSurfaceContacts 在碰撞后开始下降；起飞目标速度由 TakeoffSpeedMetersPerSecond 配置，完成只看速度与至少12秒的时长，不检查高度或星球半径；正常降落约10秒，每步通过正式引擎供电/管路消耗真实燃料。断电或缺油转真实坠落，可再点火救船；持续受控下降且触地垂直速度≤0.5米/秒才免冲击。起飞完成距星体边缘为船半径加12米、只继承星体平移速度，首次镜头留出星体边缘。独立乘员/散落物经过星球画面仍留在太空，地表下降只延续已经发起的起降流程。
- 船下降自动接管本船控制台并关闭船控窗口，WASD 按船轴平移、Q/E 旋转，镜头跟整船中心并固定朝上；同次下降 Esc 退出不强制重进，触地或重返太空恢复原镜头。下降按键的平移/制动/转向需要实际供电、引擎与燃料，但独立于点火救船的垂直推力；无输入、无动力或无人保留平移惯性。触地用真实船体位置，不能由 UI 选点或随机跳点。独自玩家保留附近选址 UI 与随机落点。
- 下降末步先按触地 Fraction 处理船体、乘员和散落物接触，再 CommitSurfaceContacts 结算落地，使用最终速度与落点中心决定陆海；可重新点火救船，海上 Floating 禁驾。冷地表读取目标冻结 Profile+最新差量，不加载地表 Scene 代替查询。

## 表现与交互

- `ShipSurfaceState` 共用空地规则：点火提高排序，贴图 `SurfaceVisualRise` 达到1单位切空中；`ShipSurfacePresentation` 对独立船件或整船 Collider 叠加同一局部升高偏移并切 Trigger，乘员/散物同步高层级。`ShipCombatBridge` 按攻击实际世界归属和升高后的形状查询，空中船体要求 `Projectile | AirborneTargets`，不依赖 Host 当前可见场景。

- 地表升高共用 ShipLandingGuidance.SurfaceVisualRise，不限制最高显示距离；船体、玩家、散落物与驾驶镜头保持一致。迁移前捕获船体外观，ShipTakeoffLoadingPresentation 在维度加载页按非缩放时间持续滚动起飞气流，场景准备完成/失败后随加载页释放。
- 乘员起降与单独下落移动整个玩家根节点和刚体，不再偏移 Sprite；ShipSurfacePresentation 按地面基准叠加一次高度，离地即把同刚体实体阻挡切 Trigger，落地、重生、休眠或卸载恢复。既有 Trigger 与独立子刚体保持原态；手持物随根节点移动。地面存档/联机坐标去除高度，TryGetSupport 的表现查询扣除船体升高，权威查询不扣，控制台距离继续读未升高坐标。
- ShipSurfaceShadowPresenter 每船合并去重占地 Mesh，复用 ActorBlobShadow 材质与 ground-shadow 排序；地面位置不加升高偏移，真实高度控制缩小/淡出，日照和柔化沿用共享阴影设置，结构或柔化变化才重建，离开地表/会话结束释放。
- 星球/星空场景对象仅在太空Scene内生成；SpaceMgr按星区租约分帧准备资源，地表初次进太空仍等待 PreparePresentationForCamera 预建星空并完成一帧。SpaceStarfield用两次SectorPosition之差算视差，不受换区/浮动原点影响；常规地面视野不绘制宇宙背景，权威会话及无人船仍继续运行。
- 单机表现复用：`SpaceMgr.PresentationCache` 最多保留2个完成星体装配的星区根，禁用后移入持久根，旧场景卸载完成再恢复；State/资源代际改变或 EndSession 清理。`SpaceSession.ViewCache` 保留16船根/2048部件，已装配船跨场景时休眠复用，正式落地交回普通建筑后清理本船视图；联机沿用释放语义。Lua 分别检查太空表现复用与普通建筑起降交接。
- 地表拉远的 UniverseOverviewRendererFeature 是独立观察画面：SurfaceZoomTransition 只平滑 60～90 的直接淡入淡出，不等待满屏云；地表云圈始终固定玩家周围的世界半径。90 时画面高度为星球直径的 30%，之后倍率拉远才露出整球与宇宙；读真实星体时钟/位置和原 Prefab 外观，未进入星区只建表现目录副本，不调场景租约、玩家迁移或权威 GetUniverse；驾驶覆盖和洞穴不使用此观察画面。
- 太空驾驶 `DrivingCameraMaxSize=120000`，滚轮乘/除 `DrivingCameraZoomMultiplier=1.25`，退出恢复原尺寸。宇宙坐标 HUD 读双精度位置；SpaceStarfield 用固定420点单网格与 Resources/Space/Starfield Shader 实现三层轻微视差、白/红/蓝色温与独立闪烁，拉远不增加对象或重抽样本。
- 驾驶高度 HUD 经 `SpaceSession.FlightHud.cs` 的 `TryGetShipFlightHudSnapshot` 只读真实起降高度；普通建筑移交成功才写乘员 `LastShipLanding` 凭证并沿既有快照复制，不能把退出驾驶或船记录消失当作落地完成；主动退出用 `RequestConsoleExit` 通知本地 HUD，手动 `EnterConsole` 解除退出锁，自动接管/释放不发这两种通知。
- PlanetSurfacePresentation 保持每体四顶点与共享 Resources/Space/PlanetSurface 材质，GPU 绘制球面细节、云层、气体条带、大气边缘和太阳日冕；MPB 接收种子/太阳方向，远景按导数淡出，太阳自发光，其余保留 URP 2D 光照。外观参数归各星体 Prefab。
- 船地板/管道/电线使用 vehicle 的 -2/-1/0，设备用 building；材质与 rendererLocalScale 来自定义。船管线读本船正式端口掩码，拓扑变化才刷新，会话结束退订。
- ShipColliderPresenter 只服务仍归 SpaceSession 的已装配船，使用一个表现刚体与共享 CustomCollider2D；待移交的落地船也保留阻挡。ShipPieceView 只留贴图/交互，地面还原成功即清理船表现；太空维修仍可用，跨区只迁移整船根。
- 船体升空排序和共享 Collider 偏移在 ShipColliderPresenter 每船只更新一次，ShipPieceView 不重复操作整船根；静止舱门不重复写两扇门的 Renderer 属性，开关与 Rebind 仍正常刷新。
- ShipPieceView 在创建、休眠恢复或换船时缓存 MachineWorld 的稳定 ship 作用域键，部件数字 GUID 只在身份绑定时解析；灯光、管线与交互热查询不再逐次拼键或解析数字。
- 船体支撑查询经 `ShipAtmosphereService.TryGetFloor` 复用结构修订索引，按格返回首个活地板，不再遍历部件或构建 Footprint 迭代器；结构变更后同步失效。
- 太空表现由 SpaceSession.Presentation 与 SpacePresentationPose 共用前后固定步姿态和一个渲染进度，物理仍为50Hz；船格原点、乘员本地位置和驾驶中心统一插值，表现刚体关闭原生二次插值。屏幕选格按表现姿态反算，支撑/丢弃/存档读权威位置；联机样本按本地收包时间插值，远端乘员在统一表现同步事件后投影。传送、归属切换和会话结束重置历史，普通跨区与浮动原点保留双精度连续性。
- 太空驾驶通过 `Mod_Cam.FollowMode.PresentationLocked` 直接跟随同一插值中心，关闭镜头额外 XY 阻尼/前探并固定 LateUpdate；地表驾驶仍用玩家设置，释放恢复原更新方式与最新偏好，不靠降船速或缩宇宙掩盖屏幕漂移。
- 散落物只有地表升降时叠加贴图高度，太空掉落不继承船体残留地表高度；偏移保留原局部位置，触地、拾取和卸载时恢复。
- 玩家须在实际 Console 的交互半径内开 UI_Ship，离开关闭；自动导航器仅经光纤供能力，线路/地板/墙不作控制台入口。飞船面板会话互斥，CanOpenShipPanel 共用打开/切换/刷新校验。
- 船上设备 UI 只由交互键打开，ShipPieceView 拒绝鼠标点选；Esc 优先取消导航选点，再退出驾驶，右键只使用手持物。
- 驾驶进入与联机接管共用 BindConsoleControl：保留本船乘员 LocalX/LocalY，首次绑定或换船从玩家当前位置换算；驾驶期间只随船同步，不吸附控制台中心，退出沿用清输入与恢复镜头。
- 船上普通机器按 `MachineDefinition.IsConnectionLine` 排除连接线路，不能只看 `ShipPieceKind`；落点使用真实部件碰撞范围。机器休眠会释放交互对象，实际开始交互时须在本船作用域重新取得当前对象，外观缓存不能作为永久操作入口。
- 舱门只经交互键 TryToggleDoor，鼠标点选拒绝；DoorOpen 持久化，通行/碰撞读 BlocksPassage，开门移除该格阻挡并保留数据交互范围，通道有人拒绝关门，不改变 SealsAtmosphere。
- 船部件能力 ID 为“船体部件模块”，Prefab Module_ShipPart，脚本 Mod_ShipPart；部件分类共用 SpaceSession.ResolvePartConfiguration，包含普通机器与线路的自动识别，测试不能把缺少显式模块误判为物品未注册。固定船补给按成员身份及共享罐实际组容量分配，GM 船复用正常起飞条件。
- 引擎喷焰由 ShipEngineExhaustPresenter 读取 SpaceSession.GetEngineOutput；固定步清空出力，起飞/巡航成功消耗真实燃料后才发布，不按移动或 Ignited 猜测。局部粒子随安装朝向、机身与升空偏移，按 vehicle -3 排在船地板/管线后面，停推残留继续同步并自然消退，禁用/卸载清理；出力不进入存档。

## 载荷读取与离线检查

- 宇宙与太空回归使用 `GameplayTests/universe-suite.json`，七个主题可多选；`--cases ship_landing_gameplay` 单独复用正式 GM、起降和普通管路加油，跳过完整航行的导航/跨区。编写、现场截图与执行契约见 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md)，覆盖边界见 `Assets/StreamingAssets/GameplayTests/Systems/Space/Gameplay/README.md`。`test.space` 返回的巨大星区编号按字符串比较；领域结果不等于场景/联机/视觉验收，普通跨区应保持玩家连续且不触发维度加载。

- 太空冷散落物的刚体质量按 Snapshot 变化解码更新，代理和形状复用；地表散落物仍先筛空间、高度与扫掠范围，再读真实质量。ShipGeometry.HasFloor 直接检查旋转矩形，无格子/迭代器分配。原生碰撞、场景重建、跨区连续性与性能需要 Unity 验收，离线数学参考及地表接触回归不能代替原生物理运行验证。
- 方块引擎严格占一格、默认喷口朝本地 -Y；ShipEnginePlacement 共用放置预览/提交/可用引擎收集的船体外缘与喷射净空检查，后续建造也不能堵喷口。气密边界只读 SealsAtmosphere 能力，边缘引擎可替代舱壁；固定船型同步调整引擎、舱壁和同格电线。
- ShipMassReader 按源 JSON/二进制身份复用解码，并监听真实库存、堆叠、模块载荷和流体质量变化；未变来源复用质量，动态乘员/散物位置仍逐步更新。ShipMassLedger 按来源增减质量、两个一阶矩及惯量，保持重心变化前后的船体点位置/速度；资源、流体图身份变化及会话结束释放绑定，分裂移交前丢弃旧来源归属。
- 船体结构事务须 NotifyStructureChanged；舱室格子、支撑和正式对接登记由 StructureRevision 失效。Ships/Flights/Devices 使用 SpaceStateCollection 权威增删版本与值类型遍历，身份在加入前确定；GetShip/GetFlight/GetDevice 复用 ID 索引，恢复/卸载释放索引。舱气库存包装复用，换 Gas 状态或材料目录时重绑。
- 冷地表查询复用冻结Profile及最新存档差量；TrySampleEnvironmentValue读真实环境层，windStrength读该地块天气Profile基线；TryGetWaterCurrent按正式河流/海流规则返回采样，不加载场景或借玩家当前天气。
- `dotnet run --project Tools/Tests/ShipMass/ShipMass.Tests.csproj -c Release` 验证载荷缓存失效、真实状态及建筑回收封包；SpaceCruise另覆盖极大星区编号的局部精度、空域、目录身份、跨界星体接触和对接，ShipContacts覆盖跨界船/物接触与远区排除，以及原有惯性、质量与快照。专项耗时/分配不代表 Unity 场景帧率，试玩中只静态修改，不操作 Play Mode。
