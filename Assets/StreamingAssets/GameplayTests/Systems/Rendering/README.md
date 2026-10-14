# 渲染、光照与特效

快速入口：`rules.lua`；操作：`rendering/fog_config、universe_view_config、group_projection`。

已编写的规则覆盖：正式 WorldRenderingConfig 的雾半径/噪声/颜色约束和宇宙视野层次/缩放/过渡约束；停用独立 SpriteRenderer/SortingGroup 的外部代理排序键投影，以及分组停用后的逐级回退。

关键流程待补：真实 URP/BRG/Shader 画面、排序遮挡、阴影和水面、GPU后端兼容、相机切换以及特效池复用。合法配置不等于渲染正确。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。
