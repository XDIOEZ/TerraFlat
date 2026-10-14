# 全局游戏事件测试

- `rules.lua`：调用真实 `DayScheduleGameEventTrigger.Validate/CollectOccurrences`。覆盖单次/每日/隔天日程、跨多日推进、边界尚未到时、加载不回放过去、已评估日去重、概率 0/1、日内时间归一化及非法配置。
- 夹具：独立参数、事件定义、时钟快照和 `GameEventProgressSaveData`，不启动正式全局管理器、不生成生物或修改正式世界事件状态。
- 缺口：冲突组/冷却/oncePerWorld、地面物停留触发、条件与行动组合、跨帧 Lua 子行动、维度挂起/恢复、坏文件隔离、真实生物预算与联机通知。
- 当前状态：日历规则探针已编排，本轮未执行。
