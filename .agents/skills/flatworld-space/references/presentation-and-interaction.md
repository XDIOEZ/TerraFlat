# 表现与交互

## 统一姿态、镜头与缓存

- SpaceSession.Presentation/SpacePresentationPose 共用前后固定步姿态与渲染进度（物理 50Hz），船格原点、乘员本地位置、驾驶中心统一插值，表现刚体关原生二次插值。选格按表现反算，支撑/丢弃/存档读权威；联机按本地收包时间插值，远端乘员在同步事件后投影。传送/归属变更/结束重置历史，跨区/浮动原点保持连续。
- 可见性围绕本地乘员权威位置而非浮动原点；支撑/驾驶按船当前世界投影，不因裁剪解除驾驶或退回旧 FreeMotion。人工重力散货用承载船本帧 DisplayPresentationPoint，不直接投影固定步坐标。
- 太空驾驶 Mod_Cam.PresentationLocked 跟同一插值中心、固定 LateUpdate、无额外 XY 阻尼/前探；地表驾驶沿用偏好，释放恢复原更新方式和最新设置。DrivingCameraMaxSize=120000，滚轮倍率 1.25，退出还原尺寸。
- UIManager 持久 UI 根；星区卸载不毁 HUD。单机 PresentationCache 最多留 2 个已装配星区根，禁用移持久根，旧 Scene 卸完才恢复；State/资源代际/EndSession 清理。ViewCache 留 16 船根/2048 部件，跨场景休眠复用，正式落地清本船；联机仍释放。
- 星球/星空只在太空 Scene；星区租约分帧准备，地表初入太空等 PreparePresentationForCamera 预建并完成一帧。SpaceStarfield 以 SectorPosition 差算视差，固定 420 点单网格+Resources/Space/Starfield Shader，拉远不增对象/重抽。
- PlanetSurfacePresentation 每体四顶点、共享 Resources/Space/PlanetSurface 材质，MPB 传种子/太阳方向，GPU 球面/云/气体/日冕，导数淡远细节；太阳自发光，其他 URP 2D 光照，外观在各 Prefab。
- 地表 UniverseOverviewRendererFeature 仅观察；SurfaceZoomTransition 在 60～90 直接淡变，云圈固定玩家世界半径，90 时视高=星球直径30%。未进星区只建表现目录副本，读真实星体时钟/位置及 Prefab，不取权威 GetUniverse、不租 Scene/迁移；驾驶覆盖/洞穴禁用。

## 起降与船件渲染

- SurfaceVisualRise 共用 ShipLandingGuidance：点火提排序，升高≥1 单位为空中；船体 Collider、玩家根/刚体、散物及镜头仅叠一次同高偏移。原实体阻挡离地改 Trigger，落地/重生/休眠/卸载恢复；原 Trigger/独立子刚体保持。存档/联机坐标去高度，TryGetSupport 表现查询扣高度，权威/控制台距离不扣。
- ShipCombatBridge 按攻击实际世界和升高后形状查询，空中需 Projectile|AirborneTargets，不依赖 Host 镜头。ShipSurfaceShadowPresenter 合并去重占地 Mesh、复用 ActorBlobShadow/ground-shadow，地面投影不叠高度，随真高度缩淡，结构/柔化变才重建，离地表/结束释放。
- 起飞迁移前捕获外观，ShipTakeoffLoadingPresentation 随原加载页以非缩放秒滚动气流，成功/失败随页释放。FlightHud.TryGetShipFlightHudSnapshot 只读真实高度；普通建筑移交成功才写/复制 LastShipLanding，不把退驾驶/删船当落地。主动 RequestConsoleExit 通知 HUD，手动 EnterConsole 解退出锁，自动接管/释放不发这两类通知。
- ShipColliderPresenter 只管仍归会话的船，一个表现刚体+共享 CustomCollider2D；待移交落地船继续阻挡，跨区迁移整根。ShipPieceView 仅贴图/交互，移交成功清理；整船排序/偏移每船一次。
- vehicle 层序：地板 -2、管道 -1、电线 0、光纤 1，机器线路 VisualLayer+1，设备 building。管臂共用 FluidPipeConnectionVisual；pipe0..15/wire0..15 读本船正式拓扑，格/朝向/端口签名变化才刷本格/四邻，结束退订。光纤 ShipOpticalGraph.GetConnectionMask/ShipFiberConnectionVisual 按结构修订，创建/休眠重建。
- ShipPieceView 在创建/休眠恢复/换船缓存 MachineWorld ship 作用域及数字 GUID；静态 Renderer 状态只在变化时写。地面 Mod_Door.UseItemSprite 与船上共用 DoorSlideVisual，休眠/卸载释放门叶。
- 引擎出力每固定步清零，真实消耗燃料后发布 GetEngineOutput，ShipEngineExhaustPresenter 不按移动/Ignited 猜。粒子随安装方向/机身/升高，vehicle -3，残焰自然消退并继续同步，禁用/卸载清理；出力不存档。Sprite 身份变才更新喷口 Bounds/位置，重绑重置缓存。

## 控制台、舱门与散物

- UI_Ship 仅实际 Console 半径内打开、离开关闭；CanOpenShipPanel 共用打开/切换/刷新校验，会话互斥，导航器仅光纤供能力，线路/地板/墙非入口。设备/舱门只经交互键，拒鼠标点选；Esc 先取消导航选点再退出驾驶，右键仅手持物。
- IGridInteractionTarget 按本船逻辑格参与光标上下等分，顺序用 MachineDefinition.VisualLayer，地板/墙/纯能力不占份数。机器休眠释放交互对象，开始交互须从本船域重新获取，不永久持外观缓存。
- TryToggleDoor 保存 DoorOpen，通行/碰撞看 BlocksPassage，开门去该格阻挡但留交互范围，通道有人拒关，不改 SealsAtmosphere。
- 太空丢弃走正式冷载荷，PickupAfterSimulationSeconds 防投掷中拾回，world-item+2 高于船板。人工重力货物存 SupportShipId/船内坐标，计船重并随起落迁移；重力/地板失效继承船点速度转独立物理。散物仅地表升降叠贴图高度，保留局部基准，触地/拾取/卸载恢复。
- SpaceSession.Audio 在建筑还原前按点火/LandingEventId 边沿播 ship.ignition/landing，首次读档仅基线，主客重复不重播。喷焰声音随真实出力，太空音量为地表 1/10；舱门独立 ship.door.open/close，来源/重建在 SoundDesign/Space_SFX/，不改普通木门 Cue。
