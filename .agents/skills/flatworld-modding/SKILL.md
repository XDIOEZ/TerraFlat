---
name: flatworld-modding
description: "Use when: 定位或修改 FlatWorld 的 MOD 扫描、manifest、依赖排序、内容哈希、AssetBundle、JSON 物品定义、Lua 生命周期、MOD API、MOD 存档或模板工具。关键词：ModRuntimeManager、ModManifest、ModApi、ModLuaRuntime。"
---

# FlatWorld MOD 与 Lua 系统定位

> 最后核对：2026-08-03。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModRuntimeManager.cs`：扫描、校验、排序、加载、卸载和全局状态。
2. `Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModManifest.cs`：manifest、依赖、Bundle、内容定义与存档记录。
3. `Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModApi.cs`：公开给 MOD/Lua 的游戏 API。
4. `Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModLuaRuntime.cs`：Lua 运行时。

## 加载链
```text
GameRes 完成本体 Addressables
→ ModRuntimeManager.LoadEnabledMods
→ 扫描 Application.persistentDataPath/Mods/*/manifest.json
→ 校验路径、版本、依赖、内容哈希
→ 加载 AssetBundle 与 definitionFiles
→ 克隆/注册资产和 Item 定义
→ 初始化 entryLua
→ 计算 ModSetHash
```

## 关键文件
- Lua 行为组件：`Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/Mod_LuaBehaviour.cs`。
- MOD 存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/GameSaveData.Mods.cs`。
- 示例模板：`Assets/5_Scripts/5-2_Editor/Mods/ModTemplateCreator.cs`。
- 本体资源衔接：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameRes.cs`。
- MOD `definitionFiles` 可直接声明 `recipes` 数组，格式复用本体 `RecipeDto`；配方 ID 必须使用 MOD 命名空间。
- 旧 `assets[].type = recipe` AssetBundle 仍通过 `LegacyRecipeConverter` 转成 `RuntimeRecipe`，仅作为兼容桥。

## 安全与兼容边界
- MOD 根目录是 `Application.persistentDataPath/Mods`，不是 Assets 内固定目录。
- 保持路径解析、防重解析点、文件数/体积/JSON 长度限制，不能为方便加载而绕过。
- manifest ID、版本范围、依赖顺序和内容哈希参与兼容判断；改格式需提供版本迁移。
- MOD 注册的 Prefab/Item ID 需避免覆盖本体或其他 MOD，除非 API 明确允许。

## 高耦合联动
只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；Lua 脚本内部玩法逻辑只加载它实际调用的领域 Skill。
| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `GameRes` 接入点、本体/MOD 加载顺序、全局注册或覆盖规则 | `flatworld-core` | 本体资源先完成、冲突可诊断、失败不留下半注册内容 | `Core.Smoke` |

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-08：本体与 MOD 的 JSON 物品显示统一通过 `GameRes.TryGetItemPresentation()` 读取解析后的 `gameName` 与 `visual.spriteAddress`；共享外壳只负责实例结构，不再作为 UI 显示数据源。
- 2026-08-08：本体物品 JSON 新增 `labelKey`/`descriptionKey`，由 Unity Localization 的 `FlatWorld` String Table 提供语言文本；MOD 现有同名字段与 `ModLocalizationRegistry` 继续保持兼容。
- 2026-07-28：MOD 定义文件新增纯 JSON `recipes`，加载顺序为先物品 Def、再校验并注册配方；保留旧 Recipe AssetBundle 转换兼容。
- 2026-07-27：当前 MOD 流程在本体资源加载后执行，支持 manifest 依赖排序、AssetBundle、JSON Item 定义、Lua 生命周期与集合哈希。

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/Modding/ModdingSmokeTests.cs`；当前基础覆盖运行时管理、manifest、Lua 与模板工具入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；MOD 测试约定目录：`Assets/GameTest/Modding/`；场景目录：`Assets/GameTest/Scenes/Modding/`；冒烟分类：`Modding.Smoke`。
- 新增 manifest、依赖排序、内容哈希、AssetBundle、JSON、Lua 生命周期或 MOD 存档行为时必须增加系统测试；修复 Bug 时先增加回归测试。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试 MOD 必须位于隔离目录，覆盖合法、缺失依赖、循环依赖和损坏配置，并在结束时清理。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Modding.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill
改变 manifest 字段、RecipeDto、JSON `recipes`、API 版本、目录结构、限制值、Lua 生命周期、Bundle/定义文件位置、存档记录或 `GameRes` 接入点后，必须更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。
