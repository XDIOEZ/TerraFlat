# FlatWorld 黄金路径扩展图

## 权威文件

- 主流程：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathCommand.cs`
- 场景编排：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathScenarios.cs`
- 复杂领域场景：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathScenarios.<Subsystem>.cs`
- 测试入口：`.agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path`
- 配置模型：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathConfiguration.cs`
- 生产系统执行器：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathExecutor.cs`
- 地图生成临时入口：`Assets/5_Scripts/5-3_GamePlay/World/Map/WorldGenerationRuntimeHooks.cs`

`--golden-path` 通过 Editor 状态机从真实 `GameStartScene` 启动；功能场景以上述 Editor 文件为唯一权威入口，不要在 Unity Test Framework 测试中再复制一套。
命令在接管每个请求前强制刷新 AssetDatabase，确保 Editor 失焦或关闭 Auto Refresh 时也不会运行旧脚本。
进入真实启动场景前会临时启用标准 Domain Reload，并在结束后按磁盘 `EditorSettings.asset` 的原值恢复，避免前一个 PlayMode 测试留下静态单例或事件。

可用 `--golden-config <partial.json>` 或重复的 `--golden-set section.field=<json-value>` 构造本次场景。结果 JSON 的 `configuration` 保存合并、验证后的完整快照；复现失败时直接复用该对象。临时配置只能调整隔离运行时实例，禁止用 Editor API 写 Prefab、SO 或 ProjectSettings 来构造场景。

## 运行产物与审计

- 每次请求的结构化结果位于 `Library/FlatWorldSkillTests/golden-result-<request-id>.json`。不能只看终端 `PASS`；必须打开该文件，确认 `state=completed`、`outcome=Passed`、`failed=0` 且 `failures` 为空。
- 主流程监听本次 Play Mode 的 `Error`、`Exception` 和 `Assert`，出现时会把错误消息与堆栈写入结构化失败。运行前用 Unity MCP 清空 Console，运行后用 MCP `read_console` 读取错误详情；不要扫描完整 `Editor.log`。MCP 断线时先重连正确的 Unity 实例。
- 三张截图位于 `Library/FlatWorldSkillTests/GoldenPathCaptures/<request-id>/initial.png`、`middle.png` 和 `final.png`，并通过结果 JSON 的 `screenshotPaths` 返回。Agent 必须逐张实际打开，检查黑屏、空白、纯色、紫色材质、错误遮罩，以及玩家、地形、主要 HUD 和阶段变化是否正常。
- 截图审计只补充视觉覆盖，不替代运行时状态断言。结构化结果、运行时报错检查或任一截图审计失败时，都应诊断、修复并重新执行完整黄金路径。

## 阶段选择

| 扩展阶段 | 适用行为 | 典型断言 |
| --- | --- | --- |
| `OnWorldReady` | 玩家模块、初始 Buff、背包、装备、建筑、可确定生成的 AI | 模块存在，公开 API 改变运行时状态 |
| `OnTraversalTick` | 需持续观测的 Buff Tick、体力、环境、AI 追踪 | 运行阶段按 Editor 帧驱动，效果在玩家移动时逐步发生 |
| `OnChunkReady` | 跨 Chunk 玩法、一次性阶段断言 | 目标 Chunk Ready，玩法状态与 Chunk 字典同时健康 |
| `BeforeWorldExit` | 存档捕获、长时状态、退出前结果 | 权威状态已进入存档根或结果已完成 |
| `Cleanup` | 会影响后续或留存的测试状态 | 通过和失败路径都恢复速度、生命、Buff 与临时对象 |

若功能必须验证“退出后重进世界”，应显式扩展主流程的重进阶段，不得用内存状态假装完成存档往返。

## 场景组织约定

- 阶段方法只做编排，具体功能用子方法和私有静态状态实现。
- 每个生产功能保留一个代表性完整场景；边界组合留在领域 Smoke 测试。
- 场景使用真实 Prefab、管理器和公开生产 API；不复制生产算法，不用反射直接改私有字段。
- 等待运行时结果时记录截止时间，在后续 Tick 检查；禁止 `Thread.Sleep`、忙等待或阻塞 Editor 主线程。
- 对组件、配置或入口缺失直接抛出带业务语义的异常；禁止因为功能缺失而静默返回伪装通过。
- 会影响后续场景的速度、生命、Buff、物品或生成对象必须在断言后恢复；需跨阶段保留的状态要有明确的最终清理。

## 当前玩法场景

| 场景 | 安排阶段 | 观测与断言 | 清理 |
| --- | --- | --- | --- |
| 燃烧 Buff | 首次 `OnTraversalTick` 通过 `BuffManager.AddBuff(BurningBuffIds.Burning)` 施加 | 后续移动 Tick 观察玩家生命下降；`OnChunkReady` 确认定义仍注册且至少发生一次 Tick | 移除燃烧并恢复测试前生命值 |
| 有限世界右边界环绕 | `OnWorldReady` 后、首次截图和长距离移动前，将真实玩家移动到右边界并通过 `Mover.Move` 越界 | 等待规范目标 Chunk Ready，验证环绕余量、玩家数据、Chunk 字典和地图存档键；恢复原位置并再次等待原 Chunk Ready | 通过和失败路径都恢复位置、速度与移动速度 |
| WorldModel 河流、冲积平原与草地表现 | `OnWorldReady` 先核对 `world.noiseScale` 已进入纯 Profile 及河流距离倍率，再扫描已绑定模型区块；后续在每次 `OnChunkReady` 累积观察 | 断言世界坐标缩放接线、气候层、淡水 `riverDepth`、高度汇流 `riverFlow`、低坡冲积层 `riverFloodplain` 与草状态存在，并确认对应 Ground/Grass Tilemap 已实际写入 Tile | 仅清空场景累计状态，不修改生产世界 |
| GM 自定义玩家移速倍率 | `OnWorldReady` 通过真实 `PlayerAdminController.TrySetAdminMoveSpeedMultiplier` 输入 `2.75x` | 断言管理员倍率与 `Mover.Speed.MultiplicativeModifier` 同步变化，且其他乘法修饰保持不变 | 立即恢复原管理员倍率；失败清理路径再次兜底恢复 |
| GM 自定义区块加载倍率 | `OnWorldReady` 通过真实 `ChunkMgr.TrySetChunkLoadSpeedMultiplier` 输入 `2.5x` | 断言公开倍率与实际每帧队列预算、并发上限同步提升 | 立即恢复原倍率；失败清理路径再次兜底恢复 |

## 示例：燃烧 Buff

1. 按 `flatworld-buff` 定位真实 Buff 注册和添加 API。
2. 在首个合适的 `OnTraversalTick` 记录玩家生命、Buff 层数和截止时间，然后通过公开 API 施加燃烧；用私有状态确保只施加一次。
3. 后续 `OnTraversalTick` 只观测 Tick 结果，不阻塞原有移动。
4. 在后续 `OnChunkReady` 断言 Buff 已注册、至少产生一次 Tick 效果，且 Chunk 窗口仍健康。
5. 在 `Cleanup` 移除测试 Buff 并恢复生命，通过和失败路径都执行。

不要在 Skill 中硬编 Buff ID 或伪造 API；每次从当前生产实现获取真实入口。
