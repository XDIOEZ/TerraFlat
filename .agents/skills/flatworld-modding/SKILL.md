---
name: flatworld-modding
description: "Use when: 定位或修改 FlatWorld 的 MOD 扫描、manifest、依赖排序、内容哈希、AssetBundle、JSON 物品定义、Lua 生命周期、MOD API、MOD 存档或模板工具。关键词：ModRuntimeManager、ModManifest、ModApi、ModLuaRuntime。"
---

# FlatWorld MOD 与 Lua

## 入口

- 管理/模型/API/Lua：`Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/{ModRuntimeManager,ModManifest,ModApi,ModLuaRuntime,Mod_LuaBehaviour}.cs`
- 存档：`World/Map/Data/GameSaveData.Mods.cs`
- 模板：`Assets/Editor/FlatWorld/ProjectTools/Mods/ModTemplateCreator.cs`
- 本体接入：`Core/Lifecycle/GameRes.cs`

## 加载与不变量

`本体资源完成 → 扫描 persistentDataPath/Mods → 路径/版本/依赖/哈希校验 → Bundle/definitionFiles → Item→Actor→Recipe→Buff→Quest → 目录 Finalize → Lua → ModSetHash`

- 保留路径、防重解析点、文件数/体积/JSON 长度限制；不要为方便绕过安全校验。
- manifest ID、版本范围、依赖顺序与内容哈希参与兼容；格式变化需版本迁移。
- MOD 内容 ID 使用 `modId:` 命名空间，冲突必须可诊断；失败/卸载不得留下半注册内容。
- JSON 定义复用本体 DTO 与校验器；旧 Recipe AssetBundle 仅作兼容桥。
- `actors` 可继承本体/同批 MOD Actor，深度覆盖 modules；Bundle 外观用 sprite/animator 成对字段。
- Actor Lua 必须使用 `Mod_LuaBehaviour`，运行时强制所属 modId 并校验 scriptPath 不越界；AssetBundle 不承载新 C# 代码。
- ModSetHash/存档记录或加入世界握手变化联动 Networking 与 Data；具体定义联动对应领域 Skill。

## 验证

- 在隔离 MOD 目录覆盖合法、缺依赖、循环依赖、损坏配置、卸载清理与 Lua 生命周期。
- 默认不主动跑测试；需要时运行 `Modding.Smoke`。入口：`Assets/GameTest/Modding/ModdingSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
