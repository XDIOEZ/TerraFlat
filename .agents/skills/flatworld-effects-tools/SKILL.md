---
name: flatworld-effects-tools
description: "Use when: 定位或修改 FlatWorld 的运行时特效、粒子、伤害文字、水体效果、Shader、视觉管理器、项目编辑器工具、结构编辑器、调试脚本或测试辅助。关键词：VisualEffectManager、SpecialEffects、Shader、Editor、GameDebugManager。"
argument-hint: "特效、Shader、编辑器或调试工具问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 特效、Shader 与工具定位

> 最后核对：2026-08-07。

## 运行时视觉

- 特效管理器：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/VisualEffectManager.cs`。
- 特效脚本：`Assets/5_Scripts/SpecialEffects/`。
- 伤害文字：`Assets/5_Scripts/SpecialEffects/DamageTextEffect.cs`。
- 水体效果：`Assets/5_Scripts/SpecialEffects/Mod_FVX_Water.cs`。
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
- 游戏调试目录：`Assets/5_Scripts/5-3_GamePlay/Development/Debug/`。
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

- 2026-08-07：整理 `Assets/Editor/FlatWorld`，按 Automation、ContentTools、DataTables、PrefabBuilders、Productivity、Structures 六类归档；配置资源与对应窗口同目录，脚本 GUID 和菜单路径保持不变。
- 2026-08-07：`Development/Debug` 拆为 `FlatWorld.Gameplay.Debug` 叶子程序集；GM 控制台仍可访问主体运行时系统，`FlatWorld.GameTest` 显式引用该程序集以保留 Buff 目标反射测试。

- 2026-07-31：新增自动持久化的游戏会话日志管理器，支持场景、帧、线程、堆栈、业务调用位置、定时刷新、文件轮转与旧日志清理。
- 2026-07-30：遗迹编辑器右侧属性区支持滚动和容器可视化配置；可选择多库存目标，并按目标 Prefab 的真实槽位以物品 Prefab 预览、数量上限、清空操作完成配置，烘焙前校验成员 ID、槽位、容量和物品引用。
- 2026-07-29：新增统一只读内容校验器 `Assets/Editor/FlatWorld/ContentTools/Validation/FlatWorldContentValidator.cs`；菜单 `FlatWorld/内容配置/校验全部本体内容` 覆盖本体配置，`IPreprocessBuildWithReport` 在正式构建前以同一规则阻断错误，禁止自动修改资产。
- 2026-07-27：完成运行时特效、Shader 与编辑器工具路径首版拆分。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/EffectsTools/EffectsToolsSmokeTests.cs`；当前基础覆盖视觉管理器、伤害文字、粒子 Prefab 和 Shader 入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；特效与工具测试约定目录：`Assets/GameTest/EffectsTools/`；场景目录：`Assets/GameTest/Scenes/EffectsTools/`；冒烟分类：`EffectsTools.Smoke`。
- 新增特效创建回收、Shader 参数、伤害文字或编辑器工具行为时必须增加系统测试；修复 Bug 时先增加回归测试。运行时特效主流程变化时同步更新隔离冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；视觉效果至少验证对象、材质、生命周期和关键参数，最终观感仍交由人工确认。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category EffectsTools.Smoke`；无需视觉模型或测试工具卡片。涉及战斗、地图、环境或 UI 时追加对应分类；只有粒子、Shader 或最终视觉观感变化才做定向截图。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

移动特效 Prefab、材质、Shader、编辑器窗口、菜单工具、调试脚本或测试入口后，必须更新本 Skill；若特效属于战斗、天气、UI 或音频，也同步更新对应系统 Skill。
