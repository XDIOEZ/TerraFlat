---
name: flatworld-effects-tools
description: "Use when: 定位或修改 FlatWorld 的运行时特效、粒子、伤害文字、水体效果、Shader、视觉管理器、项目编辑器工具、结构编辑器、调试脚本或测试辅助。关键词：VisualEffectManager、SpecialEffects、Shader、Editor、GameDebugManager。"
argument-hint: "特效、Shader、编辑器或调试工具问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 特效、Shader 与工具定位

> 最后核对：2026-08-09。

## 运行时视觉

- 特效管理器：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/VisualEffectManager.cs`。
- 特效脚本：`Assets/5_Scripts/SpecialEffects/`。
- 伤害文字：`Assets/5_Scripts/SpecialEffects/DamageTextEffect.cs`。
- 水体效果：`Assets/5_Scripts/SpecialEffects/Mod_FVX_Water.cs`；角色浸没表现由 `Assets/5_Scripts/5-3_GamePlay/Presentation/ActorRenderEffectController.cs`、`WaterImmersionRenderEffect.cs` 和 `Assets/5_Scripts/5-3_GamePlay/World/Map/TileBehv/Tile_Water.cs` 协作处理；角色受击/状态染色由同目录 `ActorRenderColorEffect.cs` 通过 MPB 接入。
- Buff 附着表现：`Assets/5_Scripts/5-3_GamePlay/Presentation/ActorStatusVisualEffectController.cs` 监听 `BuffManager` 事件并播放配置化 Sprite 序列、低强度状态光晕或 `VisualEffectManager` 池化粒子；当前燃烧复用 `Assets/6_Art/PraticalEffect/Burning/CreatureBurning_Sheet.png` 的八帧，出血类状态复用 `BloodDropStatusEffect`，`光耀` 复用 `Assets/6_Art/PraticalEffect/Circle.png` 做低透明度呼吸光，装配于 `Module_Animator.prefab` 与 `Animator/Module_Animator_AI.prefab`。运行时子精灵带 `ActorRenderEffectExclude`，不会被角色水下/受击 MPB 覆盖。
- 雨滴落地水花：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/RainGroundSplashController.cs` 复用单个世界空间粒子系统；`WeatherMgr.cs` 独立加载 `Assets/Resources/Weather/RainGroundSplash.prefab`，配套 `Assets/Shaders/Shader/Rain-GroundSplash.shader` 与 `Assets/Resources/Weather/Materials/RainGroundSplash.mat`；原雨层的位置与覆盖范围由 `RainEffectController`/`RainEffect.prefab` 维护。
- 粒子 Prefab：`Assets/2_Prefabs/ParticleEffect/`。
- 项目 Shader 脚本目录：`Assets/5_Scripts/Shader/`。
- Shader 资源目录：`Assets/Shaders/`。

## 编辑器与调试

- 项目编辑器脚本：`Assets/5_Scripts/5-2_Editor/`。
- FlatWorld 编辑器工具根目录：`Assets/Editor/FlatWorld/`；目录职责见同目录 `README.md`。
- 内容工具：`ContentTools/{Items,Migrations,Validation}/`。
- 数值表工具及其配置：`DataTables/{Food,Prefab}/`。
- Prefab 构建器：`PrefabBuilders/{UI,Building}/`。
- 编辑器效率工具：`Productivity/`；黄金路径与结构工具仍分别位于 `Automation/`、`Structures/`。
- 结构编辑器：`Assets/5_Scripts/5-2_Editor/Structures/`。
- MOD 模板工具：`Assets/5_Scripts/5-2_Editor/Mods/ModTemplateCreator.cs`。
- 音频生成工具：`Assets/5_Scripts/5-6_Audio/Editor/`。
- 游戏调试目录：`Assets/5_Scripts/5-3_GamePlay/Development/Debug/`；`GMReflectionConsole.Navigation.cs` 维护 F4 分页，领域操作拆入 `Buffs`/`Quests` partial，任务页只调用任务运行时公共 API。
- 游戏调试程序集：`FlatWorld.Gameplay.Debug`；它是依赖 `GamePlay` 的叶子程序集，生产运行时代码不得反向引用 `GMReflectionConsole`。
- 调试管理器：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameDebugManager.cs`。
- 会话日志管理器：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameLogManager.cs`；启动时自动收集 Unity 日志到 `Application.persistentDataPath/GameLogs/`，业务关键流程使用 `Log`、`LogWarning`、`LogError`、`LogException` 记录带调用位置的 `[WORK]` 日志。
- 通用工具：`Assets/5_Scripts/Tool/`、`Assets/5_Scripts/Utilitiles/`。

## 修改前检查

- 特效由哪个系统触发：战斗、Buff、Item、天气、UI 或音频。
- Prefab/材质/Shader 是否通过 Inspector、Resources 或 Addressables 引用。
- 编辑器脚本是否必须位于 Editor asmdef 或 Editor 目录，避免进入运行时程序集。
- Unity2D 项目使用 URP/Light2D；修改 Shader 前确认当前材质实际使用的 Shader 名称。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-11：F4 `GMReflectionConsole` 新增任务调试 partial 和独立分页；动态列出 `debugOnly` 任务，支持开启、批量开启、刷新与交付，场景切换/销毁时解除任务事件订阅。
- 2026-08-09：`ChunkLightOccluderRenderer` 将新区块静态阻挡格合并为少量单位方形 URP 2D `ShadowCaster2D`，只在绑定/墙体变化时刷新，并通过 `CompositeShadowCaster2D` 组织 `LightOccluders` 子层；不新增每帧光照射线计算。
- 2026-08-09：修复幽灵受击不闪红：幽灵 Prefab 根节点补挂 `ActorRenderEffectController` 与 `ActorRenderColorEffect`，让 `Visual` SpriteRenderer 进入统一 MPB 受击链路；不改幽灵伤害结算。
- 2026-08-09：统一受击表现改为红色；`DamageReceiver`、`ActorRenderColorEffect`、Animator 模块 Prefab 及 `Sprite-Lit-Master` Shader 默认值保持一致，并补齐 Unlit Pass 的 MPB 参数声明。
- 2026-08-09：状态表现控制器新增 `光耀` 低强度状态光晕；复用 `Circle.png` 作为金黄色圆形叠加，以轻微呼吸缩放和透明度变化表现发光，Buff 移除/对象禁用时隐藏，不改变 Buff 伤害逻辑。
- 2026-08-09：状态表现控制器新增 VisualEffectManager 池化粒子配置；`出血`、`流血`、`失血` 共用 `BloodDropStatusEffect` 循环红色血滴，按当前角色 Sprite 高度跟随位置和排序，并在 Buff 移除/对象禁用时回收到对象池。
- 2026-08-09：燃烧附着火焰的垂直锚点增加可配置偏移，玩家与 AI 默认向下移动角色高度的 `5%`，避免火焰底部露出玩家脚部；不改变缩放、帧率和 Buff 生命周期。
- 2026-08-09：新增配置化角色状态附着特效控制器；燃烧 Buff 添加时立即显示并以 `10fps` 循环八帧火焰，续期不中断、移除/过期立即隐藏。火焰依据当前角色 Sprite 高度自适应缩放和排序，玩家与 AI 共用动画模块均已接线。
- 2026-08-09：雨效改为由 `RainEffectController` 单点跟随相机顶部，`WeatherMgr` 不再重复写入根 Transform；保留原单边顶部发射线，粒子寿命按正交相机高度、初始下落速度和 `1.12` 倍余量动态计算，确保下半屏持续有雨且不会无限落到地图外。`RainGroundSplash` 优先采样非水非阻挡地形，区块尚未 Ready 时在可视范围降级发射；频率提升至小雨 `12/s`、暴雨 `48/s`，生命周期 `0.32–0.5s`、上限 `80`，保证中雨约 12 个同时可见水花。
- 2026-08-09：草地 Tilemap 接入独立 `Grass-Sway-Lit` Shader，使用 GPU 顶点风场、根部固定的弯曲权重和共享材质参数；`ChunkGrassRenderer`、`GrassDetailLayer` 及对应 Prefab 共用该材质并扩展裁剪边界。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/EffectsTools/EffectsToolsSmokeTests.cs`；当前基础覆盖视觉管理器、伤害文字、粒子 Prefab、Shader 入口，以及 GM 任务分页、公共任务 API 和无独立 `Update` 契约。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；特效与工具测试约定目录：`Assets/GameTest/EffectsTools/`；场景目录：`Assets/GameTest/Scenes/EffectsTools/`；冒烟分类：`EffectsTools.Smoke`。
- 新增特效创建回收、Shader 参数、伤害文字或编辑器工具行为时必须增加系统测试；修复 Bug 时先增加回归测试。运行时特效主流程变化时同步更新隔离冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；视觉效果至少验证对象、材质、生命周期和关键参数，最终观感仍交由人工确认。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category EffectsTools.Smoke`；无需视觉模型或测试工具卡片。涉及战斗、地图、环境或 UI 时追加对应分类；只有粒子、Shader 或最终视觉观感变化才做定向截图。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

移动特效 Prefab、材质、Shader、编辑器窗口、菜单工具、调试脚本或测试入口后，必须更新本 Skill；若特效属于战斗、天气、UI 或音频，也同步更新对应系统 Skill。
