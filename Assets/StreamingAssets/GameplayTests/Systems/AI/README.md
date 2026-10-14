# AI 生物测试

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

- `rules.lua`：直接调用 `AnimalEggLayingSchedule`。覆盖首次无资格、两天最小冷却、随机延迟上下界、配置提高冷却、成功后从实际日期重计时、非有限参数拒绝。
- 同脚本直接调用 GO 主路径的 `AIStateMachine/AI_StateMachineRunner`、`AIAdvanceStateNode`、`AI_AttackController`、`AIBehaviorGraphBlackboard`、`Mod_ItemDetector.CalculateEffectiveDetectionRadius`。覆盖节点退出/切换/进入顺序、切换拦截、重复/缺失身份保护、Reset复用、失效目标停车、到达去重与重入、前摇/伤害窗口/后摇/冷却、受击事实和来源记忆、世界计时独立及体型感知范围。
- 夹具：独立日历、状态机、攻击控制器、行为图黑板和局部回调，不生成动物、不改当前世界居民。攻击探针检查正式窗口时钟，没有伤害目标或动画器。
- `gameplay-suite.json` / `gameplay.lua`：与导航共用一套现场链。正式隔离世界中的 `Zombie` 经 `AiRuntimeBackendService.TrySpawnDirect` 完成真实 AI/移动模块加载；正式导航管理器异步请求先检查严格代价上限拒绝，再经 `TrySetAdvanceCommand` 驱动生物实际 Tick、获得路径、沿已加载可走格移动三格、到达清命令和取消旧路径。
- Lua 断言读取真实身份、模块加载、导航回调、路径结果、实际最大位移/速度、沿途可走性、目标距离和到达/取消后的状态，不能仅凭生成或命令受理算通过。到达现场显式截图，随后检查自有生物已删除。
- 夹具只创建一只自有生物，使用附近实际已加载的空地，不修改玩家速度或全局时钟。跨帧等待有真实时间上限；路径请求异常时取消，生物登记每个 Lua 的逆序收尾，断言失败和中断也清理。
- [导航目录](../Navigation/README.md) 共用这份清单，不再复制案例或重复计数。
- 缺口：水生/飞行导航、地形障碍绕行、门户长路、觅食消费、完整物种行为选择/目标 LOS、攻击实际伤害、蜂巢居民、远距休眠及 GO/ECS 交接。
- 当前状态：新增现场案例只完成静态编写；按用户要求本轮不编译、不运行、不作验收，也不停止正在运行的 PlayMode。
