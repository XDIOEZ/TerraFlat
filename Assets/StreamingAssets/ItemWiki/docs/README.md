# FlatWorld 游戏设计文档导航

> 本目录是 FlatWorld **当前游戏机制的开发导航与设计文档真源**。它描述“游戏现在怎么运行”，不是历史策划归档，也不是未来愿望清单。

## 使用规则

1. 修改某个游戏机制前，先读本页并进入对应系统文档，再读取文档列出的项目 Skill 与实现入口。
2. 机制、数据真源、模块关系或玩家规则发生变化时，必须在同一次开发任务中同步更新对应 Markdown。
3. 如果文档与代码/JSON/Prefab 的真实状态冲突，以当前项目实现为准，并立刻修正文档。
4. 尚未实现的设计只能写在“待设计/待实现”区域，不能混入“当前机制”。
5. 历史策划文档继续作为背景资料保留，但不作为当前实现依据。

## 当前系统形势

FlatWorld 当前已经形成清晰的三层结构：

- **内容层**：Item、Recipe、Buff、Player 创建模板等内容主要由 `Assets/StreamingAssets/GameConfig/` 下的 JSON 提供。
- **运行时规则层**：Item/Module、Combat、Inventory、Building、Environment 等领域模块负责玩法状态和规则。
- **世界模型层**：`5-0_WorldModel` 负责纯 C# 的 Chunk 权威状态与确定性生成；Unity Tilemap、Collider、Renderer 只负责表现和交互适配。

当前开发方向已经不是“每个物品/系统做一个专用脚本”，而是**稳定 ID + 数据定义 + 通用模块组合 + 权威状态集中管理**。新增玩法优先接入已有契约，不应创建平行系统。

## 系统目录

| 系统 | 文档 | 当前核心形态 |
| --- | --- | --- |
| 核心生命周期 | [core-lifecycle.md](systems/core-lifecycle.md) | GameManager + GameRes 统一进入/退出世界与资源会话 |
| Item / Module | [item-module.md](systems/item-module.md) | JSON ItemDefinition + 通用 Module 组合 |
| 背包与快捷栏 | [inventory.md](systems/inventory.md) | Inventory 事务 + Hand/HotBar + 重量/体积容量 |
| 装备 | [equipment.md](systems/equipment.md) | `Mod_Equipment` 统一装备栏、效果与装备存档 |
| 制作与加工 | [crafting.md](systems/crafting.md) | Recipe JSON + CraftingService 原子事务 |
| 战斗 | [combat.md](systems/combat.md) | DamageReceiver 唯一生命权威 + 四类伤害 |
| 生存 | [survival.md](systems/survival.md) | 营养/水分/体力/氧气/体温分模块结算 |
| 建筑 | [building.md](systems/building.md) | Summoner → Preview → 动态建筑或 Tile 建筑 |
| 农业 | [agriculture.md](systems/agriculture.md) | Farmland + Crop 模块组合 + 区块农业存档 |
| 地图内容 | [map.md](systems/map.md) | Tile/Biome/Structure/生态规则定义地图内容 |
| 地图生成 / WorldModel | [world-generation.md](systems/world-generation.md) | 确定性 Chunk 生成 + 后台调度 + 主线程绑定 |
| 环境 | [environment.md](systems/environment.md) | 时间/天气/温度/风/积雪/污染 |
| 维度 | [dimension.md](systems/dimension.md) | 地表与矿洞独立 WorldKey、独立区块差量 |
| Buff | [buff.md](systems/buff.md) | JSON BuffDefinition + BuffManager 生命周期 |
| 存档与数据 | [save-data.md](systems/save-data.md) | MemoryPack 当前版本存档 + JSON 内容真源 |
| UI | [ui.md](systems/ui.md) | 正式 Prefab + UIManager/BasePanel + 灰阶统一主题 |

## 开发导航

### 我不知道一个物品能力从哪里来的

先读 [Item / Module](systems/item-module.md)，再查看具体领域文档。物品显示名、模块组合、视觉和多数玩法参数优先从 Item JSON 定义追踪。

### 我不知道地图上的东西是谁生成的

- 地形、群系、生态内容：读 [地图内容](systems/map.md)。
- Chunk 请求、后台生成、运行时窗口、表现绑定：读 [地图生成 / WorldModel](systems/world-generation.md)。
- 洞穴：同时读 [维度](systems/dimension.md)。

### 我不知道一个玩家数值由谁负责

- 生命与伤害：读 [战斗](systems/combat.md)。
- 营养、体力、氧气、体温：读 [生存](systems/survival.md)。
- Buff 状态：读 [Buff](systems/buff.md)。
- 装备加成：读 [装备](systems/equipment.md)。

### 我不知道数据应该存在哪里

先读 [存档与数据](systems/save-data.md)。原则上先确定权威状态，再决定 JSON 内容定义、ItemSpecialData、ModuleData、PlanetData 或 Chunk 差量。

## 文档维护模板

新增系统文档至少包含：

- 系统定位
- 当前机制
- 主数据流/生命周期
- 权威数据与配置来源
- 关键实现入口
- 与其他系统的边界
- 修改时必须联动检查的内容
- 当前限制/待设计项
