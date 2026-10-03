---
name: flatworld-effects-tools
description: "Use when: 定位或修改 FlatWorld 的运行时特效、粒子、伤害文字、水体效果、Shader、视觉管理器、项目编辑器工具、结构编辑器、调试脚本或测试辅助。关键词：VisualEffectManager、SpecialEffects、Shader、Editor、GameDebugManager。"
---

# FlatWorld 特效、Shader 与工具

## 入口

- 项目自有 Shader 资源统一放在 `Assets/9_Shaders/`：Shader 源文件放 `Shader/`，材质放 `Material/`，Volume 配置放 `Volume/`；必须依赖 `Resources.Load` 的 Shader/材质放在 `Assets/9_Shaders/Resources/` 下并保持原逻辑资源路径。不要再创建 `Assets/Shaders`、`Assets/Resources/Shaders` 或其它散落的项目自有 Shader 资源目录；第三方插件资源保持原目录不移动。
- 运行时视觉：`Assets/5_Scripts/5-3_GamePlay/Presentation/Effects/Management/VisualEffectManager.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/Effects/Runtime/`
- 角色渲染：`Assets/5_Scripts/5-3_GamePlay/Presentation/{ActorRenderEffectController,ActorRenderColorEffect,WaterImmersionRenderEffect}.cs`
- 世界排序统一由 `Presentation/{WorldSortingManager,WorldSortingMember}.cs` 和 `Resources/GameConfig/Rendering/default-rendering.json` 的 `sorting` 段管理，管理器挂在 `WorldManager.prefab`。BRG 地形为 `Default`；阴影、印记、水花和预览按各自地表类别使用 `Shadow`；动态实体类别共用 `Player/100`；脱离实体的天气/世界特效使用 `world-effect`。AIECS 批量主体与阴影从 GamePlay 桥接注入相同类别键。带 `SortingGroup` 的主体只设置外层组；内部子表现仍使用组内相对排序，离体代理则读取最外层有效组的键，不能直接继承子 Sprite 的原始 `Default/0`。BRG 材质队列独立保留。
- 贴地建筑使用 `ground-building` 类别（Default/0）与 `Ground-Facility-Lit.mat`（Queue2991），避开玩家深度行网格；必须在地面/平台之上、Blocking 墙体之下，不能仅改成 Shadow 排序层，否则会反盖同格墙体。`PlacementLayer=Ground` 同时排除接触阴影、太阳投影与局部光遮挡。
- 实体脚底阴影：`ActorShadowManager` 只登记 Item 状态，`ContactShadowBatchRenderer` 把旧 Item 的扁椭圆接触阴影汇为一个 BRG 批次；BRG 无 SpriteRenderer Sorting Layer，用 Default/Queue 2995 排在地形 BRG（最高 2994）之后，动态实体仍在更高的 Player 层。ECS 保留独立的 4096 只网格批次，不注册逐实体对象；两种接触阴影与太阳投影共用 `_WorldSunShadowColor`。
- 根植植物复用 `ActorShadowManager` 的接触阴影：不可拾取且未被持有的树按 `Tree/Plant` 标签、作物按 `IPlantableCrop` 模块注册；以根部 Collider 定位，并用可见 Sprite 宽度限制阴影大小，成长时更新尺寸。草饰批次没有 Item，不进入该注册链。
- 源贴图被飞行等表现抬升时，`ActorShadowManager` 与 `WorldShadowProjectionManager` 共用 `IVisualGroundOffset` 将阴影定位折算回地面；提供者按当前实际视觉 Transform 计算世界位移，回池或死亡归零后不能继续用飞行状态值抵扣。
- 太阳长投影由 `Presentation/WorldShadowProjectionManager.cs` 独立持有，偏好由 `SunShadowSettings` 保存；代理监听完整 `RuntimeItemRegistered/Unregistered`，不复用脚底阴影的水体显隐。`SunShadowCaster` 允许 Prefab 覆盖主体、视觉高度与落地点。
- 使用共享外壳的世界物品通过 JSON `visual.shadows.contactWidth` 显式接入场景级椭圆底座阴影，`visual.shadows.footLocalPosition` 作为底座和太阳投影共用的物品根节点局部落地点；脚点由 `ShadowFootprintResolver` 限定在可见底边内缩范围，避免旧配置或透明留白拉出断层。宽矮主体若默认内缩导致长阴影根部与本体断开，可用 `visual.shadows.footOverlap` 单独缩小最小内缩，禁止改全局阴影参数迁就单个物体。尺寸与锚点属于具体物品定义，不得写到共享 Shell 或按物品 ID 硬编码。
- 编辑器工具：`Assets/Editor/FlatWorld/`、`Assets/Editor/FlatWorld/ProjectTools/`；内容工坊入口为菜单 `FlatWorld/内容配置/内容工坊`
- 调试：`Assets/5_Scripts/5-3_GamePlay/Development/Debug/`、`Development/Diagnostics/{GameDebugManager,GameLogManager}.cs`

## 不变量

- 太阳高度 `Sin(progress * PI)` 在非整数次幂前必须 Clamp01，单精度日落端点可能略为负数；`SunShadowParameters.IsValid` 同时约束可见性和全局 Shader 参数。普通物品与机械共用有限投影包围盒检查，拒绝 NaN/Infinity、负范围及排序距离溢出，异常绑定只诊断一次并低频重试，不改写实体或存档坐标。
- BRG 原生资源不能只在 `OnDestroy` 释放；脚本域重载的 `OnDisable` 必须执行幂等 Dispose，而不是仅 Hide。接触阴影与 AIECS 主体先销毁整个 BRG，再在 finally 释放自有缓冲、Mesh 和材质；多批次地形须保证任何子批次失败后仍会执行 group.Dispose。恢复启用时按需重建批次。排查重复退出日志先比对堆栈方法名、当前 DLL 符号及 Editor.log 重载顺序，不能把修复前日志当作新代码复现。
- 先确认触发系统及 Prefab/材质/Shader 的真实引用来源，再改表现。
- 陶罐 UI 的水面摇晃和罐口液流属于表现层：水面使用固定步长的连续网格波面，只读取罐体运动并自行衰减；罐口液流只在 `Mod_WaterVessel` 实际移除液体后触发，并使用独立连续条带网格按重力弹道和流速收细。不得让 Graphic 帧率参与液体数量结算；液流 Graphic 必须位于罐体外 Mask，避免被内腔裁剪。
- 池化特效每次取出时重置 Transform、Animator、颜色和生命周期；回收/禁用时清理订阅与状态。
- 通用投射物飞行拖尾由 `ProjectileFlightTrailPresenter` 挂在 `Mod_Projectile` Prefab 上负责，只读取投射物飞行态和 Sprite 表现；不要把拖尾生命周期写进 `Mod_Bow`、伤害或碰撞结算。
- 源实体会在触发特效的同帧被回收时，一次性粒子根节点必须先脱离源实体并放到同一场景独立播放；否则 `PrepareForDespawn/OnDisable` 会把粒子提前清空。
- 需要在角色 `OnDisable` 中立即回收的池化特效不能挂到该角色层级下，否则归池 `SetParent` 会与父级激活/停用过程冲突；Owner 登记与 Transform 父级分开，睡眠 ZZZ 由单位缩放的独立特效根节点持有并在 `LateUpdate` 跟随。区块休眠不等于退出 AI 睡眠状态，停用时只释放可见实例，重新激活时恢复仍有效的表现请求。
- 粒子 `VelocityModule` 的线性 X/Y/Z 必须使用同一种 `minMaxState`；2D 特效即使 Z 速度恒为零，也应使用与 X/Y 相同的模式并把上下限都设为零，避免 `Particle Velocity curves must all be in the same mode`。
- 运行时动态创建的 `ParticleSystem` 不能依赖 Renderer 默认材质；在 URP 2D 下必须显式绑定可用材质，否则默认/旧管线 Shader 可能直接显示为洋红色。
- `ParticleSystem.EmitParams.rotation` 使用欧拉角度数；2D `Billboard` 粒子按世界移动方向旋转时，屏幕旋转正负方向与 `Vector2.SignedAngle(Vector2.up, direction)` 相反，应使用其反号，否则水平/垂直方向看似正常但 45° 斜向会转成垂直朝向。
- 伤害数字的最终颜色由 `DamageTextEffect` 样式或调用数据覆盖，不能只改 TMP 的 Prefab 字色；数值到显示倍率的映射也由该表现组件负责，战斗结算只传递实际伤害值与样式。
- 逻辑未命中由 `CombatFeedbackEvents` 发布攻击位置快照，世界级 `CombatMissFeedbackPresenter` 消费后用本地化文本和白色覆盖复用伤害文字池；不得重掷命中概率、把 0 伤害/无效结算当成闪避，或在伤害模块中写入具体文案和动画。
- 角色颜色等共享 Shader 参数通过现有 MPB 控制器提交，避免多个组件互相覆盖。
- Unity 2D 使用 URP/Light2D；修改 Shader 前核对材质实际 Shader 与 Pass。
- 正式 BRG 地形占用 `Default/0` 的 Queue 2987~2994，其中 Ground/Water 为 2988/2989、Blocking 为 2992，草为 2993；旧 Item 接触阴影 BRG 使用 Default/2995，太阳投影与 ECS 接触阴影使用更高的 `Shadow/0`。耕地渐显、地格裂纹、脚印、水花也使用 `Shadow`；玩家等动态世界实体由 `WorldSorting` JSON 放在更高的 `Player`，同层按 Y 轴互相遮挡。BRG 无 SpriteRenderer 的 Sorting Layer，必须靠 Default 材质队列安排与地形的关系，不能仅靠旧 Tilemap Order 推断跨系统可见次序。Shader 位移后的 CPU 包围盒必须同步扩大，屏幕外投影源仍可能把阴影投进视口；逐 Renderer MPB 必须显式恢复 `_MainTex` 及 Android 分离 Alpha。
- `WorldSorting` 的 `vehicle` 类别用于木筏及未来交通工具；它与其它动态实体共用 `Player` Sorting Layer，但允许通过 JSON 使用更低的 Order，使低矮载具不会盖住玩家。`ResolveCategory` 必须先判定 `Mod_Carrier` 再判定 `Mod_Building`，否则载具会被错误归入建筑类别。
- 太阳长投影对低枢轴高 Sprite 的落点需收入可见根部，透明底边经长距离投影会被放大成树干与阴影之间的断缝；特殊对象仍用 `SunShadowCaster.FootOffset` 校正。共享投影 Shader 柔化采样时，普通 Sprite 通过 MPB 传贴图 texel 与当前 Sprite UV 边界，AIECS 则由图集材质和逐顶点 UV 边界提供同一契约，避免采到邻近帧；柔化步长必须按世界 PPU 归一，高分辨率 Sprite 不能直接按源纹理 texel 计算，否则同世界尺寸下会显著更锐。
- 自然资源太阳投影走 `Chunk-BRG-Sprite-Lit` 的 `_CHUNK_RESOURCE` 分支；两条 Pass 与普通/AIECS 投影共用 `SunShadowSampling.hlsl` 和 `_WorldSunShadowBlur`。普通 Sprite 与资源 BRG 使用独立缓存的五像素透明外沿四边形；根部保留有符号高度以允许主体重叠，CPU 包围盒须匹配。资源 `Data1` 传图集边界、`FlowY` 传 UV 到半埋裁切平面，裁切进入模糊采样；不改本体 Point 过滤或为树新增逐帧更新。
- `SpriteRenderer` 与 `MeshFilter/MeshRenderer` 不能共存于同一 GameObject；双绘制代理须分节点，并同步相机 Layer、排序与世界包围盒，只启用当前绘制节点。池化、场景迁移和销毁以共同根节点为单位，构造失败也要释放根节点。
- 全局渲染默认值统一从 `Resources/GameConfig/Rendering/default-rendering.json` 读取：阴影透明度、几何、柔化范围与默认值，地面分层阴影，遮挡、草饰外观、世界排序、常态后处理与本机画质/水体默认档位。Prefab 只保留资源引用和逐物体特效外观，玩家选择写入 PlayerPrefs，恢复默认再次取 JSON 默认值。太阳长投影与脚底接触阴影共用 `SunShadowParametersProvider.ResolveSolarOpacity` 计算光照和晨昏透明度，并共用 `_WorldSunShadowColor` 颜色；柔化强度由 `SunShadowSettings` 写入全局 `_WorldSunShadowBlur`，关闭太阳投影不能清掉接触阴影的颜色和柔化。Shader 不得把全局参数声明在 `Properties` 中，否则材质默认值会遮蔽运行时设置。旧 Item BRG 单位方片与 ECS 的 [-1,1] UV 共用解析椭圆 Shader。长投影模糊采样超出当前 Sprite UV 的半像素过滤外沿时返回透明，采样点限制在本帧 texel 中心范围并让边界渐隐，避免图集邻帧渗色。
- `AiecsSunShadowRenderer` 通过 `_WorldSunShadow` 全局契约消费太阳状态，每 4096 只合批并复用当前动画图集，不反向引用 GamePlay。设置关闭/夜晚/相机丢失必须停止投影顶点上传并隐藏旧批次；普通实体管理器关闭时取消注册、清空绑定并停用更新，开启时只补扫 ItemMgr 权威表。
- 地表高度分层在 `Chunk-BRG-Contact-Lit` 的共享 HLSL 中处理，两条 Pass 共用 `UnityPerMaterial` 布局和公式，且先分层着色、再接触阴影、最后进入 Light2D。按世界格坐标与四邻离散层差画边，同层无边、低侧暗边优先、拐角取最大强度；`_ElevationStrength=0` 必须完全旁路高度 Tone/暗边/亮边，不能连带关闭 Contact 或改变水面 Shader。
- 地表高度阴影的本机开关与宽度由 `GroundElevationShadowSettings` 提供。BRG 地面运行时材质先复制源 Tilemap 材质，再覆盖高度阴影偏好；现存批次监听偏好变化只改材质参数，不重建区块实例。高度阴影的 Shader 默认值与 BRG 模板材质要同步。
- 局部 `Light2D` 如果开启 `volumeIntensityEnabled`，同时要开启 `volumetricShadowsEnabled` 并设置有效 `shadowVolumeIntensity`；否则 `ShadowCaster2D` 只会阻挡普通光照，体积光晕仍会穿过石墙、矿洞岩壁等 Blocking Tile，看起来像“光穿墙”。新版区块的静态墙体遮挡统一复用 `ChunkLightOccluderRenderer`，不要再给每块玩家墙单独创建常驻 ShadowCaster。
- `ChunkLightOccluderRenderer` 的 Blocking Tile 阴影体必须开启 `selfShadows`，否则墙体虽然会向背光侧投影，墙面自身仍会被 Point Light 整块照亮；通用世界 `Mod_LightSource` 的 Point Light 使用满强度普通阴影，保证实体墙移除该局部光，同时保留昼夜全局光和墙体朝光侧的窄外沿。
- `selfShadows=true` 的 Blocking Tile 阴影体会通过 URP 2D 阴影模板影响任何与墙体占地区域重叠的 Lit Sprite，而不只影响墙体自身；火把等自发光物体应使用独立 Unlit/Emissive 覆盖层，可按玩法需要只覆盖发光区域或整个源 Sprite。覆盖层使用 Max 混合给发光体提供不被阴影压暗的颜色下限，同时保留原 Lit Sprite 更亮的受光结果；禁止为了让发光体不变黑而关闭墙体 `selfShadows`，否则会重新出现整块墙面被局部光照亮的问题。
- 动态可交互建筑不属于 Tilemap，不能依赖 `ChunkLightOccluderRenderer`；落地 `PlacedBuilding` 应在主体 `SpriteRenderer` 节点启用 `ShadowCaster2D`，旧的碰撞体节点矩形 ShadowCaster 必须关闭。URP 14 的 `useRendererSilhouette` 只负责自阴影模板，投影网格仍来自 `m_ShapePath`，因此完整建筑应把 Sprite 的 fallback physics shape 同步到路径并启用 `selfShadows`；手持/召唤器状态必须关闭。发光建筑自动避让自身光源时，应同时用裁剪路径生成投影并关闭 `useRendererSilhouette`，让自阴影也使用裁剪网格，否则完整 Sprite 仍会把光源盖住；不得通过关闭墙体阴影或整盏灯的阴影来修复。
- `Light2DSortingLayerUtility` 集中适配 URP 14 的接收排序层字段。`Mod_LightSource.TargetSortingLayers` 空列表保留原光源 Prefab 配置，`SetTargetSortingLayers` 支持运行时设置与恢复；`Mod_Building.ShadowTargetSortingLayers` 空列表表示全部接收层。层名称必须校验，不能把未知名称默默转换成 Default；环绕世界的镜像 Light2D 必须同步源光的层集合。这些字段只过滤接收者，不代表物理高度或逐灯的 Owner 排除，不能靠调整 Z、Light Order 或关掉 `selfShadows` 宣称解决内嵌光源的外投影问题。
- 共用海水 `UsePass` 的包装 Shader 必须声明公共 Pass 新增的同名材质属性；月光等夜间自发光倒影应在 `CombinedShapeLightShared` 之后合成，避免全局夜间光照被重复相乘。月亮出现动画读取 `DayTimeSystem` 发布的 `_GlobalMoonAppearance`，尺寸/渐亮与 `_GlobalMoonlightIntensity` 的月相亮度分离，避免新月把月面永久缩小。
- 正式 Ground/Liquid 由 BRG 独立提交，液体外观来自 `LiquidDefinition.WorldWater`，禁止按 GroundTileId 查水面贴图。岸线以 LiquidDepth > 0 判断，深度用每格四角插值；任一边界格液深变化须更新八方向邻区的共享边/角，不能只监听 TerrainCell 改动。旧 Tilemap 的颜色仍只编码岸线，兼容深度纹理与 BRG 共用连续液深语义。
- 水面视觉把双线性采样后的连续水深离散为 `0.1~1.0` 共十档，真实 `LiquidDepth` 与水深纹理仍保持连续；两种正式水面风格的基础深浅色权重按十档等距变化，避免深水段相邻层级难以分辨。
- Tilemap 合批后 `POSITION` 不保证是 Chunk 局部坐标；水深与岸线使用世界坐标，MPB 的 `_LiquidDepthUvScaleOffset` 必须扣除水层原点再加入一格纹理边框。当前世界网格每格为 1 单位且原点对齐整数，不要用 `unity_WorldToObject` 恢复已被合批丢失的局部坐标。
- 水面潮流使用 `DayTimeSystem` 发布的 `_GlobalGameDay` 驱动，并沿材质 `_FlowDirection` 轴按 `_TideCyclesPerDay` 往返；方向性水纹不要改回基于 `_Time` 的持续旋转，否则跳时、读档与游戏时间倍率会和潮汐表现脱节。
- 写实水面的风浪传播与潮流平移必须分开：波相位随游戏时间连续推进，潮汐只平移水面坐标，避免潮流换向时整片海面停住；波面法线与太阳高光共用解析波斜率，缩小时通过屏幕导数衰减细浪并拓宽高光，不量化写实水面的世界坐标。风格化水面按其独立算法保留像素采样与浪纹。
- 水体风格由 `WaterVisualSettings` 保存本地偏好，`ChunkView` 的 Water/CaveWater 均通过 `WaterVisualStyleBinding` 在激活和设置变化时替换共享材质，不重建地形、不改写水深 MPB。风格化材质显式启用 `FLATWORLD_WATER_STYLIZED` 本地关键字，两种正式材质都必须被 Prefab 引用，确保构建保留 Shader 变体；两种算法共用 `WaterSurfaceCommon.hlsl` 的水深、岸线与月光契约。
- 运行时动态创建、用于展示世界物品图标的 `SpriteRenderer` 不得依赖 `AddComponent` 默认材质；应复用 `RuntimeItemDefinition.Material` 的物品材质与外壳回退，确保提示表现和真实物品一致接收 Light2D。
- 草木风摆由 `WeatherMgr` 写入 `_GlobalWindStrength` Shader 全局参数，草 Shader 与支持积雪、水体等通道的 `Sprite-Lit-Master` 共用 `VegetationSway.hlsl` 顶点位移。`Sprite-Lit-Master` 只对启用 `FLATWORLD_VEGETATION_SWAY` 材质关键字的物品执行风摆，避免普通物品承担顶点计算。Tilemap 使用单元锚点弯曲；树等底部 Pivot 的独立 Sprite 使用对象根部弯曲，按图片 PPU/Pivot 标定 `_GrassSpriteHeight`，用 `_GrassBendStart` 固定树干。JSON 世界物品通过 `visual.materialAddress` 引用带 `ItemMaterial` 标签的共享材质；所有 URP 2D 活跃 Pass 必须复用同一顶点位移。
- 屏幕后处理依赖当前 `QualitySettings` 的 `customRenderPipeline`；不能只检查编辑器当前质量档位，所有可选档位都必须引用项目内实际存在的 URP 资源，否则 Scene 视图可能可见而 Game/Android 画面不可见。
- 世界常态后处理由 `WorldManager/Global Volume` 运行时读取统一渲染 JSON 创建 Volume Profile；`WorldManager` 跨场景常驻，因此 `WorldPostProcessQuality` 必须绑定同一 Prefab 内 `GameManager` 的进出世界事件，只在世界内启用 Volume，退出时释放创建的 Profile 和所有 VolumeComponent。调色也取 JSON，画质只调整泛光采样；状态警示继续由优先级 100 的 `ScreenPostProcessManager` 独立合成。
- 屏幕后处理脚本按最低支持质量只实现一个档位标记接口：Low 可在所有档位运行，Medium 需中/高档，High 仅高档；未标记效果保持旧行为。
- 低血量屏幕红边由 `ScreenPostProcessManager` 计算玩法强度，最终交给 `LowHealthRedEdgeRendererFeature` 的 GPU 全屏 Pass；URP 内置 Vignette 使用 `input * color` 的乘法公式，只能把边缘压暗，不能生成鲜明红色警示，因此不要再用它实现低血量红边。GPU Pass 只在强度大于零且相机栈 `resolveFinalTarget` 时入队，使用硬件 Alpha Blend，不复制源颜色、不创建额外 Camera，也不走 CPU UI 网格。
- 轮廓内侧描边覆盖层保留在主体 `SortingGroup` 内，比对应 Sprite 高一层；组外的独立代理须继承最外层组的有效排序键，不能直接继承子 Sprite 的原始层/Order。URP 2D 自定义 Sprite Shader 必须包含 `Core2D.hlsl` 并保留逐渲染器属性；需要随 `Light2D` 明暗变化时还须实际采样 Shape Light（如 `CombinedShapeLightShared`），只有 `Universal2D` 标签不会自动获得光照。
- 描边等代理 `SpriteRenderer` 必须同步源 Renderer 的 MPB 局部裁剪参数；代理写入自有 MPB 时必须同时写回 Sprite 的 `_MainTex`，避免逐渲染器贴图被默认白图替换。`Universal2D`、`NormalsRendering`、`UniversalForward` 等实际参与的 Shader Pass 必须使用同一坐标与阈值，避免代理或回退 Pass 重新显示已剔除像素。
- 交互描边由 `InteractionOutlineSettings` 提供本机开关与屏幕像素宽度，默认值/范围取渲染 JSON 的 `interactionOutline`，控件接入视觉特效页与通用保存/取消流程。普通 Sprite 与建筑/机械行网格共用 `InteractionOutlineCommon.hlsl`，通过屏幕 UV 导数画轮廓内侧白边并补偿 URP 渲染缩放，禁止用 PPU、texel 或放大副本决定宽度。代理保持源尺寸、绘制在主体之上，同步 UV 边界与分离 Alpha。
- 玩家被树冠遮挡时的圆形穿透窗由 `PlayerOcclusionShaderGlobals` 写入本地主角世界坐标，`Sprite-Lit-Master` 只对逐 Renderer `_PlayerOccluder=1` 的对象降低 Alpha；世界树必须带 `Tag.Tree`，`ItemDefinitionRuntime` 在共享外壳/对象池复用时必须显式写入 1 或 0 并保留其它 MPB 参数，禁止给所有 Sprite 开全局遮挡或为每棵树增加逐帧脚本。
- 树木资源 ECS 通过纯视觉 `TreeSortingVisual` 使用原生 `SortingGroup`，树根与角色共用动态排序键，果实偏移只作用于组内；主体不能继续留在 Default BRG。直接复用物品植被材质，MPB 同步贴图、分离 Alpha、裁剪与 `_PlayerOccluder`，太阳投影仍走共享 BRG，不创建逐树阴影脚本。
- 角色水体效果覆盖会旋转的手持物等附属 Sprite 时，水面高度与波浪横轴必须使用角色统一的世界空间坐标；保留本地坐标模式只用于不旋转的旧材质兼容，避免水线随物品旋转成竖线。
- `ActorWaterCommon.hlsl` 是旧 `Sprite-Lit-Master` 与 AIECS Lit 原型共用的角色水下公式；调整染色、透明或水线时保持两个调用方一致。AIECS 原型的顶点水参数只是视觉输入，不能代替 `Mod_TileEffectReceiver` 的真实地形、体力和氧气状态。
- 手持物通过 `RegisterExternalRenderers` 接入角色渲染效果后，运行时再动态创建的子 `Renderer` 不会自动进入该次注册快照；这类临时表现必须在创建后再次注册自身节点，并在销毁前 `UnregisterExternalRenderers`，避免水体浸没、受击染色等 MPB 效果漏掉或控制器残留引用。
- 角色/动物的水中生存由 `Mod_TileEffectReceiver` 读取独立 LiquidDepth；水深不超过 0.3 时不漂浮，超过 0.3 且有体力时把有效淹没维持在 0.3，体力耗尽后下沉。氧气安全线读取接收器配置（脚本默认 0.6），不得把视觉阈值当玩法阈值。水体遮罩和减速使用有效淹没，世界液深不被漂浮效果改写。
- `Mod_TileEffectReceiver` 的邻接水格容错只服务于水边交互；`WorldLiquidBehaviour` 必须根据 `IsActiveTileEdgeInteractionOnly` 阻断浸没视觉、脚底阴影、Buff 和移动速度效果，避免站在沙格边缘的角色被误判为入水。
- Liquid 接触独立于 Ground：WorldLiquidBehaviour 的 OnEnter/OnUpdate/OnExit 直接接收 WorldLiquidSourceTarget，不继承 TileBlockBehaviour，也不构造临时水地块数据。同一液体内移动或静止抽水只刷新采样，液体身份/边缘接触模式变化才 Exit/Enter。液体切换保留同帧下沉状态；LiquidFloating 只触发 Ground 边界回调，不清空液体效果。
- 雪地脚印等带历史轨迹的地表表现不能在离开积雪覆盖时清空历史；跨相邻雪格只停止或继续新轨迹采样，让已有轨迹按自身寿命逐步淘汰。`SnowFootprintTrail` 由独立 `snow.depth` 覆盖接触运行时 `AddComponent`，默认表现资源不能只依赖 Prefab/Inspector 预先赋值，必须保证动态创建后也能解析到专用 Shader/材质配置；脚印使用 `ground-mark` 类别，位于 BRG 地表之上、世界实体层之下。
- `Assets/2_Prefabs/Gameplay/Modules/Rendering/Shadow.prefab` 是 URP `ShadowCaster2D` 投影组件，不是实体脚底贴图；实体可视阴影应复用 `ActorShadowManager` 的独立注册和水体显隐入口。
- `Presentation/Effects/Runtime/` 受独立 `Effect.asmdef` 隔离，不能反向引用主 `GamePlay` 程序集中的 `VisualEffectManager`；需要名称池管理器的角色表现控制器应放在 `Presentation/` 主程序集，或先抽取无环依赖的公共契约。
- Editor 脚本留在 Editor 程序集/目录；生产程序集不得反向引用 `FlatWorld.Gameplay.Debug`。
- `Development/Debug` 使用独立的 `FlatWorld.Gameplay.Debug.asmdef`；GM 调试脚本直接使用 `GameNetwork` 时，需在该 asmdef 显式引用 `FlatWorld.Networking.Core`，引用 `GamePlay` 不会传递其程序集依赖。
- Editor Play 的 Render Streaming 使用项目自有 `RenderStreamingSettings` 资产并先确认官方 WebApp 已在选定端口可访问；不得把会话信令地址写回不可变的 PackageCache 默认资产，也不得假定 TCP 80 可用。`VideoStreamSource.Screen` 会在每帧结束执行整屏捕获与纹理转换，因此普通 Play/Profiler 压测默认关闭 Automatic Streaming，只能通过显式开发者开关或“启动手机浏览器测试服务器”入口启用；该启动入口必须同步保存自动串流偏好，避免下一次进入 Play 又被性能模式关闭。端口被系统或其它程序占用时从项目约定端口段选择空闲端口，并让信令 URL、WebApp 启动参数和展示给开发者的访问 URL 始终保持一致。工具自己启动的 WebApp 要保存 PID 以跨 Domain Reload 回收，但停止前必须核对进程可执行文件确实是当前项目缓存的 `webserver.exe`，防止 PID 复用误杀其它程序。开发期速度优先编码应优先选择本机可用的 H.264 Constrained Baseline（通常对应硬件编码），不可用时保持 WebRTC 默认协商，不得强制一个目标浏览器不支持的编码器。
- 运行时世界由 `SceneManager.CreateScene` 动态创建，不会触发 `SceneManager.sceneLoaded`；监听运行时 Hierarchy 的 Editor 工具必须同时处理旧场景卸载与后续 `hierarchyChanged`，且不能用无界切换标记长期屏蔽用户操作。`hierarchyChanged` 热路径必须从少量已保存记录定向解析对象，禁止组合 `Resources.FindObjectsOfTypeAll` 与 `GlobalObjectId.GetGlobalObjectIdSlow` 全场景扫描，否则跨场景引用会制造警告并造成 `EditorLoop` 尖峰；调用 `GlobalObjectIdentifierToObjectSlow` 前必须确认 ID 所属场景已加载，场景切换空窗直接跳过，否则 Unity 原生层会触发 `manager != NULL` 断言。
- 内容工坊保持在 `Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`，只把可验证的差异写回 JSON，不在运行时程序集引入编辑器依赖。
- 业务日志用 `GameLogManager` 的 `[WORK]` 接口；不要制造每帧重复警告。
- `GMReflectionConsole` 独占 F4 作为 GM 调试面板开关；管理员手持物品加量由面板按钮调用，`GameDebugManager` 的晴天快捷键必须在脚本默认值与 `WorldManager.prefab` 序列化值中都使用 F6，禁止运行时反射改键。
- F3 环境监测由 `GameDebugManager` 切换；`EnvironmentInfoDisplay` 必须通过 `ChunkMgr.TryGetRuntimeTerrainTile` 读取当前 WorldModel 权威格子，并从 `ChunkTerrainData.EnvironmentLayerIds` 枚举原始环境层，禁止重新依赖旧 `Chunk.Map/Map.Data.EnvironmentLayers`。耕地水肥读取 `FarmlandSystem` 的农业层，最终环境温度使用 `TemperatureMgr.TryGetAmbientTemperature`。
- GM 世界观察层属于 `Development/Debug/GMWorldLayerOverlay`，温度与污染共用一张低分辨率点采样纹理并互斥切换；纹理必须与整数世界格对齐，采样按相机视口和每帧预算分批完成后统一上传，换世界时丢弃旧批次，未加载格透明。污染总览读取全部已注册污染定义（含 MOD）的最高归一化负荷；禁止每格创建 Renderer、为可视化租住区块或向地形回写颜色。Shader 用 Resources 引用保证构建保留，关闭观察层停止采样。
- GM 导航模式与热力图互斥，共用四边形但方向纹理必须使用线性色彩空间、Point 过滤和“一真实格一 texel”，不能沿用热力图的远景降采样。箭头读取后端真实玩家流场；采样 Job 向导航缓存登记读取依赖，上传、换模式、释放前完成任务。自有输出可跨帧，借用的 Native 表不可在后端重新发布后继续访问；目标格、不可达格与未知/阻挡格分别显示蓝点、红叉和透明，不得用直指玩家的箭头伪装寻路结果。

- 季节覆雪通过统一 MPB 效果模块的 `_SnowCoverage` 通道叠加；实际物品基础材质必须支持该属性，仅给默认 Sprite 材质设置 MPB 不会显示雪。保持风、水、受击和溶解等既有通道。
- `ChunkSnowCoverRenderer`、`ChunkSupportSurfaceRenderer` 只读权威状态并提交专用 BRG 图层，卸载仅清理自身实例槽；禁止为了融雪或解绑改写原始地形。`ChunkSupportSurfaceRenderer` 使用 BRG 实例 Data0 RGBA 编码左、右、下、上四个外露平台边缘，并读取相邻 Chunk 的 `TerrainSupportLayer`；相连支撑面之间对应通道必须为 0。覆盖在原始液体上的平台把阴影绘制到平台外侧以表现浮于水面，陆地地板仍保留内侧接触阴影。角色和动物不装配静止物件雪效。

## AIECS 渲染原型

- ECS 脚底阴影由 `AiecsShadowRenderer` 独立合批：每批最多 4096 只、固定 16 位索引、24 字节顶点，复用主体可见列表与 Display 水态，不创建逐实体 GameObject，也不额外查询地形。ECS 使用 `Shadow/0 + Queue 2991`，旧 Item BRG 使用 Default/2995；两者共用 `GetShadowOpacity(scene)` 昼夜入口、可见底边内缩脚点与全局颜色。主体批次数与阴影批次数必须分开统计。相机缺失、空帧、关闭阴影、释放世界都必须隐藏/回收旧批次。
- 阴影脚底使用待机帧的固定非透明 `VisibleRect`，不能使用含大面积留白的完整帧矩形，也不能跟随攻击/奔跑逐帧伸缩。导出器从已解包像素记录边界；既有图集可用 `FlatWorld/AIECS/阴影 更新非透明边界` 只补目录数据，不重导图集、不改源贴图。`阴影 验证批次与生命周期` 的离线验证不等于真实世界、截图或设备性能验收。

- 表现代码在独立 `AIECS/Presentation` 程序集，正式 `AiecsWorldRenderer` 只读模拟提交后的 Display，不复用 AiecsPrototypeMotion 作为行为。共享动画目录、图集及移动脚本的 GUID 必须保留；开发显示的有限 Y 行批次和血条便于手测，不等于旧世界精确透明混排、实际水深或 GPU 性能门槛通过。

- `Entities/AIECS/` 通过原生 `Universal2D/NormalsRendering` 绘制排序后连续的精灵批次，禁止按物种/材质全局重排或在管线末尾补画并假设 Light2D 自动正确。当前 CPU 合并网格只用于兼容性原型；颜色批次统计不等于 GPU 实测 draw 数或两万同屏证据。运行时批次固定使用 `AIECSRuntime` Layer；GameView/构建相机保持正常渲染，Editor 的 SceneView 默认从 `Tools.visibleLayers` 排除该层，压测时避免同一批动态 Mesh 被 SceneView 重复绘制，需要观察时再通过 `Tools/AIECS/SceneView 显示运行时批次` 显式开启。
- `AiecsLegacySortScope` 仅用于单相机独立场景的完整 Sprite 显示范围，临时覆盖外部 Order 并在停用时恢复，内部 SortingGroup 保持原绘制职责；Tilemap、粒子、嵌套组和动态正式世界尚未适配，不得直接套到半个正式场景。
- 动画从启用的 Actor Manifest 与实际覆盖控制器导出，Sprite 按真实三角形/UV 解包后保存原尺寸、Pivot、PPU。非循环时间轴必须包含终点姿态，不能复用循环动画的去尾规则；攻击曲线/事件只是待映射标记。重导使用所选生成目录的显式菜单以保留 GUID，禁止在导入或打开场景时自动覆盖资源；特殊法线、Mask、附属物和正式运行时加载仍需各自适配。

## 验证

- 掉落物沉没包含入水和完全浸没后的远离两段。ECS 的 `SubmergedProgress` 只乘表现矩阵，原始 Scale/质量/体积不变；保存延续同一 WaterElapsed，拾取、离水和旧 Item 回池时都恢复原始视觉尺寸。销毁在远离阶段结束后，由权威层提交。
- 草与花的 BRG 图层共用 Default/0 与 Queue 2993；花批次排序在草之后，两者都位于 Shadow 地表表现与 Player 世界实体之下。GroundCoverAssetBuilder 必须引用草的共用材质并清除旧 Flowers Tilemap，不能重新装回 Tilemap 或抬高 Order。
- 静止/深湖仍保留独立时钟的细波和微弱天空反光；湖泊物理流速为零不能再把所有光学强度乘成零。河口的反射波速度只属于 `Chunk-BRG-Water-Lit`，海洋仍单独使用风浪/潮汐时钟，保持真实日夜光照。
- 正式 BRG 海面通过实例 `FlowX/FlowY` 读取环境层 `windX/windY` 派生的单位方向；水面材质只控制非负浪速，不决定流向。写实与风格化主浪脊相位都须沿该方向前进，避免与玩家漂移反向。

- `DroppedItemPresentation` 按有限空间行、贴图和排序层增量合并掉落物真实 Sprite 三角形/UV；静止批次不重复上传，视野外释放显示节点。ECS 掉落物及拾取反馈必须读取 `WorldSortingManager` 的 `world-item` 类别，不能继承物品 JSON/Prefab 的 `Default/0` 原始排序，否则会被 BRG 地形盖住。该兼容渲染桥不等于已验证与所有旧 Item 的精确透明混排或设备性能指标。
- 掉落共享材质放在 `Assets/9_Shaders/Resources/DroppedItems`，复用原生 Universal2D/NormalsRendering Shader。批次水线使用规范世界坐标，循环镜像通过 `_WaterLineOffset` MPB 补偿；默认值必须为 0，不能改变原 AIECS 材质语义。

- 自动断言对象、材质、池化生命周期和关键参数；最终粒子/Shader 观感才做定向视觉检查。
- 触发属于战斗、天气、UI 或音频时加载对应领域 Skill。

## Skill 维护原则

- 载具尾波只消费实际水面位移，使用限额世界空间粒子复用共享环形材质；每个发射点也需确认水面，不能由按键输入决定发射或把尾波挂在船体层级随船一起平移。尾波排序必须跟随载具主体 Sorting Layer，并使用主体 Order 的后一层，禁止硬编码 `Default` 固定层级，否则动态实体切到 `Player` 层后尾波可能被水面遮住。停用、回池和换世界必须清理发射器。

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
