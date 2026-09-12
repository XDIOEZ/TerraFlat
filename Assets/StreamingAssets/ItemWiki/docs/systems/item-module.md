# Item / Module 系统

## 系统定位

FlatWorld 的世界实体、物品和大量角色能力都建立在 Item + Module 组合架构上。具体本体尽量由 JSON 定义，Prefab 提供通用外壳和模块实例。

## 当前机制

- `item-manifest.json` 是 ItemDefinition 的唯一发现入口。
- 具体 Item 可以通过 `parent` 继承公共定义，并在 `modules.*` 中组合功能模块。
- `shellPrefab` 决定运行时外壳；`sourcePrefab` 主要服务编辑器迁移，不是运行时真源。
- Item 生命周期统一经过创建、模块注册、依赖绑定、Load、分级 Tick、Save、Unload/Despawn、对象池复用。
- 跨模块引用使用 `IItemModuleDependencyBinder` 在全部模块注册后统一解析，避免层级搜索和隐式依赖。

## 主链

`ItemMaker / ItemMgr → ItemData → ItemMods → 绑定依赖 → ModuleInit/Load → ItemMgr Tick → Save → Despawn/Pool`

## 权威数据

- Item Manifest：`Assets/StreamingAssets/GameConfig/Items/item-manifest.json`
- Item 分包：`Assets/StreamingAssets/GameConfig/Items/`
- 生命周期：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/`
- 数据：`Assets/5_Scripts/5-1_Data/ItemData/`、`ModData/`

## 当前设计原则

- 稳定 Item ID 负责业务引用，不用 GameObject 名称替代。
- 通用 Shell 不承载具体玩法；具体能力由 JSON 模块组合。
- 世界资源的战利品统一引用全局 LootTable，不在多个模块重复维护。
- 运行时资源必须由 GameRes 资源作用域持有，不能在旁路偷偷加载后失去生命周期管理。
- 远程网络副本不进入本地权威 Tick、感知和存档索引。

## 修改时联动

- 新增 Item：检查 Manifest、分包、Shell、Addressables、视觉和所需模块。
- 新增 Module：检查 ModuleData、Prefab、参数 JSON 契约、生命周期和存档。
- 改死亡掉落：联动战斗与 LootTable。
- 改堆叠/容量：联动背包系统。

## 对应 Skill

`.agents/skills/flatworld-item-module/SKILL.md`
