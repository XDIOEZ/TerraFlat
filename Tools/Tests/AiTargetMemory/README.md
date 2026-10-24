# 狼目标记忆离线回归

运行 `python Tools/Tests/AiTargetMemory/verify.py`，需要 .NET 8。

直接编译当前 `AI_Wolf.TargetMemory.cs`，并抽取正式 `TriggerPackAttack`、`ReceivePackCall`、`RefreshThreatTarget`；不复制记忆或入口算法。临时工程全部写入 `.codex-tmp`，不暂停、刷新或重载正在运行的 Unity。

覆盖集火/支援确认后尚未首次决策就失去视野、隐藏目标坐标冻结、异步导航等待、到点单次重感知、赶路和搜索的独立超时、重见目标、身份/代际/死亡清理及同伴报告。

这是带环境替身的生产方法回归，不是完整 Unity 编译或实机验收；真实状态切换由 `ai/wolf_target_memory` Lua 规则覆盖，设施遮挡由 `ai/passable_building_sight` 覆盖。
