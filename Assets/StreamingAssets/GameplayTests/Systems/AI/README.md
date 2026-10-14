# AI 生物测试

- `rules.lua`：直接调用 `AnimalEggLayingSchedule`。覆盖首次无资格、两天最小冷却、随机延迟上下界、配置提高冷却、成功后从实际日期重计时、非有限参数拒绝。
- 同脚本直接调用 GO 主路径的 `AIStateMachine/AI_StateMachineRunner`、`AIAdvanceStateNode`、`AI_AttackController`、`AIBehaviorGraphBlackboard`、`Mod_ItemDetector.CalculateEffectiveDetectionRadius`。覆盖节点退出/切换/进入顺序、切换拦截、重复/缺失身份保护、Reset复用、失效目标停车、到达去重与重入、前摇/伤害窗口/后摇/冷却、受击事实和来源记忆、世界计时独立及体型感知范围。
- 夹具：独立日历、状态机、攻击控制器、行为图黑板和局部回调，不生成动物、不改当前世界居民。攻击探针检查正式窗口时钟，没有伤害目标或动画器。
- 缺口：陆地/水生/飞行真实寻路、觅食消费、完整物种行为选择/目标 LOS、攻击实际伤害、蜂巢居民、远距休眠及 GO/ECS 交接。规则通过不能证明上述真实世界行为通过。
- 当前状态：仅规则探针；本轮未执行。
