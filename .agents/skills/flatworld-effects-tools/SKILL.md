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
- 运行时动态创建、用于展示世界物品图标的 `SpriteRenderer` 不得依赖 `AddComponent` 默认材质；应复用 `RuntimeItemDefinition.Material` 的物品材质与外壳回退，确保提示表现和真实物品一致接收 Light2D。
- 草木风摆由 `WeatherMgr` 写入 `_GlobalWindStrength` Shader 全局参数；材质只保存自身基础幅度。Tilemap 使用单元锚点弯曲，底部 Pivot 的独立 Sprite 使用对象根部弯曲，且所有 URP 2D 活跃 Pass 必须复用同一顶点位移。
- 屏幕后处理依赖当前 `QualitySettings` 的 `customRenderPipeline`；不能只检查编辑器当前质量档位，所有可选档位都必须引用项目内实际存在的 URP 资源，否则 Scene 视图可能可见而 Game/Android 画面不可见。
- 屏幕后处理脚本按最低支持质量只实现一个档位标记接口：Low 可在所有档位运行，Medium 需中/高档，High 仅高档；未标记效果保持旧行为。
- 运行时 Sprite 描边若复制 `SortingGroup` 内的渲染器，描边 Renderer 必须放到组外并排在主体之后；URP 2D 自定义 Sprite Shader 必须包含 `Core2D.hlsl`，同时保留 SpriteRenderer 的逐渲染器属性。
- 描边等代理 `SpriteRenderer` 必须同步源 Renderer 的 MPB 局部裁剪参数；代理写入自有 MPB 时必须同时写回 Sprite 的 `_MainTex`，避免逐渲染器贴图被默认白图替换。`Universal2D`、`NormalsRendering`、`UniversalForward` 等实际参与的 Shader Pass 必须使用同一坐标与阈值，避免代理或回退 Pass 重新显示已剔除像素。
- 角色水体效果覆盖会旋转的手持物等附属 Sprite 时，水面高度与波浪横轴必须使用角色统一的世界空间坐标；保留本地坐标模式只用于不旋转的旧材质兼容，避免水线随物品旋转成竖线。
- `TileEffectReceiver` 的邻接水格容错只服务于水边交互；`Tile_Water` 必须根据 `IsActiveTileEdgeInteractionOnly` 阻断浸没视觉、脚底阴影、Buff 和移动速度效果，避免站在沙格边缘的角色被误判为入水。
- `Assets/2_Prefabs/Gameplay/Modules/Rendering/Shadow.prefab` 是 URP `ShadowCaster2D` 投影组件，不是实体脚底贴图；实体可视阴影应复用 `ActorShadowManager` 的独立注册和水体显隐入口。
- Editor 脚本留在 Editor 程序集/目录；生产程序集不得反向引用 `FlatWorld.Gameplay.Debug`。
- 运行时世界由 `SceneManager.CreateScene` 动态创建，不会触发 `SceneManager.sceneLoaded`；监听运行时 Hierarchy 的 Editor 工具必须同时处理旧场景卸载与后续 `hierarchyChanged`，且不能用无界切换标记长期屏蔽用户操作。`hierarchyChanged` 热路径必须从少量已保存记录定向解析对象，禁止组合 `Resources.FindObjectsOfTypeAll` 与 `GlobalObjectId.GetGlobalObjectIdSlow` 全场景扫描，否则跨场景引用会制造警告并造成 `EditorLoop` 尖峰。
- 内容工坊保持在 `Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`，只把可验证的差异写回 JSON，不在运行时程序集引入编辑器依赖。
- 业务日志用 `GameLogManager` 的 `[WORK]` 接口；不要制造每帧重复警告。
- `GMReflectionConsole` 独占 F4 作为 GM 调试面板开关；管理员手持物品加量由面板按钮调用，`GameDebugManager` 的晴天快捷键必须在脚本默认值与 `WorldManager.prefab` 序列化值中都使用 F6，禁止运行时反射改键。

## 验证

- 自动断言对象、材质、池化生命周期和关键参数；最终粒子/Shader 观感才做定向视觉检查。
- 触发属于战斗、天气、UI 或音频时加载对应领域 Skill。
- 默认不主动跑测试；需要时运行 `EffectsTools.Smoke`。入口：`Assets/GameTest/EffectsTools/EffectsToolsSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
