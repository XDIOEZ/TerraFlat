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

`本体资源完成 → 扫描 persistentDataPath/Mods → 路径/版本/依赖/哈希校验 → Bundle/definitionFiles → Tile→Item→Actor→Recipe→Buff→Liquid→Contamination→Quest → 目录 Finalize → Lua → ModSetHash`

- 保留路径、防重解析点、文件数/体积/JSON 长度限制；不要为方便绕过安全校验。
- manifest ID、版本范围、依赖顺序与内容哈希参与兼容；格式变化需版本迁移。
- MOD 存档元数据不锁死历史版本号和内容哈希：恢复时只要求存档实际引用的 MOD ID 仍存在；同 ID MOD 更新后直接使用当前定义与资源，旧全局运行态按 modId 恢复。联机握手仍继续严格校验当前 MOD 集合与内容哈希。
- MOD 内容 ID 使用 `modId:` 命名空间，冲突必须可诊断；失败/卸载不得留下半注册内容。
- JSON 定义复用本体 DTO 与校验器；旧 Recipe AssetBundle 仅作兼容桥。
- `definitionFiles.tiles` 复用 `TileDefinitionDto/TileDefinitionFactory`，新地块 ID 使用当前 `modId:` 命名空间，并声明不小于 1000000 的稳定 `runtimeTileId`；冲突必须拒绝，不能使用哈希或加载顺序分配编号。`patchFiles` 使用 `tile:<id>` 目标，禁止修改地块身份和数字编号；此目标必须从 Item Patch 分流。
- 地块定义与 Patch 在局部目录全部构建校验后发布；失败/卸载先恢复被覆盖的定义、清理新增数字映射与 MOD TileBase 字典键，再销毁资源。外观仍通过 Bundle `assets.type=tile` 注册；旧 `type=tileblock` 必须明确提示迁移到 JSON，不再读取地块逻辑 SO。
- 新 C# 地块行为在目录加载前经 `TileBehaviourRegistry.RegisterBehaviour` 注册稳定 type 和工厂，扩展持有返回租约并在卸载时释放；工厂每次返回独立定义实例，共享 Behaviour 不持有角色/单格运行态。普通 JSON MOD 只能配置已注册算法，不会凭 JSON 自动创建新的 C# 或 Lua 执行逻辑。
- `definitionFiles` 可通过根数组 `contaminations` 注册自定义地块污染指标，ID 必须使用 `modId:` 命名空间；Lua 使用 `HasContaminationDefinition` 与 `Get/Set/AddContaminationValue` 访问，裸 ID 自动归属当前 MOD。污染运行时值由本体 `ContaminationSystem` 持久化，MOD 不应直接操作 Chunk 环境层。
- `definitionFiles` 可通过根数组 `liquids` 注册自定义液体，定义与本体共用 `LiquidDefinitionFactory` 严格 schema，ID 必须使用 `modId:` 命名空间；通用容器只保存液体 ID 和数量，Lua 物品 API 可查询/加入/移除液体。液体加热行为声明在 `heatProcess`，饮用后 Buff 后果声明在 `drinkEffects`（`buffId/chance/feedback`），两者都由本体通用处理链消费，不要为每种 MOD 液体复制容器、炉体或饮用特判。
- 玩家创建模板可在任意 `definitionFiles` JSON 的 `playerCreationTemplates` 数组中声明；裸 ID 自动归属当前 MOD 命名空间，继承使用 `parent`，修改本体或其他已注册模板使用 `patchFiles` 的 `target: playerTemplate:<id>`，切换默认模板使用 `target: playerTemplateCatalog` 的 `defaultProfileId` Patch；玩家创建配置不写入存档。
- `actors` 可继承本体/同批 MOD Actor，深度覆盖 modules；Bundle 外观用 sprite/animator 成对字段。
- Actor Lua 必须使用 `Mod_LuaBehaviour`，运行时强制所属 modId 并校验 scriptPath 不越界；AssetBundle 不承载新 C# 代码。
- xLua 托管程序集名固定为 `XLua.Runtime`，原生 P/Invoke 库名保持 `xlua`；调整托管名时必须同步程序集限定反射字符串与 `Gen/link.xml`，避免在不区分大小写的平台与 `xlua.dll` 冲突。
- ModSetHash/存档记录或加入世界握手变化联动 Networking 与 Data；具体定义联动对应领域 Skill。

## 验证

- 在隔离 MOD 目录覆盖合法、缺依赖、循环依赖、损坏配置、卸载清理与 Lua 生命周期。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
