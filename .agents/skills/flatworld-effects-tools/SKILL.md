---
name: flatworld-effects-tools
description: "Use when: 定位或修改 FlatWorld 的运行时特效、粒子、伤害文字、水体效果、Shader、视觉管理器、项目编辑器工具、结构编辑器、调试脚本或测试辅助。关键词：VisualEffectManager、SpecialEffects、Shader、Editor、GameDebugManager。"
---

# FlatWorld 特效、Shader 与工具

## 入口

- 运行时视觉：`Assets/5_Scripts/5-3_GamePlay/Presentation/Effects/Management/VisualEffectManager.cs`、`Assets/5_Scripts/5-3_GamePlay/Presentation/Effects/Runtime/`
- 角色渲染：`Assets/5_Scripts/5-3_GamePlay/Presentation/{ActorRenderEffectController,ActorRenderColorEffect,WaterImmersionRenderEffect}.cs`
- 实体脚底阴影：`Assets/5_Scripts/5-3_GamePlay/Presentation/ActorShadowManager.cs` 与 `Assets/2_Prefabs/Gameplay/Modules/Rendering/ActorShadow.prefab`；阴影使用场景级 `ActorShadows` 根节点和 `Shadow` Sorting Layer，不挂到实体或 `RuntimeEntities` 下。
- 编辑器工具：`Assets/Editor/FlatWorld/`、`Assets/Editor/FlatWorld/ProjectTools/`；内容工坊入口为菜单 `FlatWorld/内容配置/内容工坊`
- 调试：`Assets/5_Scripts/5-3_GamePlay/Development/Debug/`、`Development/Diagnostics/{GameDebugManager,GameLogManager}.cs`

## 不变量

- 先确认触发系统及 Prefab/材质/Shader 的真实引用来源，再改表现。
- 池化特效每次取出时重置 Transform、Animator、颜色和生命周期；回收/禁用时清理订阅与状态。
- 角色颜色等共享 Shader 参数通过现有 MPB 控制器提交，避免多个组件互相覆盖。
- Unity 2D 使用 URP/Light2D；修改 Shader 前核对材质实际 Shader 与 Pass。
- 运行时 Sprite 描边若复制 `SortingGroup` 内的渲染器，描边 Renderer 必须放到组外并排在主体之后；URP 2D 自定义 Sprite Shader 必须包含 `Core2D.hlsl`，同时保留 SpriteRenderer 的逐渲染器属性。
- 角色水体效果覆盖会旋转的手持物等附属 Sprite 时，水面高度与波浪横轴必须使用角色统一的世界空间坐标；保留本地坐标模式只用于不旋转的旧材质兼容，避免水线随物品旋转成竖线。
- `Assets/2_Prefabs/Gameplay/Modules/Rendering/Shadow.prefab` 是 URP `ShadowCaster2D` 投影组件，不是实体脚底贴图；实体可视阴影应复用 `ActorShadowManager` 的独立注册和水体显隐入口。
- Editor 脚本留在 Editor 程序集/目录；生产程序集不得反向引用 `FlatWorld.Gameplay.Debug`。
- 内容工坊保持在 `Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`，只把可验证的差异写回 JSON，不在运行时程序集引入编辑器依赖。
- 业务日志用 `GameLogManager` 的 `[WORK]` 接口；不要制造每帧重复警告。

## 验证

- 自动断言对象、材质、池化生命周期和关键参数；最终粒子/Shader 观感才做定向视觉检查。
- 触发属于战斗、天气、UI 或音频时加载对应领域 Skill。
- 默认不主动跑测试；需要时运行 `EffectsTools.Smoke`。入口：`Assets/GameTest/EffectsTools/EffectsToolsSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
