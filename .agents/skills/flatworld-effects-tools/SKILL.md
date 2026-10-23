---
name: flatworld-effects-tools
description: "FlatWorld 特效、Shader、世界排序与光影、水体渲染、批量表现及编辑器/调试工具。"
---

# FlatWorld 特效与工具

## 入口与职责

- 渲染资源：`Assets/9_Shaders/{Shader,Material,Volume,Resources}/`；保留 Resources 逻辑路径和移动资产的 GUID，第三方资源独立存放。
- 表现代码：`Assets/5_Scripts/5-3_GamePlay/Presentation/`。`Effects/Management/VisualEffectManager` 管池，`Effects/Runtime/` 为独立 Effect.asmdef；`ActorRenderEffectController` 合成角色 MPB；`WorldSortingManager/WorldSortingMember` 管排序；`ActorShadowManager/WorldShadowProjectionManager` 管接触/太阳阴影。
- 默认值：`Assets/Resources/GameConfig/Rendering/default-rendering.json`；Prefab 持有资源与逐物体外观，PlayerPrefs 持有本机偏好。表现只消费权威状态，不结算伤害、液量、引擎耗油或角色迁移。
- AIECS：`Entities/AIECS/Presentation/`；开发工具：`Assets/5_Scripts/5-3_GamePlay/Development/{Debug,Diagnostics}/`；编辑器：`Assets/Editor/FlatWorld/`，内容工坊在 `ContentTools/ContentWorkshop/`。
- Effect 不反向引用 GamePlay 管理器；生产程序集不引用 FlatWorld.Gameplay.Debug；Debug 使用 GameNetwork 时显式引用 Networking.Core。

## 共用契约

- 排序类别由 WorldSorting JSON 经 GamePlay 桥接 AIECS。动态实体共用 Player，地面印记/水花/预览走 Shadow，离体特效走 world-effect；SortingGroup 只设最外层键，内部保持相对序，代理继承外层有效键。贴地设施为 ground-building，Carrier 判定先于 Building。
- 模拟与增量输入保持规范坐标，本机镜像/深度偏移只在表现层投影；实例、裁剪和 CPU 包围盒同步，Shader 位移必须外扩 CPU 边界。按实际排序键/姿态/源帧变化提交原生属性，仍保留每帧必要的状态读取。
- 角色 Shader 参数统一经 MPB 控制器合成。静态模块设 `RequiresContinuousRendering=false`，变化时 `MarkRenderStateDirty`；换图、重绑、启停仍刷新。外部 Renderer 创建后注册、销毁前注销；描边代理标 `ActorRenderEffectExclude`，不让角色合成覆盖独立通道。
- `RequiresSpriteBodyRange` 默认开启；仅写独立通道的模块可关闭，水线仍跟踪换图。代理同步源贴图、分离 Alpha、UV/裁剪与 MPB，Universal2D、NormalsRendering、回退 Pass 使用同一坐标/阈值；SpriteRenderer 与 MeshRenderer 分节点、共用生命周期根。
- URP 2D Lit 必须实际采样 Shape Light；全局 Shader 参数放全局声明，避免材质 Properties 默认值遮蔽。HLSL 避开 `point/line/triangle` 等保留字；共享代码改动覆盖全部包装 Pass。
- 池取出重置 Transform、Animator、颜色与寿命，回收清订阅。一次性粒子在源销毁前脱离；需要角色 OnDisable 回收的特效用独立根，Owner 与父级分离。睡眠只释放可见实例，状态请求仍归角色。
- 完整 GameRes 的 `combat-prewarm` 按正式武器引用去重，每个命中 GameEffect 分帧准备 3 个空闲实例；`PrepareForPool` 不播放、不消费随机数。完整资源会话/世界释放旧代前清池，原位更新候选不预热。延迟回收队列保留未到期任务，提前回收取消旧任务，新租用不能继承旧截止时间。
- `SharedSpriteMeshCache.IsSessionEnding` 时停止区块提交、分帧推进与 Owner 恢复；活跃期登记/恢复失败保留异常。BRG 禁用/域重载幂等 Dispose：先释放组，再 finally 释放自有缓冲/Mesh/材质，按需重建。

## 按主题读取

只读取当前改动需要的参考，不批量加载。

| 当前任务 | 参考 |
| --- | --- |
| 排序、角色 MPB、描边/风、光照阴影、后处理/云/宇宙观察 | [渲染与光影](references/rendering.md) |
| 水体材质/流向、水深、地表/雪、淹没与包内变体 | [水体与地表](references/water-terrain.md) |
| 粒子、战斗/飞船反馈、AIECS、掉落物与 BRG | [特效与批量表现](references/effects-batching.md) |
| 性能导出、GM、资源刷新、日志、串流与本地打包 | [编辑器与调试](references/editor-debug.md) |
