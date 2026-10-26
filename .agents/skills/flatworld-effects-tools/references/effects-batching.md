# 特效与批量表现

## 粒子与反馈

- ParticleSystem 显式持 URP 材质，Velocity 三轴 MinMax 模式一致；EmitParams.rotation 为度，2D 方向取 `-SignedAngle(up,direction)`。
- 降雨走 `RainEffectController` 与 Resources/Weather/RainEffect：单个2048雨丝固定网格、共享 RainStreak 材质，顶点 Shader 推进三档远近雨丝与随风倾斜；CPU 仅同步镜头/平滑雨量/整层风位移，差异提交 MPB，覆盖与 localBounds 包含最长雨丝。落地水花独立保留；规则 `environment/rain_visual_batch` 不代表画面或帧率已验证。
- 降雪走 `SnowEffectController` 与 Resources/Weather/SnowEffect：单个1500雪花固定网格、共享 Snowflake 材质，顶点 Shader 负责下落/摆动/视口外环绕；CPU 仅同步相机、平滑天气强度和整层风位移，参数变化才交 MPB，覆盖变化同步 Renderer.localBounds，销毁释放自有 Mesh。禁止重新生成雪花 ParticleSystem；规则 `environment/shader_snow` 不代表画面或帧率已验证。
- 常驻燃烧统一走 `ShaderFlameEffect`：借用内置 Quad、共享 Resources/Combustion_Flame 材质，`_Time` 驱动火舌/火星，无逐帧动画脚本；实例尺寸/火势/相位用 MPB，仅变化或重启时提交，同时更新 Renderer.localBounds。通用入口 `Assets/2_Prefabs/Effects/VFX_ShaderFlame.prefab`，自定义颜色/速度/风向/像素感可另建材质。
- 可选常驻烟雾走 `ShaderSmokeEffect` 与 Resources/Combustion_Smoke：共享 Quad，Shader 推进八个错相烟团，无 CPU 粒子；`Mod_CombustionVisual.smokeEnabled/smokeAnchorOffset/smokeSize/smokeDensity` 配置，锚点相对火焰点，当前仅高炉开启。`CombustionVisualEffect` 桥接真实燃烧/排序/启停，规则 `rendering/combustion_shader_smoke` 覆盖配置与表现生命周期，不代替实际点火画面验收。
- 火焰默认 32 PPU 世界尺寸方格、四色硬边和单像素火星，GPU 时间按 12 FPS 分帧；只改 Shader 动画采样，不降低宿主位置/排序同步频率。手持火把定义旋转 315° 对齐本地 +X，燃烧头仍跟随 Sprite；手持/放置火把设 `attachFlameToAnchor=false` 保持世界向上。
- `CombustionVisualEffect` 只桥接燃烧、宿主锚点/翻转、源光开关和相对排序；`BindWorldPresentation` 支持无 SpriteRenderer 的行网格。两种掉落后端共用 `WorldItemCombustionVisual`，熄灭/回池隐藏网格；原发射倍率映射为火势，不再模拟持续粒子。Resources/Combustion_Particle 仅保留入水时的一次性烟汽，离体后继续播完。MPB 不走 SRP Batcher，不把共享资源或 instancing 变体当成实际合批/帧率已验证。
- `ProjectileFlightTrailPresenter` 只读飞行/Sprite，层级/排序变化才写 Renderer；池重发仅颜色改变才重建渐变，TrailRenderer 重建强制初交。`DamageTextEffect` 持样式/倍率；`CombatMissFeedbackPresenter` 消费 CombatFeedbackEvents 逻辑未命中快照，不参与伤害。
- `LightningEffectController` 消费权威落点，Resources/Weather/Shaders/LightningBolt 画分叉/白芯/外光/电弧和短闪；有限池共享 owned Mesh/Material、每事件 MPB，逐帧投影镜像，换世界/禁天气即释放。
- 载具尾波按实际水面位移、逐发射点确认水面，世界粒子限额，随主体层与 Order+1；停用/回池/换世界清理。`ShipEngineExhaustPresenter` 读取实际耗油后出力，Resources/ShipEngineExhaust 材质 GUID 为32位十六进制；局部粒子按 vehicle -3，单引擎≤48，停推 StopEmitting 且残留继续同步，禁用 StopEmittingAndClear。
- `SpaceStarfield` 固定420星点，Resources/Space/Starfield 三层视差/环绕/闪烁；CPU 仅传双精度镜头位移，单 Mesh 九宫格补边、固定像素大小，分辨率变化才重建，退出释放 Mesh/Material/恢复背景。

## AIECS、掉落与 BRG

- `AiecsWorldRenderer` 只读模拟后 Display，按排序后连续批次走原生 Universal2D/NormalsRendering，主体/阴影独立统计。AIECSRuntime 层默认只在 SceneView 隐藏。
- `AiecsShadowRenderer` 每批≤4096、16位索引/24字节顶点，复用可见表与 Display 水态；太阳阴影读全局太阳/当前图集。缺相机/空帧/关闭/退出时隐藏或回收。
- AIECS 从真实 Actor 首图、外壳状态贴图和动画模块参数导出单图目录，保留三角形/UV、尺寸/Pivot/PPU；程序摇摆结果同时供 BRG、裁剪和太阳阴影使用。重导仅显式菜单并保 GUID，PlayMode 不重导。`AiecsLegacySortScope` 用于单相机完整 Sprite 场景。
- `DroppedItemPresentation` 按空间行/贴图/层增量合并真实 Sprite，静止不重传、离视野释放；类别 world-item，材质 `Assets/9_Shaders/Resources/DroppedItems`，水线为规范坐标，用默认零 `_WaterLineOffset` 补镜像。
- DOTS 实例读取用 `DOTSInstanceData_Load4` 兼容 GLES 常量缓冲；各 Pass 自行引入使用的二维调试类型。

## 定位性能

- MPB Marker：`FlatWorld.ActorRender.{Frame,UpdateModules,SubmitRenderers,RefreshBindings}` 均为包含式耗时；Modules/Submit.work 为模块/Renderer数，Bindings.work 为收集后数，Frame 无 work。`IdleSkippedFrames/UnchangedSpriteRenderers/SubmittedRenderers` 区分整轮跳过/源帧未变/实际提交。
- `Snow.SampleCoverage.work` 为实际采样，CooldownSkips/ExcludedSamples/CoverageChanges 为冷却/排除/变化；保持原0.5秒规则，每 Renderer 共用 Sprite/归属读取。Profiler 始终可见，GM 性能 DEBUG 开启才累计数量，慢摘要独立；计数不证明提速或画面正确。
