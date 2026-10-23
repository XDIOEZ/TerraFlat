# 特效与批量表现

## 粒子与反馈

- ParticleSystem 显式持 URP 材质，Velocity 三轴 MinMax 模式一致；EmitParams.rotation 为度，2D 方向取 `-SignedAngle(up,direction)`。
- `CombustionVisualEffect.BindWorldPresentation` 给无 SpriteRenderer 的行网格复用模板/显式排序；缓存三路 Renderer，配置变化才写模板尺寸、排序/锚点/缩放变化才提交，熄灭清粒子，不改持续发射或源光关闭规则。两种掉落后端共用 `WorldItemCombustionVisual` 和 Resources/Combustion_Particle。
- `ProjectileFlightTrailPresenter` 只读飞行/Sprite，层级/排序变化才写 Renderer；池重发仅颜色改变才重建渐变，TrailRenderer 重建强制初交。`DamageTextEffect` 持样式/倍率；`CombatMissFeedbackPresenter` 消费 CombatFeedbackEvents 逻辑未命中快照，不参与伤害。
- `LightningEffectController` 消费权威落点，Resources/Weather/Shaders/LightningBolt 画分叉/白芯/外光/电弧和短闪；有限池共享 owned Mesh/Material、每事件 MPB，逐帧投影镜像，换世界/禁天气即释放。
- 载具尾波按实际水面位移、逐发射点确认水面，世界粒子限额，随主体层与 Order+1；停用/回池/换世界清理。`ShipEngineExhaustPresenter` 读取实际耗油后出力，Resources/ShipEngineExhaust 材质 GUID 为32位十六进制；局部粒子按 vehicle -3，单引擎≤48，停推 StopEmitting 且残留继续同步，禁用 StopEmittingAndClear。
- `SpaceStarfield` 固定420星点，Resources/Space/Starfield 三层视差/环绕/闪烁；CPU 仅传双精度镜头位移，单 Mesh 九宫格补边、固定像素大小，分辨率变化才重建，退出释放 Mesh/Material/恢复背景。

## AIECS、掉落与 BRG

- `AiecsWorldRenderer` 只读模拟后 Display，按排序后连续批次走原生 Universal2D/NormalsRendering，主体/阴影独立统计。AIECSRuntime 层默认只在 SceneView 隐藏。
- `AiecsShadowRenderer` 每批≤4096、16位索引/24字节顶点，复用可见表与 Display 水态；太阳阴影读全局太阳/当前图集。缺相机/空帧/关闭/退出时隐藏或回收。
- 动画从真实 Actor Manifest/覆盖控制器、三角形/UV 导出，保留尺寸/Pivot/PPU与非循环终点；重导仅显式菜单并保 GUID。`AiecsLegacySortScope` 用于单相机完整 Sprite 场景。
- `DroppedItemPresentation` 按空间行/贴图/层增量合并真实 Sprite，静止不重传、离视野释放；类别 world-item，材质 `Assets/9_Shaders/Resources/DroppedItems`，水线为规范坐标，用默认零 `_WaterLineOffset` 补镜像。
- DOTS 实例读取用 `DOTSInstanceData_Load4` 兼容 GLES 常量缓冲；各 Pass 自行引入使用的二维调试类型。

## 定位性能

- MPB Marker：`FlatWorld.ActorRender.{Frame,UpdateModules,SubmitRenderers,RefreshBindings}` 均为包含式耗时；Modules/Submit.work 为模块/Renderer数，Bindings.work 为收集后数，Frame 无 work。`IdleSkippedFrames/UnchangedSpriteRenderers/SubmittedRenderers` 区分整轮跳过/源帧未变/实际提交。
- `Snow.SampleCoverage.work` 为实际采样，CooldownSkips/ExcludedSamples/CoverageChanges 为冷却/排除/变化；保持原0.5秒规则，每 Renderer 共用 Sprite/归属读取。Profiler 始终可见，GM 性能 DEBUG 开启才累计数量，慢摘要独立；计数不证明提速或画面正确。
