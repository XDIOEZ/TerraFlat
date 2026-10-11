---
name: flatworld-modding
description: "FlatWorld MOD 清单、内容注册、Lua/C# 生命周期、资源所有权、存档与候选热更新。"
---

# MOD 与 Lua

## 代码与安装入口

- `Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/`：ModRuntimeManager*、ModManifest、ModApi、ModLuaRuntime、Mod_LuaBehaviour、ModManagedAssemblyStore。
- 存档：`World/Map/Data/GameSaveData.Mods.cs`；资源接入：`Core/Lifecycle/GameRes.cs`；流体注册：`World/Fluids/Catalog/FluidContentRegistry.cs`。
- 模板工具：`Assets/Editor/FlatWorld/ProjectTools/Mods/ModTemplateCreator.cs`；清单 schema：`Tools/FlatWorldModSDK/Schemas/manifest.schema.json`；示例：`ModSDK/Examples/`。
- 安装根为 persistentDataPath/Mods/<包目录>/manifest.json；开发示例目录不自动扫描。UI_ModManager 管启停/拖拽排序，新包默认关闭，C# 启停/排序/代码变化重启生效。

## 加载与生命周期

`本体 → 安全模式/扫描 → 路径/依赖/版本/哈希校验 → 声明资源/定义 → 各内容目录 → Finalize → Lua → ModSetHash`

- 关闭包不进入完整加载，安全模式先于扫描；路径/重解析点、数量、体积与 JSON 长度限制由加载器执行。内容 ID 使用 modId: 命名空间，失败/卸载撤回半注册目录。
- C# 当前仅桌面 Mono，启用包启动默认允许执行，程序集仍须 managed 清单声明；哈希用于完整性、联机一致性和同名程序集变化识别，不作玩家授权流程。IL2CPP 拒绝托管 DLL，AssetBundle 不承载新代码。
- Initialize 注册扩展，ContentReady 使用正式内容，Dispose 撤本 MOD 补丁/租约；注册归资源会话，世界切换不清除。Harmony 2.3.3 由游戏 0Harmony.dll 提供且关闭 Auto Reference，MOD 引用发行包同一 DLL。
- Actor Lua 经 Mod_LuaBehaviour 固定 modId 并校验 scriptPath；xLua 托管名 XLua.Runtime、原生库名 xlua。可选 preview.png 由管理页按需加载/释放，限制8MB、4096²。
- 存档恢复只要求实际引用 MOD ID 存在，同 ID 使用当前定义并按 modId 恢复全局状态；联机握手严格校验当前集合与内容哈希。

## 内容与扩展契约

- definitionFiles 复用本体 DTO/严格校验，patchFiles 修改既有目标。C# 可存档物品同时注册 ItemDefinitions/AllPrefabs，释放按定义实例身份撤销。
- tiles 使用 TileDefinitionDto/Factory，新增 runtimeTileId≥1000000 且稳定；tile:<id> Patch 不改身份/数字编号。逻辑目录局部校验后发布，卸载先恢复覆盖/移除映射再销毁资源；Bundle type=tile 只提供外观。
- TileBehaviourRegistry 工厂返回独立定义，不持单格/角色状态。普通 JSON 配置已有算法；ResourceEntityCapabilityRegistry 在 Initialize 注册能力地址，实体持冻结计划，DLL 可变状态用 context.SetState/GetState，原生组件需要 Unity TypeManager 注册。
- liquids 由 LiquidDefinitionFactory 注册稳定 ID/primaryColor，heatProcess/drinkEffects/worldWater 复用通用玩法。世界液体只持久化 LiquidId，会话数字编号不入档；Lua 容器端口/预检/转移调用服务真实事务，Add/Remove 是独立操作。
- fluids/atmospheres 同 MOD 多文件汇总预检后 FluidContentRegistry 注册，液体映射/每份体积与 LiquidId 对齐；失败整批撤回，卸载按定义租约释放。contaminations 由 ContaminationSystem 持久化，Lua 通过污染 API 访问。
- ContainerPortFactoryRegistry 的 BeginCandidate/Publish 配合最终目录校验，旧租约不撤新代。Bundle 资源归 MOD 会话，本体地址归 GameRes；Sprite/Material 地址或 bundle/asset 成对字段互斥。
- playerCreationTemplates 用 parent 继承，playerTemplate:<id>/playerTemplateCatalog Patch 修改模板/默认项，不入玩家存档；actors 可深度覆盖模块并继承本体/同批定义。

## 世界内候选更新

- HotReload 隔离目录、设置、Lua 与资源所有权，复用正式管理器/事件。候选 OnLoad/OnLoadSave 只初始化隔离状态，发布后副作用由 OnContentReady 执行；提交前捕获最新正式全局状态。
- 未变 Bundle 按路径/SHA256 借用旧句柄，在用二进制变更回主菜单更新；回收转交仍被新目录引用的所有权，失败候选只释放自有句柄。
- 候选模板在停用根下克隆/配置，不改共享 Bundle Prefab、不激活世界实体；失败/退役 Lua 用 DisposeWithoutCallbacks，避免旧回调修改现世界。
