# 特效与批量表现

## 粒子与反馈

- ParticleSystem 显式持 URP 材质，Velocity 三轴 MinMax 模式一致；EmitParams.rotation 为度，2D 方向取 `-SignedAngle(up,direction)`。
- 雨雪分别由 `RainEffectController`/`SnowEffectController` 配参数并共用 `ShaderPrecipitationEffectControllerBase` 的固定网格、相机覆盖、风/镜头位移、MPB 与生命周期；2048雨丝仍用 seed 78041、UV3.x 阈值和 `_RainWind`，1500雪花仍用 seed 65021、每点额外雪摆相位和 UV3.y 阈值。雨的材质余边与雪固定0.3覆盖分别保留；规则 `environment/rain_visual_batch` / `environment/shader_snow` 不代表画面或帧率已验证。
- 常驻燃烧统一走 `ShaderFlameEffect`：借用内置 Quad、共享 Resources/Combustion_Flame 材质，`_Time` 驱动火舌/火星，无逐帧动画脚本；实例尺寸/火势/相位用 MPB，仅变化或重启时提交，同时更新 Renderer.localBounds。通用入口 `Assets/2_Prefabs/Effects/VFX_ShaderFlame.prefab`，自定义颜色/速度/风向/像素感可另建材质。
- 可选常驻烟雾走 `ShaderSmokeEffect` 与 Resources/Combustion_Smoke：共享 Quad，Shader 推进八个错相烟团，无 CPU 粒子；`Mod_CombustionVisual.smokeEnabled/smokeAnchorOffset/smokeSize/smokeDensity` 配置，锚点相对火焰点，当前仅高炉开启。`CombustionVisualEffect` 桥接真实燃烧/排序/启停，规则 `rendering/combustion_shader_smoke` 覆盖配置与表现生命周期，不代替实际点火画面验收。
- 火焰默认 32 PPU 世界尺寸方格、四色硬边和单像素火星，GPU 时间按 12 FPS 分帧；只改 Shader 动画采样，不降低宿主位置/排序同步频率。手持火把定义旋转 315° 对齐本地 +X，燃烧头仍跟随 Sprite；手持/放置火把设 `attachFlameToAnchor=false` 保持世界向上。
- `CombustionVisualEffect` 只桥接燃烧、宿主锚点/翻转、源光开关和相对排序；`BindWorldPresentation` 支持无 SpriteRenderer 的行网格。两种掉落后端共用 `WorldItemCombustionVisual`，熄灭/回池隐藏网格；原发射倍率映射为火势，不再模拟持续粒子。Resources/Combustion_Particle 仅保留入水时的一次性烟汽，离体后继续播完。MPB 不走 SRP Batcher，不把共享资源或 instancing 变体当成实际合批/帧率已验证。
- `ProjectileFlightTrailPresenter` 只读飞行/Sprite，层级/排序变化才写 Renderer；池重发仅颜色改变才重建渐变，TrailRenderer 重建强制初交。`DamageTextEffect` 持样式/倍率；`CombatMissFeedbackPresenter` 消费 CombatFeedbackEvents 逻辑未命中快照，不参与伤害。
- `LightningEffectController` 消费权威落点，Resources/Weather/Shaders/LightningBolt 画分叉/白芯/外光/电弧和短闪；有限池共享 owned Mesh/Material、每事件 MPB，逐帧投影镜像，换世界/禁天气即释放。
- 载具尾波按实际水面位移、逐发射点确认水面，世界粒子限额，随主体层与 Order+1；停用/回池/换世界清理。`ShipEngineExhaustPresenter` 读取实际耗油后出力，Resources/ShipEngineExhaust 材质 GUID 为32位十六进制；局部粒子按 vehicle -3，单引擎≤48，停推 StopEmitting 且残留继续同步，禁用 StopEmittingAndClear。
- `SpaceStarfield` 固定420星点，Resources/Space/Starfield 三层视差/环绕/闪烁；CPU 仅传双精度镜头位移，单 Mesh 九宫格补边、固定像素大小，分辨率变化才重建，退出释放 Mesh/Material/恢复背景。

## AIECS、掉落与 BRG

- 两种 AIECS 阴影共用 `AiecsShadowRenderer.InitializeQuadBatchTriangleIndexBuffer` 初始化16位Quad索引，材质、顶点、批次容量与生命周期仍各自负责。
- `AiecsWorldRenderer` 只读模拟后 Display，按物种定义的图层、顺序和相同脚点 Y 复用原生 MeshRenderer，跨 Y 不能合批；真实脚点是网格包围盒中心，资源发布时更新物种排序。主体/阴影独立统计，`BatchCount/UploadedBytes` 记录本帧主体批数与顶点索引提交量，AIECSRuntime 层默认只在 SceneView 隐藏；可见生物多且 Y 各异时绘制批次增加，实机性能仍需验证。
- `AiecsShadowRenderer` 每批≤4096、16位索引/24字节顶点，复用可见表与 Display 水态；太阳阴影读全局太阳/当前图集。缺相机/空帧/关闭/退出时隐藏或回收。
- AIECS 从真实 Actor 首图、外壳状态贴图和动画模块参数导出单图目录，保留三角形/UV、尺寸/Pivot/PPU；程序摇摆结果同时供主体网格、裁剪和太阳阴影使用。重导仅显式菜单并保 GUID，PlayMode 不重导。`AiecsLegacySortScope` 用于单相机完整 Sprite 场景。
- `DroppedItemPresentation` 按空间行/贴图/层增量合并真实 Sprite，静止不重传、离视野释放；类别 world-item，材质 `Assets/9_Shaders/Resources/DroppedItems`，水线为规范坐标，用默认零 `_WaterLineOffset` 补镜像。
- DOTS 实例读取用 `DOTSInstanceData_Load4` 兼容 GLES 常量缓冲；各 Pass 自行引入使用的二维调试类型。

## 定位性能

- MPB Marker：`FlatWorld.ActorRender.{Frame,UpdateModules,SubmitRenderers,RefreshBindings}` 均为包含式耗时；Modules/Submit.work 为模块/Renderer数，Bindings.work 为收集后数，Frame 无 work。`IdleSkippedFrames/UnchangedSpriteRenderers/SubmittedRenderers` 区分整轮跳过/源帧未变/实际提交。
- `Snow.SampleCoverage.work` 为实际采样，CooldownSkips/ExcludedSamples/CoverageChanges 为冷却/排除/变化；保持原0.5秒规则，每 Renderer 共用 Sprite/归属读取。Profiler 始终可见，GM 性能 DEBUG 开启才累计数量，慢摘要独立；计数不证明提速或画面正确。
