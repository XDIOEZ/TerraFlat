# 渲染、光照与特效

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

快速入口：`rules.lua`；操作：`rendering/fog_config、zoom_cloud_mask、universe_view_config、universe_view_transition、group_projection`。

2026-10-17 阴影性能补充但未执行：`rendering/building_capability_cache` 检查正式能力查询的空结果、模块增删/重加失效、首个匹配顺序、实时占地/通行属性和预热后重复查询的托管分配；`rendering/shadow_proxy_changes` 检查正式太阳阴影代理的静态复用、透明度/脚点变化、重复镜像、绘制模式切换和归池失效。只创建独立禁用物品与渲染节点，不代表阴影画面、光照变化、相机裁剪或最终帧耗时已验收。

2026-10-17 新增但未执行：`universe_view_transition` 直接采正式手动缩放约束，覆盖过渡终点封顶、滚轮与大步输入、连续拉远不积攒、完成后新输入放行、过渡中拉近、同帧折返、已有远景、零时长及配置终点变化。本轮仅静态修改，未编译、未运行；真实滚轮、手机双指/滑条及最终淡入画面由用户验收。

已编写的规则覆盖：正式 WorldRenderingConfig 的雾半径/噪声/颜色约束和宇宙视野层次/缩放/过渡约束；停用独立 SpriteRenderer/SortingGroup 的外部代理排序键投影，以及分组停用后的逐级回退。

2026-10-17 天气雾补充但未执行：`rendering/fog_config` 直接采正式 `RenderingDenseFog.TryResolveRadii`，检查满强度沿用配置半径、减弱时清晰圆按强度反比扩大、柔边宽度固定、超上限钳制及零/负强度关闭。Shader 圈外始终完全遮挡，强度不再参与透明度；用户自行验收，本轮未编译、未运行规则或画面测试。

2026-10-17 补充但未执行：高视野云层通过正式 Mod_ChunkLoader 改加载范围，再共用 ChunkMgr 窗口几何与 Shader 参数提交，检查方形清晰窗覆盖 (2d-1) 个活动区块、额外表现圈作为柔雾带、无额外圈时内收一格及极小窗；覆盖范围扩大/缩小、最小值、正负坐标、区块内移动与跨块、非正方形区块。不同正交尺寸、横竖屏及投影翻转只改实际投影，保留世界云纹尺寸；宇宙画面仍在 60～90 直接淡入。此轮仅静态修改，未编译、未运行；实际窗口提交时序、环绕世界接缝、旧联机回退、异步加载中的画面及 GPU 柔边仍由用户验收。

PlayMode 入口：`gameplay-suite.json` / `gameplay.lua`；操作 `rendering/player_external_effects`。

2026-10-17 新增但未执行：`rendering/combat_effect_prewarm` 采正式石斧引用的伤害数字/切割动画，检查完整资源阶段已准备三个独立空闲实例、重复预热不扩容、并发取出复用池、回收及重复回收幂等、不改变游戏随机数。此轮保留冻结 PlayMode，仅静态修改；该断言不代表首击耗时、全部 Mono.JIT 或画面已验收。

真实流程覆盖：ItemMgr 加载正式石斧并接入真实世界排序，实际已入世玩家的 ActorRenderEffectController 注册外部 Renderer，等待正式跨帧 LateUpdate 提交 MPB。检查正式分类排序键、重复绑定单次登记、复用玩家共享材质、Sprite 身体范围提交并保留外部原有属性，随后解除并核对原材质与完整 MPB 恢复。正式 WetStatusEffect Prefab 经真实 VisualEffectManager 播放、Owner 非叠加复用、真实粒子材质、解绑/回池停粒子清空、缩放恢复和重复回收幂等；结束、失败和中止清理自有物品与粒子。操作提交前已清理，内部 MPB/池状态没有保留现场，因此本组 0 张截图。

世界排序单独采真实散落石斧的同帧状态并立即正式回收；跨帧 MPB 使用 InstantiateHeldItem 创建独立挂点的自有石斧，核对物品/Renderer 实例与代际连续、最后销毁挂点，不占用玩家现有手持物。

关键流程待补：最终 URP/BRG/Shader 画面、Y 轴视觉遮挡、阴影和水面、全部特效池类型及延迟回收、GPU 后端兼容、相机切换、性能和图像对比。真实组件/共享材质/MPB/粒子池状态正确仍不代表画面与性能已验收。

2026-10-15 的通过记录仅覆盖当时已有断言；后续补充项按各自日期记录执行状态，实际画面和最终验收由用户执行。

2026-10-17 新增但未执行：`rendering/cave_shadow_spheres` 直接采样正式 `ChunkLightOccluderVisibilityScheduler.SpheresIntersect`，由 Lua 断言近光/远光、相切、保守边界余量、Z 轴相交与分离、零半径和对称性。本组不创建渲染器、不调用渲染调度，不代表 URP 灯注册表、逐灯阴影启停、墙体漏光或最终帧率已验收。

2026-10-17 船件热查询补充但未执行：`rendering/ship_part_placement_state` 装配禁用独立 Item 的真实建筑/机械/船件模块，Lua 检查活建筑状态优先于旧持久数据、运行身份切换即时生效、缺活模块的持久状态回退、机械模块移除和地板短路，并采样预热后 256 轮正式通行查询的线程分配。保留用户暂停现场，本轮未编译、未运行；未覆盖建筑行网格最终画面、实时 MPB 显示或改善后的帧率。
