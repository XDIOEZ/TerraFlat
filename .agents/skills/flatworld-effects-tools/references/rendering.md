# 渲染与光影

## 统一物品渲染入口

- `WorldRenderingManager.Instance` 提供排序、阴影、水态、手动高亮与特效接口，partial 文件按功能拆分并复用原后端；启动由 GameManager 安装，清理只读 ExistingInstance。物品注册、结构刷新、回池/销毁与世界退出统一维护，正常显示不要求每帧重新注册。
- `SetItemShadowEnabled/SetItemInWater` 只改表现请求，结构刷新保留、注销和池复用清空；`SetItemHighlight` 与按 Component 身份合并的交互高亮独立。Owner 同名叠加特效必须全部纳入回收，归池与资源代际切换不能遗留旧请求。
- 回归 `rendering/unified_manager` 与真实 `player_external_effects/combat_effect_prewarm`；组件状态正确不等于 GPU 画面或帧率通过。

## 排序、MPB 与 Shader

- 普通 Item 持有当前 `VisualDefinition`，世界注册/结构刷新/重新显示时以 `visual.sortingLayerName` 和 `sortingOrder` 覆盖类别默认键；一级图层优先、同层比较顺序、同层同序才按 Y 排序。行合批按脚点行与实际一级排序键分组，部件顺序留在组内；地面建筑保留原材质队列，水下使用水面前的环境覆盖。自然资源主体和附属果实共用物品键，草花固定地形层/0 并在定义编译时校验。手持使用手持链，F5 更新定义引用，池复用清空。回归 `rendering/item_sorting_configuration`。
- 陆地复杂嵌套 SortingGroup 的完整建筑保留原生绘制；水下沿用既有行合批，嵌套组内排序尚未做视觉验收。
- 飞船世界由 `WorldPresentationTransformRegistry` 关联显示矩阵与 `WorldPresentationSortingDomain`，域外键取 `projected-world`。只投影显示节点，域内提交 raw 层/order/queue/脚点并压缩为临时 Order；源嵌套组保持原子性，层名保留供光照使用。投影区块地表扩展用原生合并网格参加域排序，普通地表保留 BRG。域 Dispose 必须注销映射、恢复源渲染并释放显示批次。
- 投影区块的深度行在入域时改按实体真实三轴脚点分组，旋转/移动只投影，不按帧重建网格；离域恢复行合批。重组前后保护机器的火焰/发光附属物，不能随旧行销毁。域临时序号改变后在裁剪前统一调用一次 `SortingGroup.UpdateAllSortingGroups()`。
- 纯表现消费者在 URP `beginContextRendering` 读取本帧船姿，域在首相机前补镜像再定序；不调整宿主模拟执行顺序。水面源坐标采样与显示屏幕光照分开，AIECS 水态 MPB 同步显示逆矩阵。规则沿 `rendering/item_sorting_configuration` 与 `space/ship_world`，实际遮挡/旋转/多船性能仍需现场验收。

- 地形 BRG 的队列统一读取 `default-rendering.json` 的 `batchRenderQueueBase/batchProfiles`；贴地设施使用支撑面对应队列，位于墙下，不投接触/太阳阴影、不遮局部光。树与立体自然资源按当前物品键接入原生行网格，果实仅组内偏移；ECS 阴影使用 Shadow。
- 自然资源行合批的排序点由 `NaturalEntitySpatialGeometry.ResolveMovementBlockingSortingAnchor` 取真实阻挡体中心；触发器、禁用/缺失阻挡仍用实体根点，主体与附属果实共用，贴图偏移和受击摇摆不改排序。巨矿底部碰撞偏移必须同步此脚点，回归 `item_runtime/giant_mines`、`ecology/spatial_geometry`。
- 草花 `DepthPresentationOffset` 投影本机镜像，镜像改变同步实例/裁剪；自然物阴影已为表现坐标。BRG 提交以返回成功为准，`IRecoverableChunkViewRenderer` 核验权威格/生态与真实实例；恢复全量读取权威数据，清 Owner 整层并合并 GPU 操作，不依赖可丢失的可见索引。
- 输送带用 FullRect Sprite，格心/尺寸读 rect、pivot、PPU；预览/合批共用 `ConveyorPresentation`。主体/侧轴在 MechanicalShaft、玩家下方，侧铁环只用于直带；视觉端口与供能端口独立。
- `ActorStatusVisualEffectController` 按 Buff 事件维护活跃表现，无激活表现的 NPC 跳过字典遍历；重绑/换父级仍刷新来源。睡眠粒子仅排序/Transform 改变时提交，但逐帧校验池引用。
- `InteractionOutlineCommon` 用屏幕 UV 导数和渲染缩放补偿内侧白边，宽度来自 `InteractionOutlineSettings`；代理保持源尺寸，持续核对 Sprite/排序/裁剪，仅差异写入。局部变换仅创建时归零，重高亮/换材质重交裁剪。`PlayerOcclusionShaderGlobals` 提供本地主角坐标，树冠 `_PlayerOccluder` 在共享壳/回池明确写 1 或 0。
- `ShipPieceView.AirSupply` 按实供功率重染原图左上 `(26,11,12,3)` 灯玻璃：通电保留原色、断电暗灰，故障红只由显式接口触发。`_DeviceIndicatorRect/_DeviceIndicatorTint` 共用光照/回退 Pass，Tint Alpha 默认 0，写 MPB 保留其它通道。
- `WeatherMgr` 平滑发布 `_GlobalWindStrength/_GlobalWindDirection`（XY 吹去方向）；Sprite/BRG/行网格共用 `VegetationSway.hlsl`。树 `_GrassFollowWind=1`：≤0.5 原左右摆、0.5～0.6 过渡、≥0.6 用 65% 顺风倾斜+35% 回摆，不越上风侧。同树以树根相位、行网格以单株 `input.origin`，不可用整行/各顶点相位；最大位移仍为 `摆幅×3×(1+abs(次级摆动))`。Sprite 按 Pivot/PPU、Tilemap 按格锚点弯曲，各 Pass 一致；风向单独变化也刷新，退出清零。

## 阴影与遮光

- `ActorShadowManager` 按完整注册/注销与结构事件维护，旧 Item 椭圆用 `ContactShadowBatchRenderer`；根植植物从 Tree/Plant 或 IPlantableCrop 接入。`Shadow.prefab` 是 ShadowCaster2D，与可见脚底阴影独立；实际 SpaceScene 内两种阴影均隐藏，人工重力不恢复，返回地表复用绑定。
- 接触/太阳共用 `IVisualGroundOffset`、晨昏透明度、`_WorldSunShadowColor/_WorldSunShadowBlur`，关闭太阳不关接触。脚点/宽度来自 `visual.shadows.contactWidth/footLocalPosition/footOverlap`；`ShadowFootprintResolver` 限于可见底边，AIECS 用待机帧固定 VisibleRect，不随攻击/跑步帧伸缩。
- 默认脚点向主体内重叠高度的25%、最多0.25格，覆盖小物品导入轮廓的透明边距；显式 `footOverlap=0` 保留。`rendering/shadow_footprint_matrix` 同时检查普通/缩小物品与阴影贴合。
- 接触阴影仍逐帧采样/裁剪，只与最近 GPU 提交的槽位/数量相同才跳过 SetData；空帧 count=0，可复用旧数据，收集中断/扩容强制上传。太阳代理按源帧/姿态/脚点差异提交，全局参数/裁剪盒仍逐帧更新，回池清缓存；能力查询复用 ItemMods.GetCapabilities，结构变化失效、属性实时读取。
- 轮廓阴影用仿射中心/半轴算世界包围盒，保留翻面/负缩放/旋转/偏心脚点。`SunShadowSampling.hlsl` 共用当前帧 UV、世界 PPU、半埋裁剪；图集外沿透明，CPU 边界覆盖几何；太阳高度先 Clamp01 再幂运算，拒绝非有限投影。
- `WorldShadowProjectionManager.ShadowProxy` 自行从 `SharedSpriteMeshCache` 读取源 Sprite 的柔化倍率，普通 Item 与机械方块不再各自传参；高 PPU 船件、机器共用同一网格外沿和柔化宽度，不修改主体纹理的 Point 过滤。
- `TerrainHeightShadowManager.TryGetShadowMasks` 只读有效表面/八邻格顶部，高投低、同高无边；BRG data0 为左/右/下/上直边，data1 为左下/右下/左上/右上圆角。只在相邻直边未覆盖时采样斜角，纯斜角也提交；Shader 用同宽同衰减的四分之一圆取最大值，不叠黑，层差只判有无。地形/墙脚/地板/平台共用图层和 `GroundElevationShadowSettings`。脏格双缓冲去重，变化裁剪到3×3邻域并订阅八邻区加载/卸载；仅整块重绑全扫，提交中新通知留下一轮，失败保留未完成格。
- 静态墙 `ChunkLightOccluderRenderer` 初绑只合并矩形；`ChunkLightOccluderVisibilityScheduler` 渲染前按 URP 局部光保守球惰性创建/启用，失光禁用、重绑复用，灯/墙/变换未变复用结果。Point 球含 Z，未知类型/适配失败保留全遮挡，恢复当帧补 ShadowCaster2D.Update；候选数与 EnabledOccluderCount 分开。
- 墙共用 selfShadows、体积光用有效 volumetricShadows；发光区域独立 Unlit/Emissive Max 混合保颜色下限。PlacedBuilding 由 Sprite fallback physics shape 投影，手持/召唤器态关闭；内嵌发光用裁剪网格并关 useRendererSilhouette。
- `Light2DSortingLayerUtility`：光源空列表保留 Prefab，建筑空列表接收全部，镜像同步；接收层不表示物理高度/逐灯 Owner 排除。`Mod_LightSource` 仅检测四元数 X/Y 倾斜后做欧拉 0.01° 修正；无自发光且无覆盖层时跳过 Sprite 检查。洞口采光由 DimensionTravelProgressStore.ReadCache/CaveExitAmbientLightCache 按入口/位置/拓扑变化更新，不轮询解析角色 JSON。

## 后处理与远景

- 雾、云和风纹共用 `WeatherScreenRenderingContext` 查询本地玩家及最终相机栈；雾/云读取当前执行世界天气，风纹读取玩家场景天气，各自保留失效清理时机。共享查询不缓存世界或当前相机，效果原有身份记录仍用于重置。
- `WindStreakRendererFeature/WindStreaks` 读取所属本机世界 `WeatherMgr.PresentedWind`，与植被共用平滑风向/强度；`windStreaks` 渲染 JSON 管阈值、速度、尺寸和透明度。只在本地主相机最终堆栈绘制一次，位于大雾后、加载边界云和宇宙远景前，不复制屏幕或建粒子；相位逐帧积分并补最短镜头位移，CPU/Shader 共用128格周期。无风/禁天气/换玩家或世界清理，暂停不推进；规则 `rendering/wind_streaks` 不代表 GPU 画面验收。

- `WorldPostProcessQuality` 从渲染 JSON 建 Volume，随世界生命周期建立/释放自建 Profile/组件；`ScreenPostProcessManager` 优先级 100 合成状态。六档各持 URP 资产、共用 2D Renderer，比例 0.6/0.75/0.9/1/1/1.2；`GraphicsQualityPresets` 只组合表现接口。
- 动态 Volume 不直接启用 URP「剔除未使用后处理变体」；`UberPostShaderVariantStripper` 仅剔除未启用的畸变/色差/颗粒/Bloom 脏镜头，增加效果同步剔除表。`LowHealthRedEdgeRendererFeature` 仅最终相机且强度>0 时 Alpha Blend；普通 Vignette 只会乘法压暗。
- URP 14 的 `IsCameraProjectionMatrixFlipped()` 在 SetupRenderPasses/Pass 等目标就绪阶段调用，AddRenderPasses 仅筛选/传参/入队；自定义 Blitter 由调用方传源 RTHandle 纹理步长，Blit.hlsl 无 `_BlitTexture_TexelSize`。
- `DenseFogRendererFeature` 采样玩家逻辑位置；`DenseFogPresentationTransition` 按真实秒 SmoothStep，首次从 GPU 投影最远角外 0.01 格收拢，改浓度从当前半径续接，起散默认 30 秒；终值 clearRadius/强度，柔边 opaqueRadius-clearRadius，屏外目标仅视口夹取；换玩家/世界/场景、退出/禁天气清理。
- `ZoomCloudRendererFeature/ZoomClouds` 共用 PlanetCloudCommon 与 `Mod_ChunkLoader.TryGetLoadingWindowBounds`：平滑活动窗/表现窗，0.8 秒窗口过渡，折返续接；旧 View 遮全后才回收，无额外圈内收一格。只改缩放投影，窗口取本机镜像，风积分连续、2 秒平滑风速；按本机显示世界的大气选云或真空星空，船内乘员不使用船舱场景的大气。换玩家/世界/场景与禁天气清理，正常太空清云，大雾独立。
- `UniverseOverviewRendererFeature` 在云后红边前，用一张无深度 RT 淡入独立宇宙观察，不强制满屏云；复用 Planet 外观与 PlanetSurface 的 PlanetUnlit Pass，会话提供位置、未进入星区只建目录副本，不迁移角色/场景。`universeView` 默认 60 起/90 全、步进×1.25，地表镜头封顶 60；`entryPlanetRadiusMultiplier=0.3` 为完成时画面高度/星球直径，合法值>0。`SurfaceZoomTransition.ClampInputSize` 在淡入未完成时封顶 opaqueOrthographicSize、丢超额拉远，拉近和零时长放行；退出/换玩家/世界释放。
