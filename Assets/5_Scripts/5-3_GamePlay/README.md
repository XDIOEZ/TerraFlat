# GamePlay 脚本目录

`GamePlay.asmdef` 覆盖本目录全部运行时代码。一级目录只表达稳定领域边界，原有功能目录保留为第二层，避免把脚本按类型或历史批次重新打散。

| 一级目录 | 职责 | 二级目录 |
| --- | --- | --- |
| `Core` | 启动、生命周期、全局服务、配置与事件 | `Manager`、`Config`、`Difficulty`、`GameEvents`、`Progress` |
| `World` | 世界生成、流送、维度、导航、环境与建筑 | `Map`、`Chunk`、`WorldModel`、`Building`、`Dimension`、`PathFinding`、`Space`、`Time` |
| `Entities` | Item/Module 实体及其行为系统 | `Item`、`AI`、`Buff`、`Combat`、`Move`、`Skill`、`Spawner` |
| `Items` | 玩家持有物、容器、制作和装备玩法 | `Inventory`、`Crafting`、`Equipment`、`Food`、`Tool` |
| `Player` | 玩家输入、交互与管理控制 | `Controller` |
| `Presentation` | 玩家可见/可听的表现适配 | `UI`、`Dialogue`、`Guide`、`Audio` |
| `Extensibility` | MOD、Lua 与扩展内容运行时 | `Mods` |
| `Development` | 运行时调试与 GM 工具 | `Debug` |

新增脚本时优先放入已有二级领域目录。只有职责足够稳定且至少包含一组相关脚本时才新增一级目录；不要重新建立含义模糊的根级 `Manager`、`Controller`、`Tool` 或 `Misc`。

## 程序集边界

- 根目录 `GamePlay.asmdef` 暂时承载仍有双向依赖的主体运行时代码。
- `Presentation/Dialogue`、`Presentation/Guide` 和 `Development/Debug` 各自拥有叶子程序集，只能依赖主体程序集，主体代码不得反向引用它们。
- 新增 asmdef 前必须先确认依赖方向；不要直接按照一级目录机械拆分，以免形成程序集循环引用。
