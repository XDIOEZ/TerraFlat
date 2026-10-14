# 渲染、光照与特效

快速入口：`rules.lua`；操作：`rendering/fog_config、universe_view_config、group_projection`。

已编写的规则覆盖：正式 WorldRenderingConfig 的雾半径/噪声/颜色约束和宇宙视野层次/缩放/过渡约束；停用独立 SpriteRenderer/SortingGroup 的外部代理排序键投影，以及分组停用后的逐级回退。

PlayMode 入口：`gameplay-suite.json` / `gameplay.lua`；操作 `rendering/player_external_effects`。

真实流程覆盖：ItemMgr 加载正式石斧并接入真实世界排序，实际已入世玩家的 ActorRenderEffectController 注册外部 Renderer，等待正式跨帧 LateUpdate 提交 MPB。检查正式分类排序键、重复绑定单次登记、复用玩家共享材质、Sprite 身体范围提交并保留外部原有属性，随后解除并核对原材质与完整 MPB 恢复。正式 WetStatusEffect Prefab 经真实 VisualEffectManager 播放、Owner 非叠加复用、真实粒子材质、解绑/回池停粒子清空、缩放恢复和重复回收幂等；结束、失败和中止清理自有物品与粒子。操作提交前已清理，内部 MPB/池状态没有保留现场，因此本组 0 张截图。

关键流程待补：最终 URP/BRG/Shader 画面、Y 轴视觉遮挡、阴影和水面、全部特效池类型及延迟回收、GPU 后端兼容、相机切换、性能和图像对比。真实组件/共享材质/MPB/粒子池状态正确仍不代表画面与性能已验收。

本轮仅静态编写，未编译、未运行；实际画面、表现和最终验收由用户执行。
