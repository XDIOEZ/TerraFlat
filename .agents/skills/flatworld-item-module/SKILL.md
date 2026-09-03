---
name: flatworld-item-module
description: "Use when: 定位或修改 FlatWorld 的 Item/Module 组合架构、实体创建销毁、对象池、模块加载保存、Tick 调度、运行时注册、空间索引或网络模块序列化。关键词：ItemMgr、Item、Module、ItemMods、ItemMaker。"
---

# FlatWorld Item / Module

## 入口

- 生命周期：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/{Item,Module,ItemMods,ItemMaker}.cs`
- 管理器：`Entities/Item/Management/ItemMgr*.cs`（Spawning/Perception/Players/RandomDrop partial）
- 数据：`Assets/5_Scripts/5-1_Data/{ItemData/ItemData,ModData/ModuleData}.cs`
- 本体定义：`Assets/StreamingAssets/GameConfig/Items/item-manifest.json`
- 模板化物品编辑：`Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`
- Actor 定义复用 Item/Module 实例化与池化：`Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs`

## 主链与不变量

`ItemMaker/ItemMgr → ItemData → ItemMods → ModuleInit/Load → ItemMgr 分级 Tick → Save/Despawn/Pool`

- Module 明确选择 EveryFrame、FixedInterval 或 Disabled；增删模块、配置变化和池复用必须使调度缓存失效。
- 注册/注销、保存/销毁各执行一次；`PrepareForDespawn` 与 `OnDestroy` 不得被外部重复调用。
- 远程网络副本不进入本地 Tick、感知和存档索引。
- 新模块同时检查脚本、ModuleData、模块/Item Prefab、Addressables 与 JSON 定义。
- 遇到“物品找不到模块 Prefab”时先核对 `[GameRes] Prefab 加载计划`；若通用 Prefab 数量为 0，根因是全局 Addressables/Locator 启动失败，禁止误改首个报错物品的 JSON 或模块 Prefab。
- JSON 本体按职责组合通用模块；单个资源节点的名称和玩法配置不能成为专用模块 Prefab。周期资源应由生产模块写入库存接收契约，再由采集模块处理交互和掉落。
- 通用世界实体外壳只能提供 `Item`、表现节点与查询 Collider；作物等玩法必须由 JSON 组合模块。成熟交互的可扩展副作用通过 `ICropHarvestAction` 注册，权威状态模块只负责按顺序调度动作与结束实体生命周期。
- Prefab 必须由 Unity 序列化生成，禁止手写根对象 `fileID: 100100000`；该值是 Prefab 资产保留 ID，把它分配给 GameObject 会触发 `GameObject to Prefab` 的 PPtr 转换错误。
- 批量调整 Prefab override 后不得在 `m_Modification.m_Modifications` 序列中留下空项；Unity 会把对应 `PrefabInstance` 判为损坏并在导入时删除整个嵌套模块，修改后必须重新导入并核对实际层级。
- Item 与 Actor 的 `modules.*.parameters` 共用 `ModuleJsonConfigurator` 严格契约；删除或改名可配置字段后必须同步现行 JSON，并运行“FlatWorld/内容配置/校验全部本体内容”，禁止等到具体实例生成时才发现漂移。
- 运行时生成模块参数时，`Vector2/Vector3` 必须显式写成 `x/y/z` 的 `JObject`；禁止 `JToken.FromObject(UnityEngine.Vector*)`，否则 Json.NET 会遍历 `normalized` 等计算属性并形成自引用。
- JSON 定义实体的死亡战利品由顶层 `lootTableId` 引用全局 `GameConfig/LootTables/loot-tables.json`；表内 `itemId` 是稳定 ItemDefinition ID，运行时才展开为 `LootPrefabName`，禁止再内联 `Data.LootTable` 或保存 `LootPrefab` 对象引用。
- Manifest 是唯一发现入口；包的最终 `shellPrefab` 必须与声明一致。
- JSON 通用 Item Shell 的 SpriteRenderer 必须使用 `SpriteSortPoint.Pivot`；运行时换图也要重新写入该值，透明排序锚点以 Sprite 导入 Pivot 为唯一权威。
- 世界物品若由主体 SpriteRenderer + 子提示/装饰 SpriteRenderer 组成，子层级需要 `sortingOrder` 偏移时必须用根 `SortingGroup` 把整件物品作为一个 Y 深度单元；禁止让子 Renderer 的正偏移直接跨过角色等外部实体的世界排序。
- 世界物品可用 `visual.materialAddress` 声明共享 Addressable 材质；运行时定义必须在对象池复用时显式恢复“配置材质或外壳默认材质”，避免共用 Shell 把上一个物品的材质带给下一个实例。
- `GameRes.CreateItemData`、群系生成与生产模块只接受 Manifest 中存在的 JSON 物品 ID；缺失定义必须直接报错，禁止回退到同名 Prefab。`sourcePrefab` 只用于编辑器迁移定位，不得加入运行时依赖。
- 具体 Prefab 删除后，JSON 必须同步移除 `sourcePrefab`，迁移器则把这类无源定义登记为手工保留项；否则再次执行全量迁移会误删权威 JSON。
- 内容工坊创建物品时只写继承差异：父定义和参考模块必须来自启用分包，Sprite 先生成稳定 Addressables 地址，JSON 写入前校验继承、重复 ID、文件指纹与分包外壳边界。
- `RuntimeItemDefinition.IsActor` 只表示复用通用管线；Actor 还必须登记到 `GameRes.ActorDefinitions` 且外壳包含 `IAIActor`。
- 堆叠身份统一由 `ItemData` 判定，空与 null 特殊数据按现有规范处理。
- 模块 Prefab 的 `ModuleData.Name/ID` 可能未序列化；进入 `ItemMods`、`ModuleInit` 或网络更新前必须统一建立非空身份，禁止直接把空值写入字典。
- JSON 的 `modules.*.prefab` 是模块变体的唯一实例化地址；多个专用 Prefab 可以共用同一玩法 `ModuleData.ID`，`GameRes` 只能为唯一候选登记该 ID 的兼容别名，禁止按加载顺序静默覆盖。
- `ItemPicker` 不能只依赖 `OnTriggerEnter2D`：掉落/飞行或联机预约可能让物品先以不可拾取状态进入范围，状态恢复后应补偿检查，并限制为一次性请求以避免部分入包或网络请求重复执行。
- 掉落拾取时序由 `Mod_Droping` 的轨迹状态决定：必须先移除掉落模块，再把 `CanBePickedUp` 设为 true；拾取器不能只信任这个数据标志。

## 验证

- 检查加载→Tick→保存→Despawn→复用后无旧状态、订阅、空间索引或调度残留。
- 生命周期/ModuleData 联动 Data Skill；网络状态联动 Networking；具体玩法只加载其领域 Skill。
- 默认不主动跑测试；需要时运行 `ItemModule.Smoke`。入口：`Assets/GameTest/ItemModule/ItemModuleSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
