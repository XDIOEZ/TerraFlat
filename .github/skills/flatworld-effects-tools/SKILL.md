---
name: flatworld-effects-tools
description: "Use when: 定位或修改 FlatWorld 的运行时特效、粒子、伤害文字、水体效果、Shader、视觉管理器、项目编辑器工具、结构编辑器、调试脚本或测试辅助。关键词：VisualEffectManager、SpecialEffects、Shader、Editor、GameDebugManager。"
argument-hint: "特效、Shader、编辑器或调试工具问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 特效、Shader 与工具定位

> 最后核对：2026-07-27。

## 运行时视觉

- 特效管理器：`Assets/5_Scripts/5-3_GamePlay/Manager/VisualEffectManager.cs`。
- 特效脚本：`Assets/5_Scripts/SpecialEffects/`。
- 伤害文字：`Assets/5_Scripts/SpecialEffects/DamageTextEffect.cs`。
- 水体效果：`Assets/5_Scripts/SpecialEffects/Mod_FVX_Water.cs`。
- 粒子 Prefab：`Assets/2_Prefabs/ParticleEffect/`。
- 项目 Shader 脚本目录：`Assets/5_Scripts/Shader/`。
- Shader 资源目录：`Assets/Shaders/`。

## 编辑器与调试

- 项目编辑器脚本：`Assets/5_Scripts/5-2_Editor/`。
- 额外编辑器目录：`Assets/Editor/`。
- 结构编辑器：`Assets/5_Scripts/5-2_Editor/Structures/`。
- MOD 模板工具：`Assets/5_Scripts/5-2_Editor/Mods/ModTemplateCreator.cs`。
- 音频生成工具：`Assets/5_Scripts/5-6_Audio/Editor/`。
- 游戏调试目录：`Assets/5_Scripts/5-3_GamePlay/Debug/`。
- 调试管理器：`Assets/5_Scripts/5-3_GamePlay/Manager/GameDebugManager.cs`。
- 通用工具：`Assets/5_Scripts/Tool/`、`Assets/5_Scripts/Utilitiles/`。

## 修改前检查

- 特效由哪个系统触发：战斗、Buff、Item、天气、UI 或音频。
- Prefab/材质/Shader 是否通过 Inspector、Resources 或 Addressables 引用。
- 编辑器脚本是否必须位于 Editor asmdef 或 Editor 目录，避免进入运行时程序集。
- Unity2D 项目使用 URP/Light2D；修改 Shader 前确认当前材质实际使用的 Shader 名称。

## 近期变更

- 2026-07-27：完成运行时特效、Shader 与编辑器工具路径首版拆分。

## 修改后维护本 Skill

移动特效 Prefab、材质、Shader、编辑器窗口、菜单工具、调试脚本或测试入口后，必须更新本 Skill；若特效属于战斗、天气、UI 或音频，也同步更新对应系统 Skill。
