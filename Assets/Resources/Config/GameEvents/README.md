# FlatWorld 游戏事件配置

运行时会按文件名顺序合并 `Definitions/*.json`。配置是纯 JSON，不使用 ScriptableObject。

关键约定：

- `event.id` 和 `action.id` 会进入存档，发布后不要改名。
- 天数从 `1` 开始；默认一天为 `1440` 游戏秒。
- `repeatEveryDays: 0` 表示只在 `minimumDay` 触发一次。
- 同一 `conflictGroup` 同时只能运行一个事件，优先级高的配置先判定。
- 单个坏文件或坏事件会被隔离，不影响其他有效配置。
- 可拆分任意数量的 JSON 文件，适合 AI 按活动独立新增和修改。

内置扩展类型：

- 触发器：`day.schedule`、`manual`、`world.item.dwell`
- 条件：`dimension.is`
- 动作：`creature.waves`、`creature.advance`、`weather.override`、`signal.emit`

`world.item.dwell` 会按物品 GUID 记录地面停留时间，拾起、销毁或切换世界会重置候选；
`creature.advance` 使用触发载荷中的目标位置，在玩家距离与活动相机视野外生成生物并下发可存档的推进命令。

全新行为通过实现并注册 `IGameEventTriggerHandler`、`IGameEventConditionEvaluator` 或
`IGameEventActionHandler` 扩展，不需要修改 JSON 数据结构。`signal.emit` 可让 UI、任务、活动系统
订阅自定义信号和载荷。
