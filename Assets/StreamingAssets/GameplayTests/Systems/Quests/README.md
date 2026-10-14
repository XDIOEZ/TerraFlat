# 任务与进度测试

- `rules.lua`：调用正式内建 `SignalCountObjectiveHandler`。覆盖事件/目标/维度过滤、不指定过滤器、大小写/空白规范、目标上限、负增量不倒扣和缺少事件类型的配置拒绝。
- 同脚本直接调用 `InventoryOwnsObjectiveHandler` 的真实多库存计数/去重、`QuestRewardPlan.TryCommit` 的真实库存查询和原子奖励提交、`QuestProgressStore` 的待领奖/完成/领取标记/次数往返、大小写身份和命名空间保护。空包空间不足时检查没有部分奖励。
- 夹具：独立目标与信号、未加载的临时 Player/Bag 以及纯内存库存/玩家数据。临时 Player 不进入 ItemMgr、不建立档案、不 Load；奖励信号只读返回的延迟计划，不发布全局事件，不接取真实玩家任务、不写正式存档。
- 缺口：任务目录合并、接取资格、阶段 all/any、完整运行时信号链、`PlayerQuestRuntime` 重复领取拒绝、MOD Handler 和阅读 UI。完成记录在夹具中提供初态再做生产存储往返，不声称测试了阶段自动推进。
- 当前状态：目标规则、真实奖励事务与进度存储已编排，本轮未执行。
