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
| Universe/SpaceCatalog、UniverseSimulation、SpaceSurfaceQuery | 星系、双精度运动、冷地表查询 |
| Ships/、Ships/Domain/ | 船结构、气密、运动、碰撞、乘员、导航与对接 |
| Ships/ShipCruiseGuidance、ShipLandingGuidance、ShipAssemblyCache | 惯性推进/巡航、手动降落范围/实际姿态、对接整体缓存 |

- 生命周期：`Core/Lifecycle/GameManager.Space.cs`、`World/Dimension/DimensionManager.cs`；面板：`Presentation/UI/{ShipPanelSession,SpaceLandingPanelSession}.cs`。
- PlanetData 是太空目录与 `World/Map/Data/PlanetData.cs` 的 partial；场景/Prefab 在 `Assets/3_Scenes/SpaceScene.unity`、`Assets/2_Prefabs/World/Space/`。
- 内容：`GameConfig/Space/solar-system.json`、`Resources/Config/Space/space-gameplay.json`、Items/shells/space_ships.json 和 space_devices.json；固定船型在 `GameConfig/Structures/fixed-structures.json`，由 ShipFixedStructureBuilder 复用 AddPiece/CommitPiece/RollbackPiece。
- 功能规划来源：`开发文档文件夹/01_世界生成与环境/太空场景开发待办.md`，未完成项与运行系统分开。

## 时钟、坐标与接触

`世界进入/恢复 → SpaceSession 固定时钟 → 记录接触姿态 → 船体运动 → 船间/乘员/散落物接触 → 地表提交 → 场景投影`

- 固定步等待 IsGameplayReady，CaptureShipContactPoses/StepShipContacts/CommitSurfaceContacts 控制顺序；Kinematic Collider 是表现，平动/旋转反作用由权威扫掠解算。地表触地与船间接触比较同一步 Fraction，先撞船后重积剩余轨迹；太空飞船及碰撞后的轨迹重积共用 StepFreeMotion，不做星体表面扫掠。
- 宇宙位置/速度用 double 米，ViewOrigin 只影响显示；地表坐标/镜像经 WorldLocalPresentation，不交太空坐标给 Chunk 或 NormalizePosition。LateUpdate 先按玩家位置更新原点再统一投影。
- 星体按配置的半长轴、偏心率、近日点方向和恒星周期做 Kepler 椭圆公转，默认天文时钟加速720倍；父星先更新，正常固定步接触复用前后位置，自转读权威时钟。会话载入由 SpaceCatalog.RebindUniverse 应用当前目录，保留身份、种子、初始相位与时钟，不迁移船体坐标。飞船仍无星体引力并自由航行。
- Landed/Floating 不积动力、不做船间动态碰撞，机器/气密/自由乘员继续；起飞先同步真实载荷。地表 Lifting 只推进高度/垂直速度/燃料，物理坐标静态、Sprite 偏移显示；下降继续原运动碰撞。
- 视图卸载或玩家回地表不冻结无人船/残骸，暂停与离线不补算。FindPiece 权威索引随装载/放置/回滚/分裂维护，结构改变才重绑视图。

## 船体、导航与乘员

- 四邻接地板定义船体，普通接触/补地板/光纤不形成对接。独立船通过墙类接口和双方控制台确认形成整体；断开清双方确认，按剩余连接重算成员，保留身份/库存/分离速度。
- ShipAssemblyCache 复用拓扑，质量/运动每步更新；模拟与即时查询使用独立缓存。机器/库存按船本地格子作用域运行，分裂后满足控制台/电力/燃料/引擎条件即可驾驶。
- 对接整体只保留最新导航任务，手动接管/到点结束；断电、停机或缺燃料保留任务与平移惯性，恢复动力后续航，不能无燃料自动制动。导航保留玩家选择的真实目标，可到星体画面上方或穿过星体，不自动改为外侧停靠或触发降落。真实剩余燃料限制速度变化，光纤查询检查实际连接。航空燃料为罐内近2:1氢氧气相用途，按真实 mol 原子扣量，不另造燃料物质。
- 手动 WASD 在已有速度上累加推力，松键继续滑行，反向输入渐进制动；转船头不抹掉已有侧向速度。太空操纵检查点火与真实动力，起飞进入太空保留点火开关；无输入不耗推进燃料，碰撞造成的超速也不能被无动力限速静默清除。
- 巡航/下降平移由 ShipThrustAllocation 按所需速度变化选择朝向：向左推用右侧向外喷嘴，四向/斜向及导航制动共用规则。成员船角度与安装 QuarterTurns 决定实际推力方向；同向引擎按推力容量平均供油比例，缺某侧引擎/燃料只失去该方向，不借其它喷嘴。Q/E 选相对整体重心力矩同号的喷嘴，保留直接角速度；无偏心推力则不能转向。
- 引擎本地 +Y 为气相燃料输入、-Y 为喷口，朝向随安装旋转；每台只从本船正式气相供给路径取料，端口/阀门/选择器及逐段共享吞吐限制供给，不从未接管的罐或对接船跨网扣量。GM测试船模板保留舱门到控制台前两格中央通道，设备与供电改位同步更新。
- 驾驶 Q/E 直接控制整船角速度，`CruiseTurnDegreesPerSecond` 默认 45°/秒；松键停转、反向立即换向，同向多控制台不叠加超速、反向抵消。导航共用转速上限，转向仍需有效供电、可用引擎和真实燃料，不按力矩与转动惯量累加按键角加速度。
- 非驾驶失重玩家用 WASD 按屏幕方向累加推力；`Mod_Mover.SmoothSpaceVelocity` 松键保持当前速度，没有摩擦减速，反向输入逐渐制动。FreeWalkVelocity 只记录主动移动分量，其差量合入 FreeMotion 总速；离船继承船上所在点速度（含旋转）与步行速度，12米每秒上限只管主动分量，不消除继承/碰撞速度。太空禁跑，WASD 不消费推进气体；人工重力直接步行，驾驶按船轴向，支撑毁坏释放乘员。
- 太空玩家本体与镜头固定朝上，`Mod_Mover.SetSpaceUpright` 清零本体/刚体转角和角速度，同时保留 Mod_TurnBack 的鼠标左右翻面和手持物瞄准。驾驶只跟整船中心，Q/E 仍转飞船；CameraFollowRotation 关闭跟随旋转时恢复绝对零角。
- 地面支撑恢复直接移动；有供电人工重力的舱内使用地球重力，落地仍有效且不叠当地重力，外部用当地规则。船内查询只读支撑/供电/舱气，密闭舱不请求地表环境。

## 环境与迁移

- 闭合墙边界及完整内部地板决定密闭；停供保留舱气，正常舱门不泄压，破口随外界，修复不返旧气。密闭舱维持正常舱温，补舱只恢复温度，仍需真实供气。
- 墙体压力耐受比较船外气压；真空占位温度不作为气体传热伤害，密闭舱内设备读舱温，有气外界仍按材料耐温结算。压力沿持续断裂线损伤，破坏后重取线；触地、船间冲击和燃爆共用局部断裂线及真实能量预算。结构实际掉血由统一事件聚合红字；Cabin FluidInventory 最小空间使用 MinimumCabinGasSpaceLiters。
- 生物与结构环境损伤各有职责，星体可登陆性由材料耐受表达。冲击/燃爆复用唯一伤害入口，真实燃料库存和压力预算只释放/消费一次。
- 损伤造成的四邻接分裂生成成块 ShipWreck：一个无原设备模块的 Item 记录组成与库存，纯 ShipState 保留漂移/接触几何，停机器与生命支持；攻击按整块耐久回收原物品封包。失去地板支撑的部件按邻接合成残骸。太空普通掉落用 SpaceDropView 冷载荷外壳，复用正式拾取库存事务，不装配建筑/机器模块。
- TryTeleportPlayerToSpace 接收宇宙坐标/速度，负责脱支撑/驾驶/下落状态及失败恢复。场景迁移限单机，复用 DimensionManager 黑屏/玩家快照；读档以飞行阶段选目标场景，未就绪不下落。
- 太空死亡以实际 SpaceScene 判断来源，回 earth 地表的固定主世界出生点；SpaceSession.Respawn 在完整维度重生事务前解除驾驶/支撑/下降状态，迁移期间用中立乘员与 transitionRunning 防止重建太空归属，成功清记录、失败恢复。SpaceScene 不能补写为主世界出生点。
- 飞船可自由经过星球画面，不自动撞毁或切地表；只有控制台 `land`/降落按钮登记 RequestedLandingBodyId，有效范围含船半径及 max(100米,星体半径/4)。CommitSurfaceContacts 在碰撞后开始下降；起飞目标速度由 TakeoffSpeedMetersPerSecond 配置，完成只看速度与至少12秒的时长，不检查高度或星球半径；正常降落约10秒，每步通过正式引擎供电/管路消耗真实燃料。断电或缺油转真实坠落，可再点火救船；持续受控下降且触地垂直速度≤0.5米/秒才免冲击。起飞完成距星体边缘为船半径加12米、只继承星体平移速度，首次镜头留出星体边缘。独立乘员/散落物保留星体接触规则。
- 船下降自动接管本船控制台并关闭船控窗口，WASD 按船轴平移、Q/E 旋转，镜头跟整船中心并固定朝上；同次下降 Esc 退出不强制重进，触地或重返太空恢复原镜头。下降按键的平移/制动/转向需要实际供电、引擎与燃料，但独立于点火救船的垂直推力；无输入、无动力或无人保留平移惯性。触地用真实船体位置，不能由 UI 选点或随机跳点。独自玩家保留附近选址 UI 与随机落点。
- 下降末步先按触地 Fraction 处理船体、乘员和散落物接触，再 CommitSurfaceContacts 结算落地，使用最终速度与落点中心决定陆海；可重新点火救船，海上 Floating 禁驾。冷地表读取目标冻结 Profile+最新差量，不加载地表 Scene 代替查询。

## 表现与交互

- 地表升高共用 ShipLandingGuidance.SurfaceVisualRise，不限制最高显示距离；船体、玩家、散落物与驾驶镜头保持一致。迁移前捕获船体外观，ShipTakeoffLoadingPresentation 在维度加载页按非缩放时间持续滚动起飞气流，场景准备完成/失败后随加载页释放。
- 星球/星空仅在实际 SpaceScene 内生成；正式入口等待 SpaceMgr.LoadPresentation 分帧准备星体，玩家镜头创建后等待 PreparePresentationForCamera 预建星空网格并完成一帧，再结束过渡。地面不绘制宇宙背景，权威会话及无人船仍继续运行。
- 太空驾驶 `DrivingCameraMaxSize=120000`，滚轮乘/除 `DrivingCameraZoomMultiplier=1.25`，退出恢复原尺寸。宇宙坐标 HUD 读双精度位置；SpaceStarfield 用固定420点单网格与 Resources/Space/Starfield Shader 实现三层轻微视差、白/红/蓝色温与独立闪烁，拉远不增加对象或重抽样本。
- PlanetSurfacePresentation 保持每体四顶点与共享 Resources/Space/PlanetSurface 材质，GPU 绘制球面细节、云层、气体条带、大气边缘和太阳日冕；MPB 接收种子/太阳方向，远景按导数淡出，太阳自发光，其余保留 URP 2D 光照。外观参数归各星体 Prefab。
- 船地板/管道/电线使用 vehicle 的 -2/-1/0，设备用 building；材质与 rendererLocalScale 来自定义。船管线读本船正式端口掩码，拓扑变化才刷新，会话结束退订。
- 散落物只有地表升降时叠加贴图高度，太空掉落不继承船体残留地表高度；偏移保留原局部位置，触地、拾取和卸载时恢复。
- 玩家须在实际 Console 的交互半径内开 UI_Ship，离开关闭；自动导航器仅经光纤供能力，线路/地板/墙不作控制台入口。飞船面板会话互斥，CanOpenShipPanel 共用打开/切换/刷新校验。
- 船上设备 UI 只由交互键打开，ShipPieceView 拒绝鼠标点选；Esc 优先取消导航选点，再退出驾驶，右键只使用手持物。
- 船上普通机器按 `MachineDefinition.IsConnectionLine` 排除连接线路，不能只看 `ShipPieceKind`；落点使用真实部件碰撞范围。机器休眠会释放交互对象，实际开始交互时须在本船作用域重新取得当前对象，外观缓存不能作为永久操作入口。
- 舱门只经交互键 TryToggleDoor，鼠标点选拒绝；DoorOpen 持久化，通行/碰撞读 BlocksPassage，开门留 Trigger，通道有人拒绝关门，不改变 SealsAtmosphere。
- 船部件能力 ID 为“船体部件模块”，Prefab Module_ShipPart，脚本 Mod_ShipPart；固定船补给按成员身份及共享罐实际组容量分配，GM 船复用正常起飞条件。
- 引擎喷焰由 ShipEngineExhaustPresenter 读取 SpaceSession.GetEngineOutput；固定步清空出力，起飞/巡航成功消耗真实燃料后才发布，不按移动或 Ignited 猜测。局部粒子随安装朝向、机身与升空偏移，按 vehicle -3 排在船地板/管线后面，停推残留继续同步并自然消退，禁用/卸载清理；出力不进入存档。

## 载荷读取与离线检查

- 自由乘员/散落物先筛空间、高度和完整扫掠距离，再解码计重；船外接半径每固定步预算，近处接触复用整体/部件容器且每次刷新真实运动量，会话结束释放引用。散落载荷先筛距离，ShipGeometry.HasFloor 直接检查旋转矩形，无格子/迭代器分配。性能看 Fixed.FreePassengers/LooseItems/Mass，离线回归不代表场景帧率。
- 方块引擎严格占一格、默认喷口朝本地 -Y；ShipEnginePlacement 共用放置预览/提交/可用引擎收集的船体外缘与喷射净空检查，后续建造也不能堵喷口。气密边界只读 SealsAtmosphere 能力，边缘引擎可替代舱壁；固定船型同步调整引擎、舱壁和同格电线。
- ShipMassReader 按源 JSON 内容或二进制引用复用解码，资源实例/版本变化与会话结束清缓存；库存、流体、堆叠数量仍每步读取真实状态，不缓存整船重量。载荷样本池在分裂移交前丢弃，避免复用已交给新船的对象。
- `dotnet run --project Tools/Tests/ShipMass/ShipMass.Tests.csproj -c Release` 验证载荷缓存失效、真实状态及建筑回收封包；SpaceCruise 离线回归另覆盖惯性推进、乘员继承速度/碰撞冲量/快照恢复、降落范围、星体上方自由飞行/导航、质量、重心、惯量和样本池。专项耗时/分配不代表 Unity 场景帧率，试玩中只静态修改，不操作 Play Mode。
