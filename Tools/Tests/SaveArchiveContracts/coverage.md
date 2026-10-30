# SQLite v1 契约覆盖与缺口

此清单只记录已检查的路径，不能据此推定整个游戏对象图已经兼容未来版本。新增字段必须使用新编号，提供显式缺省；修改或删除已有编号、改变字段含义或嵌套类型时，要增加 Schema 版本、迁移实现以及读取本目录 v1 样本的回归，不能覆盖旧样本。

## 已处理

- 记录格式：`SaveArchiveSchemaRegistry` 登记 Schema ID 与当前版本，未知 ID、未来版本和缺失迁移拒绝；迁移只操作独立载荷。
- 实例壳：`ItemInstanceSnapshot`、`InventoryInstanceSnapshot`、`ModuleInstanceSnapshot`、玩家基础状态、物质状态及库存模块载荷使用固定字段；坐标和姿态拆为基础数值；物质缺省温度、玩家缺省倍率采用可空字段及显式回退。物品快照内的未知模块状态以原字节保留。
- 模块：计时消失、区块距离、区块坐标、简易生存状态、光源、体力、氧气、压力、温度、冷热、理智、音频、快捷栏、宇航服、钓竿、手钻、移动/焦点、画布位置、冷兵器及其体力观察者、生产、植物生长和容器固体库存的当前状态字段固定；未注册的 unmanaged 结构禁止直接写入 `Ex_ModData_MemoryPackable`，扩展可以注册包含 Schema ID 和版本的 codec。冷兵器未加载的观察者保留原始载荷。
- 装备：现有 Union ID 0–7 显式固定，各装备类型及护甲数值固定字段；背包单独保存库存实例以及槽位过滤、容量和 UI 配置。BodyPartType 枚举固定数值。
- 农业：`CropRuntimeData`、`GrowData`、`PlantClimateState` 及相关阶段枚举明确编号，种植坐标拆为整数。
- 液体：流体库存、组分和出口预留字段固定，`decimal` 以不依赖语言环境的字符串保存；容器混合液与待产物字段固定。机械压力事件、气液余量与删除身份使用独立 `MachineArchiveMetadataSnapshot`。
- 机器及星球大气：间接泛型读取的储物、炉子、燃料吸收、培育仓、电器和工业流体状态已编号；独立库存保存槽位标签、容量、名称及 UI 配置。工业流体与大气的 decimal 均按不依赖语言环境的字符串保存，`machine-payload-v1.bin` 和 `atmosphere-v1.bin` 覆盖相应生产类型。
- 生态：冻结 Profile、数值/文本参数、矿脉、生态规则、独立自然实体和区块元数据使用固定字段，自然物转成独立 `ItemInstanceSnapshot` 载荷。掉落物记录字段固定并拆分 Vector2；冻结生态和 SQLite 区块外壳保有 v1 样本。
- 地图实例：`Data_TileMap` 的四种已注册地块、地块栈、二维环境、草层和位置采用固定字段，不直接写运行时 `TileStackCell` / `Vector2Int` / `Vector3Int`；包含农田、草和建筑的固定样本验证。
- 食物：营养实例状态、面板坐标、腐败/融化计时及观察者扩展字节有固定样本；当前未发现官方 `IFoodMechanicStateProvider` 实现，扩展自行保证其 Payload 契约。
- 世界生成：冻结 Profile 保留 `flatworld.generation.algorithmVersion`，当前仅支持 1；地表/洞穴、无限/环绕固定输出样本位于 `Tools/Tests/WorldGenerationV1/`。
- 核心与地形：根/星球、事件、计时、生成器、地形/农业/雪/天气等 DTO 已编号；`core-v1.bin`、`world-v1.bin`、`terrain-v1.bin` 检查核心时间/事件/居民、世界生态/气体及地形和嵌套物品；时间曲线和渐变另有 `time-gradient-v1.bin`。
- 太空 JSON：会话、星区/星体、船体/部件、乘员/运动、设备、停靠、电梯、散落物及其缩放对象保留未知可选属性，电梯/传送替换乘员状态时保留其扩展；参见 `SpaceCruise` 的 `--space-unknown-fields` 回归。

## 尚未完整覆盖

- 根、世界和地形固定样本只覆盖列出的代表性字段，不证明所有运行场景与扩展都兼容；SQLite 外层 Schema 版本不能自动保护未审查的内部布局。工作区其他任务的火焰地块字段没有纳入本提交的固定样本。
- `Ex_ModData_MemoryPackable` 经泛型二次封装、反射和运行时 MOD 的载荷不会全部出现在 Roslyn 直接调用报告中，MOD 自定义 codec 及食物扩展机制的不透明字节由扩展自行维护版本。装备 Union 等 Gameplay 类型通过离线生成器检查，尚缺独立固定样本和真实 Unity 重建回归。
- 网络、天气、空间 JSON 的值类型内部、运行时替换对象时的未知字段、真实 Unity 场景、IL2CPP 和 Android 跨版本往返尚未完成验证。游戏中的每条保存、退出、加载、跨区重访与恢复链都需要独立场景回归。

审计入口 `audit.py` 发现当前 Unity 响应文件中可解析的模块直接调用、机器存取封装和飞船质量读取；它不是自动兼容证明。遇到未知官方状态，先增加明确的可达图和样本，再宣称覆盖。
