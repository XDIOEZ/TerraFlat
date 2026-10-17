---
name: flatworld-effects-tools
description: "FlatWorld 特效/Shader、MPB、世界排序、光影、水体渲染、AIECS 表现及编辑器/调试工具。"
---

# FlatWorld 特效、Shader 与工具

## 入口与所有权

- 资源：`Assets/9_Shaders/{Shader,Material,Volume,Resources}/`；Resources 子树保持逻辑加载路径，第三方资源独立。
- `Assets/5_Scripts/5-3_GamePlay/Presentation/`：`Effects/Management/VisualEffectManager.cs` 管池；`Effects/Runtime/` 为独立 Effect.asmdef；`ActorRenderEffectController/ActorRenderColorEffect/WaterImmersionRenderEffect` 管角色 MPB；`WorldSortingManager/WorldSortingMember` 管排序；`ActorShadowManager/WorldShadowProjectionManager` 管接触/太阳阴影。
- 默认配置：`Assets/Resources/GameConfig/Rendering/default-rendering.json`；Prefab 保存资源/逐物体外观，PlayerPrefs 保存本机偏好。
- 编辑器：`Assets/Editor/FlatWorld/`，内容工坊为 `ContentTools/ContentWorkshop/`。
- 调试：`Assets/5_Scripts/5-3_GamePlay/Development/{Debug,Diagnostics}/`；AIECS 表现：`Entities/AIECS/Presentation/`。
- 程序集单向依赖：Effect 不反向引用 GamePlay 的管理器，生产程序集不引用 FlatWorld.Gameplay.Debug；Debug 使用 GameNetwork 时显式引用 Networking.Core。

## 世界排序与坐标

- WorldSorting JSON 的类别键由 GamePlay 向 AIECS 桥接。动态实体共用 Player 层；地表阴影/印记/水花/预览使用 Shadow，离体世界特效为 world-effect。SortingGroup 只设最外层键，内部保持相对序，组外代理继承外层有效键。
- BRG 无 SpriteRenderer 排序层，Default 材质队列决定与地形关系：Ground/Water 2988/2989，Blocking 2992，草/花 2993，地形最高 2994，旧 Item 接触阴影 2995；ECS 接触阴影/太阳投影为 Shadow。贴地设施为 ground-building 与 Ground-Facility-Lit（2991），位于地面/平台之上、墙体之下，且无接触/太阳阴影和局部光遮挡。
- vehicle 与其它实体共用 Player，可独立配置 Order；类别判定中 Carrier 先于 Building。树资源主体以 TreeSortingVisual 的原生 SortingGroup 与角色混排，果实只在组内偏移，太阳投影仍为共享 BRG。
- 草/花由 Owner 的 DepthPresentationOffset 投影到本机镜像，镜像变化同步实例与裁剪，增量输入为规范坐标；自然物阴影已是表现坐标。Shader 顶点位移同时扩展 CPU 包围盒。
- 草花提交以 BRG 返回成功为准；窗口检查通过 `IRecoverableChunkViewRenderer` 核对权威格/生态与真实图层实例。恢复从权威数据全量重建，不能依赖可丢失的可见索引；清理整个 Owner 图层并合并 GPU 删除/提交。
- `SharedSpriteMeshCache.IsSessionEnding` 标记渲染会话退出；此时停止区块提交与分帧表现推进，不再恢复 BRG Owner。活跃期登记/恢复失败仍保留真实异常，不能用退出分支吞掉正常运行错误。
- 输送带 UV 使用 FullRect Sprite，尺寸/格心来自 rect/pivot/PPU，预览与合批共用 ConveyorPresentation；主体/侧轴在 MechanicalShaft 层、玩家下方，侧铁环仅直带显示，视觉端口与权威供能端口独立。

## MPB、光照与后处理

- HLSL 参数和局部变量避开 `point/line/triangle` 等保留关键字；命名冲突可导致语法错误和后续标识符未声明的连带报错，共享代码需统一改名。
- 角色 Shader 参数经统一 MPB 控制器合成；静态效果 RequiresContinuousRendering=false 并按变化 MarkRenderStateDirty。换图、重新绑定与启停仍更新；动态外部 Renderer 创建后单独注册，销毁前注销。
- 舱内供气器由 `ShipPieceView.AirSupply` 读取实际实供功率，仅重染原图左上 `(26,11,12,3)` 的灯玻璃：通电保留绿蓝原色，断电为暗灰，红色由显式故障表现接口预留，未定义自动触发。`Sprite-Lit-Master` 的 `_DeviceIndicatorRect/_DeviceIndicatorTint` 共用于光照与回退 Pass，Tint Alpha 默认零，写 MPB 前保留其它通道。
- 代理同步源贴图、分离 Alpha、UV 边界、裁剪与 MPB；Universal2D/NormalsRendering/回退 Pass 共用坐标和阈值。URP 2D Lit Shader 实际采样 Shape Light，标签本身不提供光照。SpriteRenderer 与 MeshRenderer 分节点，共同根负责生命周期。
- 动态世界图标复用 RuntimeItemDefinition.Material/外壳回退。全局 Shader 参数置于全局声明，与材质 Properties 分离，避免材质默认值遮蔽。
- InteractionOutlineCommon 以屏幕 UV 导数和渲染缩放补偿画内侧白边，宽度来自 InteractionOutlineSettings；代理保持源尺寸。PlayerOcclusionShaderGlobals 提供本地主角坐标，逐 Renderer `_PlayerOccluder` 控制树冠穿透窗；共享壳/回池明确写入 1 或 0。
- WeatherMgr 平滑发布 `_GlobalWindStrength/_GlobalWindDirection`（XY 吹去方向）；VegetationSway.hlsl 的 ResolveVegetationSwayOffset 由 Sprite/BRG/区块行网格共用。树材质 `_GrassFollowWind=1`：≤0.5 保留原左右摆，0.5～0.6 连续过渡，≥0.6 以 65% 顺风倾斜叠加 35% 树身左右回摆，不越回上风侧。同树按实际树根相位一起摇，行网格传单株 input.origin，不能用整行原点或各顶点相位代替。最大位移仍不超过 `摆幅×3×(1+abs(次级摆动))`，复用原 CPU 外扩。独立 Sprite 按根部 Pivot/PPU、Tilemap 按格锚点弯曲，FLATWORLD_VEGETATION_SWAY 控制接入，所有活跃 Pass 位移一致；风向单独改变也必须刷新，退出清零。
- LightningEffectController 消费权威雷击落点；Resources/Weather/Shaders/LightningBolt 程序化绘制分叉、白芯蓝紫外光、落点电弧并配合屏幕短闪。有限池共享 owned Mesh/Material，各事件用 MPB，落点每帧投影环绕镜像；换世界/禁天气立即释放，表现不扣血。
- WorldPostProcessQuality 从渲染 JSON 建世界 Volume，绑定 GameManager 进出世界事件并释放自建 Profile/组件；画质只改泛光采样。ScreenPostProcessManager 在优先级 100 合成状态，质量接口声明最低档位；可选质量档均持有效 URP 资产。
- 打包时 `UberPostShaderVariantStripper` 仅剔除当前未启用的畸变、色差、胶片颗粒和 Bloom 脏镜头组合；新增这些效果时先更新剔除表，再检查构建中的变体数量和实际画面。动态创建 Volume，不能直接启用 URP 的「剔除未使用后处理变体」。
- 低血量强度交给 LowHealthRedEdgeRendererFeature，在最终相机 resolveFinalTarget 且强度>0 时 Alpha Blend 全屏红边；普通 Vignette 为乘法压暗。
- 地表云层由 ZoomCloudRendererFeature 与 Resources/Shaders/ZoomClouds 合成，复用 PlanetCloudCommon.hlsl 的噪声；通过 Mod_ChunkLoader.TryGetLoadingWindowBounds 只读正式方形活动窗和表现预加载窗。活动窗内清晰，额外表现圈柔和过渡，外边完全不透明；无额外表现圈时内收一格柔边。范围随玩家加载设置及跨区块提交变化，缩放只改屏幕投影；窗口整体按本地镜像投影，不能逐角归一化。风只积分连续云纹漂移，driftTransitionSeconds=2 平滑风速；玩家/场景变化重建，正常太空、禁天气维度和退出清云。天气大雾仍由 DenseFog 独立负责。
- `UniverseOverviewRendererFeature` 在云层后、红边前合成独立宇宙观察；`SurfaceZoomTransition` 只平滑宇宙画面直接淡入淡出，不强制满屏云。`universeView` 默认 60 起、90 完全、每步 ×1.25，地表真实镜头封顶 60。`entryPlanetRadiusMultiplier=0.3` 表示过渡完成时画面高度为星球直径的 30%，先看局部，再继续拉远看整球与宇宙；合法倍数只要求大于零。星体复用 Planet 外观和 PlanetSurface 的 PlanetUnlit Pass，位置来自会话，未进入的配置星区只建目录副本；一张无深度 RT 供整体淡入，退出/换玩家/换世界释放，不迁移角色或加载太空场景。
- 自定义 Blitter 后处理的像素采样步长由调用方按源 RTHandle 纹理尺寸显式传入；URP 14 的 `Blit.hlsl` 不声明 `_BlitTexture_TexelSize`。

## 阴影与遮光

- ActorShadowManager 按完整 RuntimeItemRegistered/Unregistered 与结构事件维护注册，旧 Item 椭圆由 ContactShadowBatchRenderer 合批；根植植物通过 Tree/Plant 或 IPlantableCrop 接入，尺寸来自根 Collider 与可见 Sprite。`Shadow.prefab` 是 ShadowCaster2D，与可见脚底阴影独立。
- 玩家位于实际 SpaceScene 时，接触阴影和太阳投影都跳过，舱内人工重力不恢复阴影；保留注册绑定，返回地表继续按原光照规则显示。
- 太阳投影独立持有注册与 SunShadowCaster 覆盖；接触/太阳共用 IVisualGroundOffset 地面折算、光照/晨昏透明度和 `_WorldSunShadowColor`，柔化共用 `_WorldSunShadowBlur`，关闭太阳投影保留接触阴影通道。
- 世界物品 `visual.shadows.contactWidth/footLocalPosition/footOverlap` 持有底座/落地点；ShadowFootprintResolver 限定可见底边内缩，AIECS 取待机帧固定 VisibleRect。落点不随攻击/奔跑帧伸缩。
- SunShadowSampling.hlsl 由普通 Sprite/AIECS/资源 BRG 共用；采样使用当前帧 UV 边界和世界 PPU，图集外沿透明，半埋裁剪进入采样。透明外沿几何与 CPU 包围盒一致；太阳高度先 Clamp01 再幂运算，投影范围有限且排除 NaN/Infinity。
- 地表高度分层由 Chunk-BRG-Contact-Lit 共享公式处理：分层→接触阴影→Light2D，同层无边，低侧暗边优先；ElevationStrength=0 只旁路高度效果。GroundElevationShadowSettings 更新批次材质，不重建实例。
- Blocking 静态墙共用 ChunkLightOccluderRenderer/selfShadows；局部体积光同时使用有效 volumetricShadows。selfShadows 会影响墙区重叠 Lit Sprite，自发光区域由独立 Unlit/Emissive 的 Max 混合保颜色下限。
- 静态墙阴影由 `ChunkLightOccluderVisibilityScheduler` 在渲染前按 URP 已登记局部投影光筛选，灯/墙/变换不变时复用结果；候选矩形与 `EnabledOccluderCount` 分开。Point 使用含 Z 的保守球相交，非 Point 或适配失败保留全部，恢复当帧补 `ShadowCaster2D.Update`。
- 洞口环境采光由 `DimensionTravelProgressStore.ReadCache` 和 `CaveExitAmbientLightCache` 维护；入口/锚点变化才更新出口集合，玩家位置/拓扑变化才重算距离，不定时解析角色 JSON。
- 动态 PlacedBuilding 的 Sprite 节点持 ShadowCaster2D，投影路径来自 Sprite fallback physics shape；手持/召唤器态关闭。内嵌发光避让使用裁剪投影并关闭 useRendererSilhouette，使自阴影也采用裁剪网格。
- Light2DSortingLayerUtility 适配 URP 14 接收层：光源空列表保留 Prefab，建筑空列表接收全部；镜像光同步集合。接收层过滤不表达物理高度或逐灯 Owner 排除。

## 液体与地表表现

- Ground/Liquid 独立合批，水面主路径为 `ChunkGroundMeshRenderer/Chunk-Mesh-Water-Lit`，BRG 包装共享同一效果；液体外观来自 LiquidDefinition.WorldWater，岸线来自 LiquidDepth>0，水深为四角连续插值。液深边界变化更新八方向共享边/角；水深 UV 使用世界坐标和水层原点/纹理边框，独立于合批后的局部 POSITION。
- WaterSurfaceCommon.hlsl 共用深度/岸线/月光，包装 UsePass Shader 暴露同名新增材质属性；月光在 Light2D 后合成，月亮出现与月相亮度为独立参数。
- 写实水色连续、风格化十档，两种路径 Alpha 都随连续深度 0.60→0.80。ChunkEnvironmentRenderer 默认排除有液格，抽干恢复地表覆盖；岩浆用 LavaSurfaceCommon 与 LavaMode，独立 LavaPatternTex 承载形态。
- `WaterSurfaceFlow.hlsl` 统一 Mesh/BRG 的完整水面输入：FlowX/Y 为河流/实验液流四角实际速度，天然海洋为风向；仅 kind=2 使用海面规则，kind=3 顺流。双相平流移植自 unity-voronoi-water（MIT-0，许可在 StreamingAssets/ThirdPartyNotices），用世界坐标与二维噪声交接，保留流速大小。
- 原风格化/写实模式的河流/实验液流（kind=1/3）走 `RiverSurface.hlsl` 独立纹理法线与稀疏泡沫分支；Mochie FlowUV 配合 Scrawk 共享格角局部方向混合，禁止恢复噪声中值闭环描边。采样用未量化世界坐标的显式梯度，避免格边误选 Mipmap；泡沫先按源纹理阈值、再混合透明度。
- `RiverPattern_RGBA` 为线性、Repeat、Mipmap、不压缩的数据图（RG斜率/B碎光/A泡沫），由 `Tools/Rendering/generate_river_surface_pattern.py` 生成；来源和许可见 `RiverSurface-LICENSE.txt`，`_RiverFoamCoverage` 只调河流泡沫。
- `_FlowSpeed` 只控制水流平移倍率，`_WaveSpeed/_WaveDirection` 控制独立波浪传播（方向为零跟随水流/风向），`_FlowCycleSeconds` 控制交接周期；湖泊无单向漂移但保留细波/反光。潮汐由 `_GlobalGameDay/_TideCyclesPerDay` 独立往返，海洋风浪仍读取 `_OceanWaveTime/_OceanWaveFactors`；三种海洋风格均禁用噪声等值线细节和网状焦散，写实海面不做噪声坐标扭曲。
- `WaterMotionData.ocean` 只标记 kind=2；风格化海洋走 `OceanSurfaceStylized.hlsl`，与河流共用 RiverPattern 短亮纹、十档水色及格角局部采样，海风/潮汐/白沫规则独立。`SampleDirectionalWaterCorner` 接收已经乘过倍率的速度和独立泡沫覆盖率，河流包装保持原参数，禁止海洋二次乘流速倍率。
- WaterVisualSettings 切换风格化、写实、Unity 风格水三种材质，保留水深 MPB/地形实例；第三种使用 `FLATWORLD_WATER_UNITY_STYLIZED/UnityStylizedWaterSurface.hlsl`，河流与海洋均走四向法线、碎光、指数深浅色与泡沫。`ChunkView.unityStylizedWaterMaterial` 直接引用材质；线性 Repeat/Mipmap 数据贴图由 `Tools/Rendering/generate_unity_stylized_water_patterns.py` 生成，改编代码 BSD-3-Clause 许可见 ThirdPartyNotices/UnityStylizedWater-LICENSE.txt。
- 水体三种本地关键字使用 `multi_compile_local_fragment` 保留运行时切换变体；不能依赖源 Tilemap 材质为 Shader.Find 创建的 Mesh/BRG 材质保留变体。
- ActorWaterCommon.hlsl 共用于 Sprite-Lit-Master/AIECS；旋转附属物共享角色世界水线坐标。视觉读取 Mod_TileEffectReceiver 的有效淹没，独立权威 LiquidDepth 不被漂浮改写；边缘邻格容错仅用于交互，液体接触独立于 Ground，液体身份变化才 Exit/Enter。
- 季节雪为 `_SnowCoverage` MPB 通道；SnowFootprintTrail 的历史按自身寿命消退，动态创建时解析专用资源。ChunkSnowCoverRenderer/ChunkSupportSurfaceRenderer 只读对应层并释放自身表现；支撑面 Data0 RGBA 编码四向外露边，相邻边为零。
- 掉落沉没先浸没再远离，SubmergedProgress 仅缩表现矩阵，质量/体积/原 Scale 保留；WaterElapsed 延续进度，结束销毁由权威层提交，离水/拾取/回池恢复姿态。

## 特效池与反馈

- 池取出重置 Transform/Animator/颜色/寿命，回收清理订阅。源同帧销毁时一次性粒子先脱离源；需在角色 OnDisable 回收的特效使用独立根，Owner 与父级分离。睡眠可见实例随休眠释放/恢复，状态请求仍由角色持有。
- ParticleSystem 显式持有 URP 材质，Velocity 三轴 MinMax 模式一致；EmitParams.rotation 为度数，2D 方向使用 SignedAngle(up,direction) 的反号。CombustionVisualEffect 的 BindWorldPresentation 供无 SpriteRenderer 的行网格设施复用粒子模板与显式排序；模板/尺寸只在配置变化时应用，熄灭清空粒子。
- ProjectileFlightTrailPresenter 只读投射物飞行/Sprite。DamageTextEffect 持有样式/显示倍率；CombatFeedbackEvents 的逻辑未命中快照由 CombatMissFeedbackPresenter 消费，独立于伤害结算。
- 罐内连续液面与罐口重力条带为表现；液流在 Mod_WaterVessel 成功移除后触发，Graphic 不参与数量结算且位于内腔 Mask 外。
- 载具尾波消费实际水面位移并逐发射点确认水面，限额世界空间粒子随主体 Sorting Layer、Order+1 排序；停用/回池/换世界清理。
- ShipEngineExhaustPresenter 消费船引擎实际耗油后的出力，复用 Resources/ShipEngineExhaust 材质；材质 .meta 的 GUID 必须为 32 位十六进制，导入后确认 Resources 可加载。局部粒子按 vehicle -3 排在船地板/管线后面，停推残留继续同步；单引擎最多 48 粒子，停推 StopEmitting、禁用 StopEmittingAndClear。
- `SpaceStarfield` 固定420个白/暖白/红/蓝星点，用 Resources/Space/Starfield Shader 做三层轻微视差、环绕和独立闪烁；CPU 只传双精度镜头位移，单 Mesh 九宫格补边，像素大小固定，分辨率变化才重建几何，退出释放 Mesh/Material 并恢复相机背景。

## AIECS 与批量表现

- AiecsWorldRenderer 只读模拟提交后的 Display，按已排序连续精灵批次进入原生 Universal2D/NormalsRendering，主体/阴影分别统计。AIECSRuntime Layer 默认在 SceneView 隐藏，GameView 正常绘制。
- AiecsShadowRenderer 每批≤4096、16 位索引、24 字节顶点，复用可见表/Display 水态；AiecsSunShadowRenderer 消费全局太阳与当前图集。相机缺失/空帧/关闭/退出时隐藏或回收批次。
- 动画按实际 Actor Manifest/覆盖控制器与真实三角形/UV 导出，保留尺寸/Pivot/PPU，非循环保留终点；重导为显式菜单并保留 GUID。AiecsLegacySortScope 适用单相机完整 Sprite 场景。
- DroppedItemPresentation 按空间行/贴图/层增量合并真实 Sprite，静止不重传，离视野释放；类别为 world-item，共享材质在 `Assets/9_Shaders/Resources/DroppedItems`，水线为规范坐标，镜像由默认零的 `_WaterLineOffset` 补偿。
- BRG OnDisable/域重载执行幂等 Dispose，先释放整个组，再 finally 释放自有缓冲/Mesh/材质，启用按需重建；多子批次失败仍释放组。
- DOTS 自定义实例读取使用 `DOTSInstanceData_Load4`，兼容 GLES 常量缓冲路径；Shader 各 Pass 独立引入自己使用的二维调试类型。

## 编辑器与调试工具

- 自动资源刷新合并外部变化，Play/切换期间保留请求、编辑模式静默后处理；Unity 保存独立于全库刷新。内容工坊只写 JSON 差异。
- 动态 SceneManager.CreateScene 不触发 sceneLoaded；Hierarchy 工具结合卸载/hierarchyChanged 定向解析保存记录，GlobalObjectId 仅解析已加载所属场景。
- GameLogManager 业务日志接口为 `[WORK]`；定时刷盘线程池最多一个请求，共享日志流锁且无 Unity API，暂停/退出/错误/显式 Flush 同步落盘。
- F3 EnvironmentInfoDisplay 读取 WorldModel 权威格/EnvironmentLayerIds、FarmlandSystem 水肥和 TemperatureMgr 环境温度；隐藏时停止采样，投影前拒绝非有限/视口外指针，无效时清悬停且不绘制 IMGUI。IMGUI 按实际 CalcHeight 整页滚动。F4 为 GM，F6 为晴天。
- GMReflectionConsole 是动态调试 UI，公共控件等 GameRes.StartupResourcesReady；GMConsolePreferences 保存开关，五分页为玩家/世界/生成与 AI/调试工具/设备数据。RuntimeDebugOverlay 通过事件打开 GM，日志 Canvas 更高；复制/磁盘保原文，缺字仅在显示时转码。
- GM 偏好恢复只从 ItemMgr 已登记 Player_DIC 读取本地档案玩家并定向找管理员模块；未入世继续低频等待，不扫描全场 MonoBehaviour，也不为探测状态创建单例。
- GMWorldLayerOverlay 的温度/污染互斥共用预算采样纹理，格对齐、未加载透明、关闭停止，既不租区块也不改地形。导航模式为一真实格一 Point texel，读取真实流场与 Job 依赖，借用 Native 表不跨后端发布。
- Render Streaming 使用项目自有设置与 WebApp，普通 Play 自动串流关闭；显式入口统一保存偏好、端口/URL 和自启服务 PID，回收核对当前缓存 webserver.exe 身份，编码按目标协商能力。
