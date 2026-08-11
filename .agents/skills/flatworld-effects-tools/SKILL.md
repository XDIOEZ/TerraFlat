---
name: flatworld-effects-tools
description: "Use when: 定位或修改 FlatWorld 的运行时特效、粒子、伤害文字、水体效果、Shader、视觉管理器、项目编辑器工具、结构编辑器、调试脚本或测试辅助。关键词：VisualEffectManager、SpecialEffects、Shader、Editor、GameDebugManager。"
---

# FlatWorld 特效、Shader 与工具定位

> 最后核对：2026-08-09。

## 运行时视觉
- 特效管理器：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/VisualEffectManager.cs`。
- 特效脚本：`Assets/5_Scripts/SpecialEffects/`。
- 伤害文字：`Assets/5_Scripts/SpecialEffects/DamageTextEffect.cs`。
- 水体效果：`Assets/5_Scripts/SpecialEffects/Mod_FVX_Water.cs`；角色浸没表现由 `Assets/5_Scripts/5-3_GamePlay/Presentation/ActorRenderEffectController.cs`、`WaterImmersionRenderEffect.cs` 和 `Assets/5_Scripts/5-3_GamePlay/World/Map/TileBehv/Tile_Water.cs` 协作处理；角色受击/状态染色由同目录 `ActorRenderColorEffect.cs` 通过 MPB 接入。

## 编辑器与调试
- 项目编辑器脚本：`Assets/5_Scripts/5-2_Editor/`。
- FlatWorld 编辑器工具根目录：`Assets/Editor/FlatWorld/`；目录职责见同目录 `README.md`。
- 内容工具：`ContentTools/{Items,Migrations,Validation}/`。
- 数值表工具及其配置：`DataTables/{Food,Prefab}/`。

## 修改前检查
- 特效由哪个系统触发：战斗、Buff、Item、天气、UI 或音频。
- Prefab/材质/Shader 是否通过 Inspector、Resources 或 Addressables 引用。
- 编辑器脚本是否必须位于 Editor asmdef 或 Editor 目录，避免进入运行时程序集。
- Unity2D 项目使用 URP/Light2D；修改 Shader 前确认当前材质实际使用的 Shader 名称。

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：淡水饮用复用 `VisualEffectManager` 的 `Particle_BeEat` 名称池，每个饮水 Tick 将取出的粒子实例重置为浅蓝/深蓝水滴并在 0.8 秒后自动回收，不新增常驻粒子对象。
- 2026-08-12：修复武器切割攻击特效池化后消失：`SlicingEffect` 缓存 Prefab 初始旋转，并在每次从 `GameEffect` 池取出时恢复旋转、重置 Animator 到首帧，保证连续命中均正常播放。
- 2026-08-09：`ChunkLightOccluderRenderer` 将新区块静态阻挡格合并为少量单位方形 URP 2D `ShadowCaster2D`，只在绑定/墙体变化时刷新，并通过 `CompositeShadowCaster2D` 组织 `LightOccluders` 子层；不新增每帧光照射线计算。
- 2026-08-09：修复幽灵受击不闪红：幽灵 Prefab 根节点补挂 `ActorRenderEffectController` 与 `ActorRenderColorEffect`，让 `Visual` SpriteRenderer 进入统一 MPB 受击链路；不改幽灵伤害结算。
- 2026-08-09：统一受击表现改为红色；`DamageReceiver`、`ActorRenderColorEffect`、Animator 模块 Prefab 及 `Sprite-Lit-Master` Shader 默认值保持一致，并补齐 Unlit Pass 的 MPB 参数声明。

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/EffectsTools/EffectsToolsSmokeTests.cs`；当前基础覆盖视觉管理器、伤害文字、粒子 Prefab 和 Shader 入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；特效与工具测试约定目录：`Assets/GameTest/EffectsTools/`；场景目录：`Assets/GameTest/Scenes/EffectsTools/`；冒烟分类：`EffectsTools.Smoke`。
- 新增特效创建回收、Shader 参数、伤害文字或编辑器工具行为时必须增加系统测试；修复 Bug 时先增加回归测试。运行时特效主流程变化时同步更新隔离冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；视觉效果至少验证对象、材质、生命周期和关键参数，最终观感仍交由人工确认。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category EffectsTools.Smoke`；无需视觉模型或测试工具卡片。涉及战斗、地图、环境或 UI 时追加对应分类；只有粒子、Shader 或最终视觉观感变化才做定向截图。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill
移动特效 Prefab、材质、Shader、编辑器窗口、菜单工具、调试脚本或测试入口后，必须更新本 Skill；若特效属于战斗、天气、UI 或音频，也同步更新对应系统 Skill。
