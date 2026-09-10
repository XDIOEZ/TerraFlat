---
name: flatworld-effects-tools
description: "Use when: 定位或修改 FlatWorld 的运行时特效、粒子、伤害文字、水体效果、Shader、视觉管理器、项目编辑器工具、结构编辑器、调试脚本或测试辅助。关键词：VisualEffectManager、SpecialEffects、Shader、Editor、GameDebugManager。"
---

# FlatWorld 特效、Shader 与工具

## 入口

- 项目自有 Shader 资源统一放在 `Assets/9_Shaders/`：Shader 源文件放 `Shader/`，材质放 `Material/`，Volume 配置放 `Volume/`；必须依赖 `Resources.Load` 的 Shader/材质放在 `Assets/9_Shaders/Resources/` 下并保持原逻辑资源路径。不要再创建 `Assets/Shaders`、`Assets/Resources/Shaders` 或其它散落的项目自有 Shader 资源目录；第三方插件资源保持原目录不移动。
- 运行时视觉：`Assets/5_Scripts/5-3_GamePlay/Presentation/Effects/Management/VisualEffectManager.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/Effects/Runtime/`
- 角色渲染：`Assets/5_Scripts/5-3_GamePlay/Presentation/{ActorRenderEffectController,ActorRenderColorEffect,WaterImmersionRenderEffect}.cs`
- 实体脚底阴影：`Assets/5_Scripts/5-3_GamePlay/Presentation/ActorShadowManager.cs` 与 `Assets/2_Prefabs/Gameplay/Modules/Rendering/ActorShadow.prefab`；阴影使用场景级 `ActorShadows` 根节点和 `Shadow` Sorting Layer，不挂到实体或 `RuntimeEntities` 下。
- 编辑器工具：`Assets/Editor/FlatWorld/`、`Assets/Editor/FlatWorld/ProjectTools/`；内容工坊入口为菜单 `FlatWorld/内容配置/内容工坊`
- 调试：`Assets/5_Scripts/5-3_GamePlay/Development/Debug/`、`Development/Diagnostics/{GameDebugManager,GameLogManager}.cs`

## 不变量

- 先确认触发系统及 Prefab/材质/Shader 的真实引用来源，再改表现。
- 池化特效每次取出时重置 Transform、Animator、颜色和生命周期；回收/禁用时清理订阅与状态。
- 需要在角色 `OnDisable` 中立即回收的池化特效不能挂到该角色层级下，否则归池 `SetParent` 会与父级激活/停用过程冲突；Owner 登记与 Transform 父级分开，睡眠 ZZZ 由单位缩放的独立特效根节点持有并在 `LateUpdate` 跟随。区块休眠不等于退出 AI 睡眠状态，停用时只释放可见实例，重新激活时恢复仍有效的表现请求。
- 粒子 `VelocityModule` 的线性 X/Y/Z 必须使用同一种 `minMaxState`；2D 特效即使 Z 速度恒为零，也应使用与 X/Y 相同的模式并把上下限都设为零，避免 `Particle Velocity curves must all be in the same mode`。
- `ParticleSystem.EmitParams.rotation` 使用欧拉角度数；2D `Billboard` 粒子按世界移动方向旋转时，屏幕旋转正负方向与 `Vector2.SignedAngle(Vector2.up, direction)` 相反，应使用其反号，否则水平/垂直方向看似正常但 45° 斜向会转成垂直朝向。
- 伤害数字的最终颜色由 `DamageTextEffect` 样式或调用数据覆盖，不能只改 TMP 的 Prefab 字色；数值到显示倍率的映射也由该表现组件负责，战斗结算只传递实际伤害值与样式。
- 角色颜色等共享 Shader 参数通过现有 MPB 控制器提交，避免多个组件互相覆盖。
- Unity 2D 使用 URP/Light2D；修改 Shader 前核对材质实际 Shader 与 Pass。
- 共用海水 `UsePass` 的包装 Shader 必须声明公共 Pass 新增的同名材质属性；月光等夜间自发光倒影应在 `CombinedShapeLightShared` 之后合成，避免全局夜间光照被重复相乘。月亮出现动画读取 `DayTimeSystem` 发布的 `_GlobalMoonAppearance`，尺寸/渐亮与 `_GlobalMoonlightIntensity` 的月相亮度分离，避免新月把月面永久缩小。
- Water Tilemap 的 Tile Color RGBA 只编码左、右、下、上岸线方向；水深必须由每个 Chunk 独立的带一格邻区边框纹理提供，并在格子中心之间使用双线性采样，禁止再把连续水深与岸线位打包进同一颜色通道。包装 Shader 必须继续声明公共 Pass 使用的全部属性。
- Tilemap 合批后 `POSITION` 不保证是 Chunk 局部坐标；水深与岸线使用世界坐标，MPB 的 `_WaterDepthUvScaleOffset` 必须扣除水层原点再加入一格纹理边框。当前世界网格每格为 1 单位且原点对齐整数，不要用 `unity_WorldToObject` 恢复已被合批丢失的局部坐标。
- 水面潮流使用 `DayTimeSystem` 发布的 `_GlobalGameDay` 驱动，并沿材质 `_FlowDirection` 轴按 `_TideCyclesPerDay` 往返；方向性水纹不要改回基于 `_Time` 的持续旋转，否则跳时、读档与游戏时间倍率会和潮汐表现脱节。
- 写实水面的风浪传播与潮流平移必须分开：波相位随游戏时间连续推进，潮汐只平移水面坐标，避免潮流换向时整片海面停住；波面法线与太阳高光共用解析波斜率，缩小时通过屏幕导数衰减细浪并拓宽高光，不量化写实水面的世界坐标。风格化水面按其独立算法保留像素采样与浪纹。
- 水体风格由 `WaterVisualSettings` 保存本地偏好，`ChunkView` 的 Water/CaveWater 均通过 `WaterVisualStyleBinding` 在激活和设置变化时替换共享材质，不重建地形、不改写水深 MPB。风格化材质显式启用 `FLATWORLD_WATER_STYLIZED` 本地关键字，两种正式材质都必须被 Prefab 引用，确保构建保留 Shader 变体；两种算法共用 `WaterSurfaceCommon.hlsl` 的水深、岸线与月光契约。
- 运行时动态创建、用于展示世界物品图标的 `SpriteRenderer` 不得依赖 `AddComponent` 默认材质；应复用 `RuntimeItemDefinition.Material` 的物品材质与外壳回退，确保提示表现和真实物品一致接收 Light2D。
- 草木风摆由 `WeatherMgr` 写入 `_GlobalWindStrength` Shader 全局参数；材质只保存自身基础幅度。Tilemap 使用单元锚点弯曲，底部 Pivot 的独立 Sprite 使用对象根部弯曲，且所有 URP 2D 活跃 Pass 必须复用同一顶点位移。
- 屏幕后处理依赖当前 `QualitySettings` 的 `customRenderPipeline`；不能只检查编辑器当前质量档位，所有可选档位都必须引用项目内实际存在的 URP 资源，否则 Scene 视图可能可见而 Game/Android 画面不可见。
- 世界常态后处理由 `WorldManager/Global Volume` 引用 `Assets/9_Shaders/Volume/Global Volume Profile.asset`；`WorldManager` 跨场景常驻，因此 `WorldPostProcessQuality` 必须绑定同一 Prefab 内 `GameManager` 的进出世界事件，只在世界内启用 Volume，退出时释放克隆的 Profile 和所有 VolumeComponent。调色保持在该资产内，画质只调整泛光采样；状态警示继续由优先级 100 的 `ScreenPostProcessManager` 独立合成。URP 14 的 `Volume.profile` 自动克隆所有子组件，但 `Volume` 本身不负责销毁克隆，必须由持有者清理。
- 屏幕后处理脚本按最低支持质量只实现一个档位标记接口：Low 可在所有档位运行，Medium 需中/高档，High 仅高档；未标记效果保持旧行为。
- 运行时 Sprite 描边若复制 `SortingGroup` 内的渲染器，描边 Renderer 必须放到组外并排在主体之后；URP 2D 自定义 Sprite Shader 必须包含 `Core2D.hlsl`，同时保留 SpriteRenderer 的逐渲染器属性。
- 描边等代理 `SpriteRenderer` 必须同步源 Renderer 的 MPB 局部裁剪参数；代理写入自有 MPB 时必须同时写回 Sprite 的 `_MainTex`，避免逐渲染器贴图被默认白图替换。`Universal2D`、`NormalsRendering`、`UniversalForward` 等实际参与的 Shader Pass 必须使用同一坐标与阈值，避免代理或回退 Pass 重新显示已剔除像素。
- 角色水体效果覆盖会旋转的手持物等附属 Sprite 时，水面高度与波浪横轴必须使用角色统一的世界空间坐标；保留本地坐标模式只用于不旋转的旧材质兼容，避免水线随物品旋转成竖线。
- 浅滩最低淹没高度属于 `WaterImmersionRenderEffect` 的纯视觉映射，应在 `depthToSurface` 曲线结果后叠加身体归一化偏移；禁止改写 `TileData_Water.deepValue`，该值还会参与水中移速等玩法结算。
- `TileEffectReceiver` 的邻接水格容错只服务于水边交互；`Tile_Water` 必须根据 `IsActiveTileEdgeInteractionOnly` 阻断浸没视觉、脚底阴影、Buff 和移动速度效果，避免站在沙格边缘的角色被误判为入水。
- `TileEffectReceiver` 在地块来源变化时会于同一帧依次调用旧地块 `OnExit` 和新地块 `OnEnter`；一次性入水效果必须由角色侧保存真实浸水状态，并合并连续水格之间的同帧切换，不能把每个水格都当成重新入水。
- 雪地脚印等带历史轨迹的地表表现不能在 Tile `OnExit` 时清空历史；跨相邻同类地块同样会先 Exit 再 Enter，应只停止新轨迹采样，让已有轨迹继续按自身寿命逐步淘汰。`SnowFootprintTrail` 当前由 `Tile_Snow` 运行时 `AddComponent`，默认表现资源不能只依赖 Prefab/Inspector 预先赋值，必须保证动态创建后也能解析到专用 Shader/材质配置。
- `Assets/2_Prefabs/Gameplay/Modules/Rendering/Shadow.prefab` 是 URP `ShadowCaster2D` 投影组件，不是实体脚底贴图；实体可视阴影应复用 `ActorShadowManager` 的独立注册和水体显隐入口。
- `Presentation/Effects/Runtime/` 受独立 `Effect.asmdef` 隔离，不能反向引用主 `GamePlay` 程序集中的 `VisualEffectManager`；需要名称池管理器的角色表现控制器应放在 `Presentation/` 主程序集，或先抽取无环依赖的公共契约。
- Editor 脚本留在 Editor 程序集/目录；生产程序集不得反向引用 `FlatWorld.Gameplay.Debug`。
- 运行时世界由 `SceneManager.CreateScene` 动态创建，不会触发 `SceneManager.sceneLoaded`；监听运行时 Hierarchy 的 Editor 工具必须同时处理旧场景卸载与后续 `hierarchyChanged`，且不能用无界切换标记长期屏蔽用户操作。`hierarchyChanged` 热路径必须从少量已保存记录定向解析对象，禁止组合 `Resources.FindObjectsOfTypeAll` 与 `GlobalObjectId.GetGlobalObjectIdSlow` 全场景扫描，否则跨场景引用会制造警告并造成 `EditorLoop` 尖峰；调用 `GlobalObjectIdentifierToObjectSlow` 前必须确认 ID 所属场景已加载，场景切换空窗直接跳过，否则 Unity 原生层会触发 `manager != NULL` 断言。
- 内容工坊保持在 `Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`，只把可验证的差异写回 JSON，不在运行时程序集引入编辑器依赖。
- 业务日志用 `GameLogManager` 的 `[WORK]` 接口；不要制造每帧重复警告。
- `GMReflectionConsole` 独占 F4 作为 GM 调试面板开关；管理员手持物品加量由面板按钮调用，`GameDebugManager` 的晴天快捷键必须在脚本默认值与 `WorldManager.prefab` 序列化值中都使用 F6，禁止运行时反射改键。
- GM 世界观察层属于 `Development/Debug/GMWorldLayerOverlay`，温度与污染共用一张低分辨率点采样纹理并互斥切换；纹理必须与整数世界格对齐，采样按相机视口和每帧预算分批完成后统一上传，换世界时丢弃旧批次，未加载格透明。污染总览读取全部已注册污染定义（含 MOD）的最高归一化负荷；禁止每格创建 Renderer、为可视化租住区块或向地形回写颜色。Shader 用 Resources 引用保证构建保留，关闭观察层停止采样。

- 季节覆雪通过统一 MPB 效果模块的 `_SnowCoverage` 通道叠加；实际物品基础材质必须支持该属性，仅给默认 Sprite 材质设置 MPB 不会显示雪。保持风、水、受击和溶解等既有通道。
- `ChunkSnowCoverRenderer`、`ChunkSupportSurfaceRenderer` 只读权威状态并绘制专用 Tilemap，卸载仅清理自身图层；禁止为了融雪或解绑改写原始地形。`ChunkSupportSurfaceRenderer` 使用 Tile Color RGBA 编码左、右、下、上四个外露平台边缘，并读取相邻 Chunk 的 `TerrainSupportLayer`；相连支撑面之间对应通道必须为 0，只允许整体外围产生接触阴影。角色和动物不装配静止物件雪效。

## 验证

- 自动断言对象、材质、池化生命周期和关键参数；最终粒子/Shader 观感才做定向视觉检查。
- 触发属于战斗、天气、UI 或音频时加载对应领域 Skill。
- 默认不主动跑测试；需要时运行 `EffectsTools.Smoke`。入口：`Assets/GameTest/EffectsTools/EffectsToolsSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
