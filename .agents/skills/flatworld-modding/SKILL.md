---
name: flatworld-modding
description: "FlatWorld MOD 清单、内容注册、Lua/C# 生命周期、资源所有权、存档与候选热更新。"
---

# MOD 与 Lua

## 代码与安装入口

- `Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/`：ModRuntimeManager*、ModManifest、ModLuaRuntime（含精简 ModLuaContext）、Mod_LuaBehaviour、ModManagedAssemblyStore；业务 ModApi/ModItemApi 已删除。
- 存档：`World/Map/Data/GameSaveData.Mods.cs`；资源接入：`Core/Lifecycle/GameRes.cs`；流体注册：`World/Fluids/Catalog/FluidContentRegistry.cs`。
- 模板工具：`Assets/Editor/FlatWorld/ProjectTools/Mods/ModTemplateCreator.cs`；清单 schema：`Tools/FlatWorldModSDK/Schemas/manifest.schema.json`；示例：`ModSDK/Examples/`。
- 安装根为 persistentDataPath/Mods/<包目录>/manifest.json；开发示例目录不自动扫描。UI_ModManager 管启停/拖拽排序，新包默认关闭，C# 启停/排序/代码变化重启生效。
- 运行时 F10 IMGUI 管理窗口及其设置快捷编辑入口已移除，设置仍可经 `ModSettingsRegistry`/配置文件读写；主菜单 UI_ModManager 为正式管理入口。C# 包只读检查合并到编辑器“创作与打包工具”。
- xLua 正式绑定配置为 `Assets/Plugins/XLua/Editor/RuntimeBindingsGenConfig.cs`；供应商示例归 `Tools/ReferenceSamples/XLua/`，不参与编译，重新导回时需重生成对应桥接。
- API v2 开放 CS，Lua 直接调用游戏公开系统；物品回调传真实 Item。`mod` 仅保留身份、日志、权威提示、状态和 RegisterCleanup；包内 require 使用 scripts.helper → scripts/helper.lua，每包独立缓存。旧 v1 包须迁移，不保留业务兼容壳。
- 删除游戏事件→JSON→Lua 的重复转发；主 Lua 只保留 Load/ContentReady/Update/Save/LoadSave/Unload，玩法事件直接订阅 C# 并登记退订。
- 构建保留器与 ModAuthoringWindow 同文件：按 Player 程序集的 Assets/5_Scripts 来源生成临时 link.xml，反射调用不为每个业务方法产生包装 CS；特殊 AOT 泛型/委托与第三方类型仍需专项验证。示例为 `ModSDK/Examples/LuaDirectCs/`。

## 加载与生命周期

`本体 → 安全模式/扫描 → 路径/依赖/版本/哈希校验 → 声明资源/定义 → 各内容目录 → Finalize → Lua → ModSetHash`

- 关闭包不进入完整加载，安全模式先于扫描；路径/重解析点、数量、体积与 JSON 长度限制由加载器执行。内容 ID 使用 modId: 命名空间，失败/卸载撤回半注册目录。
- C# 当前仅桌面 Mono，启用包启动默认允许执行，程序集仍须 managed 清单声明；哈希用于完整性、联机一致性和同名程序集变化识别，不作玩家授权流程。IL2CPP 拒绝托管 DLL，AssetBundle 不承载新代码。
- C# MOD 必须引用当前游戏程序集重编译；公开构造/方法增加可选参数也会改变二进制签名，更新时同步替换安装 DLL。直接创建 RuntimeItemDefinition 时传入 moduleResources，按当前目录编译模块装配。
- Initialize 注册扩展，ContentReady 使用正式内容，Dispose 撤本 MOD 补丁/租约；注册归世界资源会话。Harmony 2.3.3 原始 DLL 位于 `StreamingAssets/ManagedModRuntime/0Harmony.dll`，PluginImporter 全平台禁用；托管 MOD 启用时 `Assembly.LoadFrom`，保留 Location/CodeBase，不参与 Unity 编译/Burst，SDK 引用同一文件。
- Actor Lua 经 Mod_LuaBehaviour 固定 modId 并校验 scriptPath；xLua 托管名 XLua.Runtime、原生库名 xlua。可选 preview.png 由管理页按需加载/释放，限制8MB、4096²。
- 存档恢复只要求实际引用 MOD ID 存在，同 ID 使用当前定义并按 modId 恢复全局状态；联机握手严格校验当前集合与内容哈希。

## 内容与扩展契约

- definitionFiles 复用本体 DTO/严格校验，patchFiles 修改既有目标。C# 可存档物品同时注册 ItemDefinitions/AllPrefabs，释放按定义实例身份撤销。
- tiles 使用 TileDefinitionDto/Factory，新增 runtimeTileId≥1000000 且稳定；tile:<id> Patch 不改身份/数字编号。逻辑目录局部校验后发布，卸载先恢复覆盖/移除映射再销毁资源；Bundle type=tile 只提供外观。
- TileBehaviourRegistry 工厂返回独立定义，不持单格/角色状态。普通 JSON 配置已有算法；ResourceEntityCapabilityRegistry 在 Initialize 注册能力地址，实体持冻结计划，DLL 可变状态用 context.SetState/GetState，原生组件需要 Unity TypeManager 注册。
- liquids 由 LiquidDefinitionFactory 注册稳定 ID/primaryColor，heatProcess/drinkEffects/worldWater 复用通用玩法。世界液体只持久化 LiquidId，会话数字编号不入档；Lua 经 CS 复用 ContainerTransferService 等正式事务，不直接改库存冒充转移。
- fluids/atmospheres 同 MOD 多文件汇总预检后 FluidContentRegistry 注册，液体映射/每份体积与 LiquidId 对齐；失败整批撤回，卸载按定义租约释放。contaminations 由 ContaminationSystem 持久化，Lua 经 CS 直接调用，使用完整命名空间 ID。
- ContainerPortFactoryRegistry 的 BeginCandidate/Publish 配合最终目录校验，旧租约不撤新代。Bundle 资源归 MOD 会话，本体地址归 GameRes；Sprite/Material 地址或 bundle/asset 成对字段互斥。
- playerCreationTemplates 用 parent 继承，playerTemplate:<id>/playerTemplateCatalog Patch 修改模板/默认项，不入玩家存档；actors 可深度覆盖模块并继承本体/同批定义。

## 世界内候选更新

- 世界内候选仅保留纯数据路径：已运行 Lua 或启用 C#/Harmony 时拒绝原位重载，候选阶段也禁止首次读取执行新脚本。开放 CS 无法保证任意世界副作用回滚，返回主菜单重载；MOD 自己登记的事件退订在 LuaEnv 释放前逆序执行。
- 未变 Bundle 按路径/SHA256 借用旧句柄，在用二进制变更回主菜单更新；回收转交仍被新目录引用的所有权，失败候选只释放自有句柄。
- 候选模板在停用根下克隆/配置，不改共享 Bundle Prefab、不激活世界实体；退役 Lua 用 DisposeWithoutUnloadCallback 跳过 OnUnload，仍清理已登记订阅。
