---
name: flatworld-item-module
description: "FlatWorld 物品/模块的身份、生命周期、定义装配、调度与世界实体后端。"
---

# Item / Module

## 定位与按需读取

代码根：`Assets/5_Scripts/5-3_GamePlay/`。

| 目录 | 入口 |
| --- | --- |
| `Entities/Item/Core/` | Item、Module、ItemMods、ItemMaker：生命周期 |
| `Entities/Item/Management/` | ItemMgr*：生成/注册、玩家、感知、距离档 |
| `Entities/Item/Definitions/` | ItemDefinitionRuntime、ModuleJsonConfigurator：装配/刷新 |
| `Entities/DroppedItems/` | DroppedItemService、DroppedItemRuntime* |
| `Entities/NaturalECS/` | NaturalEntityEcsService*、NaturalEntityEcsProfileCompiler |
| `World/Chunk/Management/` | WorldItemWaterSystem：完整 Item 水态 |

数据：`Assets/5_Scripts/5-1_Data/{ItemData/ItemData,ModData/ModuleData}.cs`；清单：`Assets/StreamingAssets/GameConfig/Items/item-manifest.json`；编辑器：`Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`。Actor 独立内容目录，复用装配/池化。

仅按当前任务读参考，不批量加载：

- Tick、距离档、感知、空间/环绕：[scheduling-and-space.md](references/scheduling-and-space.md)。
- 定义编译/绑定、重载、动画、物质/燃烧：[definitions-state-visual.md](references/definitions-state-visual.md)。
- 掉落、水态、自然 ECS 与发布：[world-backends.md](references/world-backends.md)。

## 身份与模块边界

- 运行时物品归属由 `GameplayWorldRegistry` 的世界物品账本和反向索引统一维护，Item 不缓存世界身份；注册入口按拥有者或明确世界上下文登记。Item 的 Load/Save/Unload/Tick 借用自身世界，每步回调核对归属修订，作用域不跨 yield。船内起降沿正式事务迁原实例、机器和库存，不能通过重建外壳绕过账本。
- 轻量掉落模拟按 WorldKey 持有，Scene 解绑只释放显示，重绑沿用原载荷与句柄；`TransferWorldItems` 批量移交保留旧句柄转发和回滚。`SimulateWithoutObservers` 的局部世界持续运行，不按本机视距停更物品；其它未加载世界仍按实际宿主能力处理。
- 能力由 Prefab/JSON 显式组合。StableName 是 Item 内唯一实例槽键；ModuleId 是可共享能力 ID；PrefabId/JSON prefab 是实现地址，不进存档。JSON 键定 StableName，Prefab-only 用确定性对象名；同能力多实现的内嵌节点显式填与 JSON 一致的 StableName。
- 默认能力通过 CanonicalModuleId 由统一绑定入口写入，不在 Awake 改空模板 ID。JSON 显式 ID 仍符合依赖契约；来源模板与当前定义身份的编译约束见定义参考。
- 编译/绑定/额外组件清退只归最近 Item，不穿子物品；独立模块 Prefab 不含 Item。独立模块拥有 Prefab 根，内嵌模块只拥有组件。
- IItemModuleDependencyBinder 在配置完成后解析唯一 ID/接口。ItemMods 只读视图经 AddMod/RemoveMod/BeginMutation 更新结构版本/能力缓存，可变 ModuleData 不跨宿主共享。
- RuntimeGeneration 每 Load 更新且不存档；战斗身份含 UID、world/dimension、该代际，感知另有代际。同实例注册幂等，不同实例 GUID 冲突报错，注销用登记时身份。

## 装配、加载与回收

`当前定义 → ItemData → 全模块登记 → ModuleInit → 依赖绑定 → OnLoad → Tick/Save → OnUnload → Despawn/Pool`

- Item.Load/LoadInSteps 共用该顺序；跨帧保留生命周期切换态，释放枚举器清理已加载模块。InstantiateItemInSteps 复用壳池，冷壳 InstantiateAsync，完成前不进世界/保存/网络索引，Profiler 作用域不跨让帧。
- Module.Load/Save/Unload 是受控入口，子类覆写 OnLoad/OnSave/OnUnload；实际加载态决定保存/Tick，Enabled 是配置，运行切换 SetEnabled。资源建/清归 OnLoad/OnUnload，保存仅捕状态。
- BindData 只绑未加载/未注册外壳。世界物换定义用 ReplaceRuntimeItem 的停用候选/失败恢复，不直接重绑活实例；自然物在候选成功加载后经 ChunkNaturalItemRenderer.ReplaceNaturalItem 交接生态索引与销毁监听，失败保留原归属。场景销毁只经 ItemMgr.GetExistingInstance 注销，管理器已毁仍卸模块，不逐件搜全场单例。
- PrepareItem 在停用池壳装模块/配置/依赖，不配世界 GUID、不 OnLoad；CommitPreparedItem 绑最新数据正式注册/加载，可继续停用待批显。句柄 Dispose 取消，资源实例/定义/重载版本变化失效；机器仍由 MachineWorld 管。
- 池身份在 Item 序列化字段，PooledItemMarker 记 JSON 装配后层级基线；卸载完成且基线匹配才回池，资格独立于 saveData。临时缓冲按调用借还，嵌套生成不共用。
- 恢复先按当前定义建实例，再以 StableName 合并状态；嵌套二进制库存由持有模块逐槽 rebase。生产配置与进度分离，产物身份匹配才保留时间/次数/初始化标记，制作耐久倍率进入堆叠身份。

回归按 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md) 与 systems.catalog.json 选 `item_runtime/*`、`ecology/*`；专项断言名见参考。独立规则结果不代表真实动作观感/场景已验收。
