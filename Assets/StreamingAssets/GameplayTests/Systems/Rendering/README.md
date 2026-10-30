# 渲染、光照与特效

2026-10-30 区块火场资源断言已加入 `rendering/combustion_visual`：检查 ChunkView 的 Unlit 源材质绑定、八帧贴图与 FullRect 导入。以下新增采样仍未运行。

`rendering/world_fire_batches` 使用 ChunkView 的正式 Sprite/材质，实际创建100×100个 fire domain 四边形，按16×16格构造49个 `ChunkDepthMeshRenderer(useParentSortingAnchor:true)`，显式 Flush 后读取每个 Mesh 的 submesh 顶点/索引、真实 MeshRenderer/SortingGroup、排序键和 `SetIndexBufferData` / `SetVertexBufferData` 调用点的 Editor 计数/字节。相同火格重提应无上传；单火颜色变化应只增加顶点上传，不增加索引上传或拓扑重建。该同步规则只验证 Unity Mesh 构造和 CPU 上传路径；实际相机帧数据由 `rendering/world_fire_render_benchmark` 另行采集。

`rendering/world_fire_render_benchmark` 是显式 PlayMode Lua 操作：建临时 Scene、1920×1080 RenderTexture 与只看专用 Layer 的正交相机，使用相同49个正式深度合批 Renderer 提交10,000个 Sprite Quad；采样前暂时关闭其他已启用 Camera，取12帧预热和60帧实渲染，之后立即恢复相机。结果包含摄像机是否完整覆盖火格与真实 Mesh bounds、预热与正式采样期 Camera 实际渲染帧数、Profiler `Render/Draw Calls Count`、`Memory/GC Allocated In Frame`、FrameTimingManager 的 CPU 主线程/渲染线程/整帧与 GPU 时间，以及真实 `SetIndexBufferData` / `SetVertexBufferData` 的 Editor 计数和字节增量。采样完成后单独从 RT 读回 PNG 到 `Library/FlatWorldGameplayTests/WorldFireRenderBenchmark/`，JSON 的 `previewImagePath` 给出绝对路径；Lua 宿主结束截图仍是普通游戏现场。Lua只检查 PNG 文件生成，不做非黑像素或火焰着色断言，需人工查看这张基准PNG。Profiler 项不可用时标记 `unavailable`；GPU 样本为0时按不可用记录，不会伪报为通过。FrameTimingManager 是整帧计时，Draw Calls/GC 是隔离采样期间的整帧计数，并非每 Camera 的GPU计时；离屏 RT 测量不等于真实显示器刷新率或 Frame Debugger 验收。此夹具只在被 Lua 明确调用时建场景、启相机和 Recorder，无常驻玩法开销。本轮只添加采样入口，没有启动 Unity 或执行该规则。

同一规则还提交正式16×16 `ChunkRuntime` 地形并写入真实火格，让 `DroppedItemPresentation.Present` 依次采样同格抑制、移动到无火格恢复、原格灭火后恢复；测试使用独立 owned `WorldRuntime` 和临时 Scene，并在清理阶段通过 `ReleaseOwnedWorldModel` 释放。尚未覆盖 `ItemMgr.Matter` 的散落物表现入口。

## 冷机器与提前显示回归（2026-10-30）

`rendering/cold_machine_content` 克隆全部正式机器定义，以配置未解析、逻辑未创建的节点调用正式落地表现预备，再读取所有声明模块；Lua 验证炉体、晾架、培育仓、燃烧特效和照明类别均实际覆盖，光源不遗漏且保持停用，读取不激活业务、不改写节点快照或共享定义。另检查资源缺失的明确错误、失败不发布缓存、同对象重试、序列化排除缓存及失效重建。该采样不代替实际区块分帧绑定、音频播放或画面验收。离线入口 `Tools/Tests/ColdMachinePresentation/verify.py` 直接编译正式入口/绑定代码，使用 Unity 与资源边界替身验证事务，26 项断言通过；完整外部 C# 编译 0 错误、43 警告。

`20261029T171734686Z_8e4290e2` 的统一渲染规则通过，`cold_projection_initialization` 的停用父节点首次同步、激活后材质参数保持与释放恢复共2项断言通过。掉落物新增显式资源上下文和空输入拒绝，编辑态物品夹具使用 ExecuteAlways 接收真实销毁回调。扩大回归仍有 AIECS 测试场景无效、机器快照数值、烟口配置和旧渲染 Lua 超256步的失败，不计为通过。`20261029T172007649Z_c8b90059` 的场景测试启动遇到未保存场景提示，已取消并保留现场，未执行农业/全目录机器现场 Lua，未产生玩法截图。

## 既有覆盖

物品与生物静态排序由 `visual.sortingLayerName` 和 `sortingOrder` 决定，同层同序才按 Y 排序；普通物品和生物使用 `Player/100`，船板与铁轨使用 `Default/0` 和贴地材质。水生表现入水时临时使用水下类别，携带或离水后恢复定义排序。`rendering_item_sorting_configuration` 覆盖正式配置、分组、上下位置、原位更新、手持/显示切换、池复用、轻量掉落物、合批和水生切换；实际 GPU 画面需现场验收。

2026-10-29 `rendering/contact_shadow_culling_indices` 直接调用生产索引收集，覆盖全可见、部分可见、相机变化、全不可见和 96 个实例；本轮未执行，真实 BRG 输出、Append 扩容与 GPU 画面仍待验收。

2026-10-29 `item-sorting-configuration.lua` 新增 `presentation_sorting_domain`：域内正式 Flush 按原始图层/Order、材质队列、批次序和显示脚点 Y 写入组件临时 Order；探针读取实际 SortingGroup/Renderer 序号检查 Y 交换、跨域独立 rank、固定地板与角色顺序、顶层组原子性，以及移除、投影释放和域销毁时的 raw 键恢复。`depthRows` 另以同源行内不同 X 实体验证 90° 投影后拆组 rank、离域恢复行合批和部件顺序。仅静态补齐，本轮未执行 Lua 或 Unity。

2026-10-28 本轮专项规则 28 项断言通过；`20261028T151214478Z_15c3e9a0` 的 29 项 EditMode 和真实渲染案例通过，两种船板跨帧保留 `Default/0`、未进动态合批，结束截图已复核角色完整显示在船板前方，控制台无错误和警告。扩大规则回归仍在暂停刚体与 `destroyed` 注册清理断言失败，未记为本轮通过项。

2026-10-28 默认写实水面改为灰青水色、四组共用波光和断续岸沫，河流降低整片反射；`water_surface_palette` 已加入 `unified-manager.lua`。本轮单独执行原 Lua 水面段，70 项断言通过；三材质的两个正式 Pass 编译及湖泊/河流/海岸离屏 GPU 预览通过。完整 `rendering_unified_manager` 在既有 `destroyed` 注销清理断言失败，未覆盖真实游玩水岸、夜景及 Android 后端。

2026-10-28 阴影索引整合：`shadow_quad_indices` 读取正式初始化后Mesh的首尾索引，Lua检查1/2/4096个Quad的容量、绕序与16位格式，放在独立unified-manager案例；先补断言后改生产，未运行。真实两类阴影提交和GPU画面仍待验收。

2026-10-28 天气画面上下文整合：`rendering/weather_screen_context` 使用独立玩家、相机和真实相机栈采样正式查询，Lua 检查玩家资格、相机替换和最终栈筛选；断言放在可单独选择的 `rendering_unified_manager` 案例，避免既有 `rendering_rules` 超过 256 步而在预检失败。本轮只写入源码，未编译或运行；天气所属世界切换、维度禁用、各效果清理时机和最终画面仍待现场验收。

2026-10-27 统一渲染入口：`rendering_unified_manager` 独立案例通过正式 `WorldRenderingManager` 采样排序分发、重复注册、阴影开关与水态传递、结构刷新、手动/多来源交互描边独立、注销/回池/复用/销毁清理。真实 `player_external_effects` 改经统一入口调用排序与特效，增加同 Owner 同名两次 Stackable 效果注销全回收、重租、未显式注册物品直接高亮/特效后正式 Despawn 清理；`combat_effect_prewarm` 同样使用统一池入口。首轮规则在共享编译刷新期间中断，新操作当时尚未加载；随后按用户最新要求暂停主动编译和测试，新增规则与现场验收均未完成，不记为通过。这些组件/池状态断言不覆盖最终 GPU 画面和性能。旧 `rendering_rules` 的超过256步编排问题见下方历史记录，新增案例单独执行以免被其预检阻塞。

2026-10-26 高炉 Shader 烟雾：`rendering/combustion_shader_smoke` 采样正式高炉参数、顶部炉口锚点、共享 Quad/材质、错相、裁剪边界、排序、独立向上和燃烧/停用/恢复开关；仅高炉启用。独立 C# 编译与当前 D3D Shader 编译、离屏预览通过；保留用户 PlayMode，未执行 Lua 或实际高炉点火画面验收。

2026-10-26 熔炉火焰定位：正式 Smelter JSON 的 Y 偏移调整为 -0.375，火焰底部贴齐炉口内底面（16×16、16PPU中心原点贴图的底部Y=2像素）。现有火焰检查移至独立 `combustion-visual.lua`，案例 `rendering_combustion_visual` 的17项资源、朝向、生命周期与正式配置断言已通过，绕开旧渲染规则的步骤上限。Unity 正式组件的隔离预览已核对，控制台无错误或警告；未进入 PlayMode，未验收真实机器后端现场。

2026-10-26 建筑水面覆盖：直接放进有液体、无平台的格时，设备、管线与普通建筑进入海床后/水面前的行网格；有平台则沿用水上排序。`rendering/building_liquid_surface` 覆盖正式液体/支撑数据的浅水、平台增删、相邻平台、批量排水/涨水通知及队列范围；尚不覆盖真实机器换批次、GO启停及最终GPU画面。快速建造规则 `building_rules` 已通过；旧 `rendering_rules` 因既有256组掩码循环导致编排超过256步，尚未执行断言。本次水面断言已独立为 `rendering_building_liquid_surface`，专用规则已通过；最终GPU画面仍待验收。

2026-10-25 风条纹：`WindStreakRendererFeature/WindStreaks` 消费本地天气已平滑的风向与风力，单次 GPU 合成稀疏弧形细线，风越大越密、越快、越明显；相机移动和环绕使用连续相位，停风/换世界清理，无风不绘制。`rendering/wind_streaks` 补充风力阈值、四向/斜向、强度上限、暂停、镜头位移、非法输入与重置断言。Data/GamePlay 隔离静态编译及 JSON、资源引用和 Feature 映射检查通过；未刷新运行中的 Editor，未执行 Lua 或画面验收。

2026-10-25 贴地阴影重叠：默认脚点向主体内部重叠高度的25%、最多0.25格，避免小物品透明边距让阴影与底部脱开；显式零重叠配置保留。`shadow_footprint_matrix` 补充普通/缩小的带留白物品贴合与显式脚点断言。本轮保持用户 PlayMode，仅静态修改与检查，未运行 Unity 编译、Lua 或最终画面验收。

2026-10-24 火把朝向与像素火焰：手持定义顺时针校正 90° 并同步灯光挂点，手持/放置火把的火焰均保持世界向上；共享 Shader 改为按世界尺寸计算的 32 PPU 方形像素、四色硬边火舌、单像素火星和 12 FPS 的 GPU 动画。`rendering/dropped_combustion_material` 补充默认像素密度/帧率、跟随倾斜、直立与左右翻面方向断言。用户保留 PlayMode 且自行验收，本轮仅静态修改，未编译或执行 Lua；实际鼠标跟随、动态挂点、像素画面与 Android GPU 仍需现场验收。

2026-10-24 物件雪线保持水平：`Sprite-Lit-Master` 的光照/回退 Pass 与 `ChunkDepthSpriteLit` 共用 `SpriteSnowCommon.hlsl`，按世界 Y 和单个精灵的仿射包围盒合成雪顶，旋转、翻面、非等比缩放与机械动画不再倾斜雪线。`sprite_snow_bounds` 新增正式控制器 MPB 的偏心原点、连续翻面、换图、旋转/缩放下原图尺寸和静态免重传断言；该规则只覆盖元数据提交，不覆盖 GPU 最终画面、行网格顶点上传或机械动画。本轮保留用户 PlayMode，只做静态修改，未运行 Unity 编译、Lua 或画面验收。

2026-10-24 世界方块太阳投影柔化补齐：机械投影曾把 `_SunShadowTexelSize.z` 写成 0，Shader 回退为 1，导致化学引擎等高 PPU 贴图的阴影近似硬边。`ShadowProxy.UpdateProperties` 现在从共享阴影网格自行读取倍率，不再要求普通 Item 与机械调用方传入，保持主体 Point 过滤、外沿包围盒与静态免重传。`shadow_proxy_blur_scale` 增加 PPU 8/16/32/64/1254 的实际 MPB、图集步长、网格外沿、换帧与归池断言；既有 `shadow_proxy_changes` 同步新接口。静态审查确认 BRG 自然物投影已提交倍率、整船地面占地投影已读取全局柔化，未改其表现。实际生产代理、完整共享网格缓存与两个规则采样方法通过独立 Roslyn/Unity 引用编译，补丁检查通过；保留用户 PlayMode，未主动刷新或编译运行中的 Editor，未执行 Lua 或最终 GPU 画面验收。

2026-10-24 地形阴影转角补缝：正式 `TryGetShadowMasks` 在四向直边之外提交四向斜角，Shader 使用同宽同衰减的四分之一圆并取最大值合并；增量更新覆盖3×3邻域和八邻区订阅。`terrain_shadow_masks` 新增全部256种邻格组合及四角的同高/较低/多层高差/墙脚/空格接收条件断言；`terrain_height_neighbours` 扩为八方向加载、采挖、卸载、重载的实际边角掩码采样；`terrain_shadow_dirty_queue` 增加15组局部、跨边、跨角和无关远格的精确更新范围与去重断言。本轮保留用户 PlayMode，未主动刷新或重编译运行中的 Editor，未执行 Lua 与最终 GPU 画面验收；这些规则也不等同于真实 BRG 提交、订阅回调和环绕接缝的视觉验收。

本轮静态校验：补丁格式检查通过；复用现有 Bee 清单进行隔离 C# 编译，被工作区其他功能的新类型/旧引用阻塞（包括 `IHeldItemPositionControl`、`ShaderFlameEffect`、`CreatureHitChanceRules`、`UIMultiSelectDropdown`、`SpaceElevatorState`），不记为编译通过。实际 URP 14.0.12 头文件下普通顶点/片元的独立 D3D 编译通过；DOTS 组合受独立旧预处理器处理 Unity 宏失败阻塞，不能据此报告 BRG Shader 或 Unity 导入通过。诊断留在 `Temp/TerrainShadowCorners/`，不进入提交。

2026-10-23 常驻燃烧改为可复用 Shader 火焰：`rendering/dropped_combustion_material` 已扩充为正式工厂与适配器的共享材质/四顶点网格、零常驻粒子、独立相位、尺寸与 CPU 包围盒、显式排序、点燃/熄灭、组件启停、回池后保持熄灭、零火势及非有限配置保护断言。入口为 `Assets/2_Prefabs/Effects/VFX_ShaderFlame.prefab`，普通/轻量掉落物与燃烧设施共用；入水烟汽仍是独立一次性粒子。用户处于 PlayMode，本轮不刷新、不编译运行中的 Unity、不执行 Lua；四个核心/采样 C# 文件通过独立 Roslyn 编译，实际 URP 14.0.12 头文件下的顶点/片元及实例化四个 HLSL 编译组合通过 Windows D3D 编译；不替代 Unity 导入与上述断言执行。尚未验证真实 GPU 画面、所有缩放/挥动姿态、Android 后端和帧率收益。

2026-10-22 人物分层穿戴补充但未执行：`rendering/character_layers` 直接调用正式 `CharacterAppearanceCompositor.Compose`，由 Lua 断言同一份半包围头盔叠加不同肤色底图时保留自己的脸、透明开口保留底图、装备可超出身体轮廓、头发整体/局部遮挡、身体遮罩与头发独立、缺穿戴图不应用遮罩、摘下恢复原发，以及 Alpha 叠加、遮罩叠乘、装备前后顺序、最终统一镜像、尺寸拒绝和输入不变。采样只使用独立小像素数组，不加载正式人物、装备资产或世界；未覆盖资源实际分层质量、正式槽位通知、换装后的动画/阴影/特效及最终 GPU 画面。本轮只做静态修改，未主动控制 Unity 或运行 Lua，最终画面由用户验收。

2026-10-20 接触阴影 BRG 稳定帧上传优化：ContactShadowBatchRenderer 每帧仍按 ActorShadowManager 原顺序收集/裁剪全部 Item 与机械阴影，逐槽精确比较位置、大小和透明度，数量或数据不变时不重复调用 GPU GraphicsBuffer.SetData；扩容重建 GPU 缓冲或前帧收集未完成时保证完整上传。空帧保持原有 count=0 隐藏，能在保留有效缓冲时复用旧数据。尚未添加 Unity GPU 现场用例，本轮未执行编译、PlayMode 和 Profiler 前后对比；需验证动态角色、静态植物、排序批次、昼夜光照及空帧恢复。

2026-10-20 点光源稳定态性能优化：Mod_LightSource 对已在 XY 平面的 Point Light2D 用四元数快速判断，避免每个常态灯逐帧将世界旋转转为欧拉角；倾斜时仍按原 X/Y 0.01° 门槛修正并保留世界 Z。未配置自发光且尚无覆盖节点时跳过逐帧 Sprite 同步。现有 Lua 未专测手持灯倾斜、旋转父层与动态自发光切换；本轮未运行完整 Unity 编译或 PlayMode。

2026-10-20 延迟特效回收队列优化：VisualEffectManager.Update 不再每帧对有效未到期 PendingReturn 执行 RemoveAt+Add（会导致 List 元素搬移）；到期、失活和对象销毁仍沿原条件回收/剔除，提前归池仍注销旧任务。现有 Lua 覆盖正常特效池与 Owner 清理，尚未专测大量混合到期时间、同帧失活与重新租用；本轮未运行完整 Unity 编译或 PlayMode。

2026-10-20 投射物飞行拖尾优化：ProjectileFlightTrailPresenter 每帧仍同步源排序，但只在 Layer/Sorting 值变化时写入；重复发射沿用已配置的颜色渐变，改变 TrailColor 或重建 TrailRenderer 后重新提交。保留实际飞行判定、尾部锚点、发射/回收及 TrailRenderer 清理语义。现有 Lua 未覆盖频繁发射、动态排序、渐变变化和最终拖尾画面；本轮未运行完整 Unity 编译、PlayMode 或 FPS/GC 前后对比。

2026-10-20 交互描边稳定态优化：InteractionTargetOutline 对相同 Sprite、颜色、排序、裁剪值跳过 Renderer/MPB 重复提交，代理相对 Sprite 的变换仅创建时归零，源动画和材质裁剪仍逐帧采样。重新高亮或材质更换重新提交，独立描边不参与 ActorRenderEffectController 的 MPB 合成。当前 Lua 尚未覆盖描边目标变更、动态裁剪和最终白边画面；本轮只做静态检查，未运行 PlayMode 或 FPS 实测。

2026-10-20 燃烧粒子稳定态优化：CombustionVisualEffect 缓存火焰/火星/烟雾的 Renderer，排序层和顺序有变化才写回；火焰跟随挂点的位置、旋转与局部缩放也只在变化时提交。保留每帧检查外部排序与变换，不修改发射率或熄灭语义。现有规则覆盖掉落燃烧材质，但未覆盖这一路组件的动态排序、锚点和粒子画面；本轮未运行 Unity 编译和 PlayMode，需现场验收。

2026-10-20 积雪渲染优化：ChunkSnowCoverRenderer 对雪层原始数组先检查实际正值；没有自然雪、天气雪和玩家编辑雪且先前无雪的区块直接跳过全格液体/支撑面/雪厚计算。真实有雪时复用已采样的有效地表，Owner 重建后无可见雪不遍历网格提交。新增 `rendering/snow_sparse_refresh` 对照正式单格接口，覆盖零层、天然/天气/玩家雪、平台遮盖及无地面。本轮静态修改，未运行 Lua 或测量最终 FPS，真实视觉由用户验收。

2026-10-20 天然水面齐平补充但未执行：`terrain_height` 检查地表/洞穴浅深底床交界双向无台阶、剩余水量 1/5/6/10 不影响水面高度、岸边只低一层，同时保留真实地势高差、抽干露底及平台遮挡。保留用户 PlayMode，未编译、未运行 Lua；最终河岸阴影和水面画面由用户验收。

`terrain_shadow_dirty_queue` 在独立规则中验证高低阴影的 128 轮稀疏脏格只处理 384 次（全表扫描原需 524,288 次），同时覆盖重复标记、重入、提交中断与解绑重置。该规则不测实际 BRG GPU 提交和画面。

2026-10-19 无大气加载边界补充但未执行：`rendering/zoom_cloud_mask` 检查真空及大气耗尽时切换星空、有气和极低非零气压保留云层、缺少大气状态时保守回退，以及共用材质从星空恢复云层；背景切换保留活动窗、完全遮挡边界和柔边宽度。星空与宇宙远景共用 `UniverseStarfieldCommon.hlsl`，无需额外相机或星点对象。保留用户 PlayMode，未编译、未运行 Lua；最终 GPU 画面由用户验收。

2026-10-18 天然水域底床补充但未执行：`terrain_height` 覆盖天然浅/深底床、水抽干、平地倒水向外投影、坑内倒水齐平/向内投影、底床继续采挖和填土、地下水床与平台；`terrain_height_save` 检查天然凹陷独立于玩家采挖快照恢复。保留用户 PlayMode，未编译、未运行，最终阴影和玩家打水/倒水由用户验收。

2026-10-18 掉落物燃烧材质补充但未执行：`rendering/dropped_combustion_material` 在独立禁用节点调用正式共用粒子工厂，检查非空共享材质、可用 URP 2D Shader 与 Circle 透明贴图。原燃烧材质保留 GUID 移入 Resources，轻量与普通世界掉落物统一显式绑定。保留用户 PlayMode，未编译、未运行 Lua；最终画面由用户验收。

2026-10-18 地表高低阴影改为固定表现深度：共享 Shader 将正整数层差统一视为有边，不再按高差削弱强度，所有高差使用相同材质深浅和宽度；同高无边、阴影落在较低侧的现有 `terrain_height` 规则保持。此次只做静态 Shader/材质修改，保留用户 PlayMode，未运行 Lua 或视觉验收；GPU 最终输出仍由用户验收。

2026-10-17 地表高低阴影补充但未执行：`rendering/terrain_height` 采样正式 `TerrainSurfaceHeightLayer`，覆盖地板齐平、同高异材质、逐层采挖、基岩最低深度、成片低地无内缝、高侧向低侧投影、填土、平台、水面、墙脚与洞穴基准。`terrain_height_save` 检查独立于石头产量的表面深度、MemoryPack 保存恢复、非法负深度及生成高度不变；`terrain_height_neighbours` 通过正式管理器查询邻区加载、采挖、卸载和重载。当前用户 PlayMode 保留，未编译、未运行；未覆盖最终 Shader 画面、BRG GPU 提交、环绕接缝视觉、真实采挖操作及性能，验收由用户执行。

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

快速入口：`rules.lua`；操作：`rendering/fog_config、zoom_cloud_mask、zoom_cloud_transition、universe_view_config、universe_view_transition、group_projection`。

2026-10-18 天气雾过渡补充但未执行：`rendering/fog_transition` 直接推进正式 `DenseFogPresentationTransition`，Lua 检查首次从画面外围聚拢、大小雾均用完整 30 真实秒起雾与散雾、柔边从 10 格加倍到 20 格、跨浓度区与起散反向从当前半径重新计时，以及重复目标、零/负时间、零时长、归零关闭和重置；屏外起点还采样正式四角计算，覆盖偏心镜头与宽屏，并检查加载首帧大时间、零时间视口改变和屏外弱雾接续。保留用户 PlayMode，本轮仅静态修改，未编译、未运行；位置天气仍由正式渲染器读取玩家所在位置，本规则不创建真实天气世界或执行 GPU 合成，移动采样、真实时间源与最终画面由用户验收。

2026-10-17 阴影性能补充但未执行：`rendering/building_capability_cache` 检查正式能力查询的空结果、模块增删/重加失效、首个匹配顺序、实时占地/通行属性和预热后重复查询的托管分配；`rendering/shadow_proxy_changes` 检查正式太阳阴影代理的静态复用、透明度/脚点变化、重复镜像、绘制模式切换和归池失效。只创建独立禁用物品与渲染节点，不代表阴影画面、光照变化、相机裁剪或最终帧耗时已验收。

2026-10-17 新增但未执行：`universe_view_transition` 直接采正式手动缩放约束，覆盖过渡终点封顶、滚轮与大步输入、连续拉远不积攒、完成后新输入放行、过渡中拉近、同帧折返、已有远景、零时长及配置终点变化。本轮仅静态修改，未编译、未运行；真实滚轮、手机双指/滑条及最终淡入画面由用户验收。

已编写的规则覆盖：正式 WorldRenderingConfig 的雾半径/噪声/颜色约束和宇宙视野层次/缩放/过渡约束；停用独立 SpriteRenderer/SortingGroup 的外部代理排序键投影，以及分组停用后的逐级回退。

2026-10-17 天气雾补充但未执行：`rendering/fog_config` 直接采正式 `RenderingDenseFog.TryResolveRadii`，检查满强度沿用配置半径、减弱时清晰圆按强度反比扩大、柔边宽度固定、超上限钳制及零/负强度关闭。Shader 圈外始终完全遮挡，强度不再参与透明度；用户自行验收，本轮未编译、未运行规则或画面测试。

2026-10-17 补充但未执行：高视野云层通过正式 Mod_ChunkLoader 改加载范围，再共用 ChunkMgr 窗口几何与 Shader 参数提交，检查方形清晰窗覆盖 (2d-1) 个活动区块、额外表现圈作为柔雾带、无额外圈时内收一格及极小窗；覆盖范围扩大/缩小、最小值、正负坐标、区块内移动与跨块、非正方形区块。不同正交尺寸、横竖屏及投影翻转只改实际投影，保留世界云纹尺寸；宇宙画面仍在 60～90 直接淡入。此轮仅静态修改，未编译、未运行；实际窗口提交时序、环绕世界接缝、旧联机回退、异步加载中的画面及 GPU 柔边仍由用户验收。

PlayMode 入口：`gameplay-suite.json` / `gameplay.lua`；操作 `rendering/player_external_effects`。

2026-10-17 雾窗交接补充但未执行：`rendering/zoom_cloud_transition` 直接采正式 `ChunkWindowPresentationTransition`，Lua 检查 0.8 秒缓入缓出、两侧同时移动、中途折返、连续跨块/斜向交接、窗口扩大缩小、长方形窗口、暂停/负时间/长帧、零时长/非法时长、不重叠传送与重置，以及旧区块仅在离开不透明外边界后释放的几何条件。ChunkMgr 持有同一表现窗口，暂留雾边内已有地面的 View 和租约，离开雾边后回收；正式加载目标不延迟。用户自行验收，本轮未编译、未运行 Lua；真实 View 回收/重入、异步生成、世界环绕、联机及最终 GPU 渐变画面未验收。

2026-10-17 新增但未执行：`rendering/combat_effect_prewarm` 采正式石斧引用的伤害数字/切割动画，检查完整资源阶段已准备三个独立空闲实例、重复预热不扩容、并发取出复用池、回收及重复回收幂等、不改变游戏随机数。此轮保留冻结 PlayMode，仅静态修改；该断言不代表首击耗时、全部 Mono.JIT 或画面已验收。

真实流程覆盖：ItemMgr 加载正式石斧并接入真实世界排序，实际已入世玩家的 ActorRenderEffectController 注册外部 Renderer，等待正式跨帧 LateUpdate 提交 MPB。检查正式分类排序键、重复绑定单次登记、复用玩家共享材质、Sprite 身体范围提交并保留外部原有属性，随后解除并核对原材质与完整 MPB 恢复。正式 WetStatusEffect Prefab 经真实 VisualEffectManager 播放、Owner 非叠加复用、真实粒子材质、解绑/回池停粒子清空、缩放恢复和重复回收幂等；结束、失败和中止清理自有物品与粒子。操作提交前已清理，内部 MPB/池状态没有保留现场，因此本组 0 张截图。

世界排序单独采真实散落石斧的同帧状态并立即正式回收；跨帧 MPB 使用 InstantiateHeldItem 创建独立挂点的自有石斧，核对物品/Renderer 实例与代际连续、最后销毁挂点，不占用玩家现有手持物。

关键流程待补：最终 URP/BRG/Shader 画面、Y 轴视觉遮挡、阴影和水面、全部特效池类型及延迟回收、GPU 后端兼容、相机切换、性能和图像对比。真实组件/共享材质/MPB/粒子池状态正确仍不代表画面与性能已验收。

2026-10-15 的通过记录仅覆盖当时已有断言；后续补充项按各自日期记录执行状态，实际画面和最终验收由用户执行。

2026-10-17 新增但未执行：`rendering/cave_shadow_spheres` 直接采样正式 `ChunkLightOccluderVisibilityScheduler.SpheresIntersect`，由 Lua 断言近光/远光、相切、保守边界余量、Z 轴相交与分离、零半径和对称性。本组不创建渲染器、不调用渲染调度，不代表 URP 灯注册表、逐灯阴影启停、墙体漏光或最终帧率已验收。

2026-10-17 船件热查询补充但未执行：`rendering/ship_part_placement_state` 装配禁用独立 Item 的真实建筑/机械/船件模块，Lua 检查活建筑状态优先于旧持久数据、运行身份切换即时生效、缺活模块的持久状态回退、机械模块移除和地板短路，并采样预热后 256 轮正式通行查询的线程分配。保留用户暂停现场，本轮未编译、未运行；未覆盖建筑行网格最终画面、实时 MPB 显示或改善后的帧率。

2026-10-18 新增但未执行：`rendering/cave_shadow_lazy_creation` 使用独立 4×2 权威区块和正式 `ChunkLightOccluderRenderer.Bind/RefreshShadowVisibility`，断言无灯时不创建阴影对象、首次受光创建真实 URP 网格、失光关闭与再次受光复用、解绑重绑后的墙位置/尺寸/镜像更新，以及未知光范围的保守回退。本组不改变正在冻结的现场；尚未验证最终墙体漏光和重新采样后的帧率，全受光新墙冷创建仍有开销。

2026-10-19 新增但未执行：`rendering/ship_block_visual` 采正式化学引擎目录机身尺寸和共用 `DoorSlideVisual`，覆盖双向滑动、中途反转、首次开门恢复、旋转缩放保留、排序/颜色同步与释放。2026-10-25 增强为 0/90/180/270 度各自采样门扇世界坐标，断言沿安装横轴对称滑动、原位关合、反转和开门状态重建，避免只检查局部 X 导致方向错误漏检。用户正在 PlayMode，本轮只静态修改，未编译或运行；实际门交互、通行、预览到落地的方向和最终拼缝由用户验收。
