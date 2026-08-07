---
name: flatworld-golden-path
description: Continuously evolve FlatWorld's real single-player Runtime.GoldenPath test after runtime gameplay changes. Use whenever modifying player/world lifecycle, buffs, combat, items, buildings, AI, environment, Chunk streaming, save behavior, or another feature that can be exercised deterministically after entering a real world; select the correct lifecycle phase, add a bounded scenario with observable assertions and cleanup, and run the complete golden path after subsystem smoke tests.
---

# FlatWorld 黄金路径持续演进

让完整流程测试随生产系统一起演进，覆盖真实启动、创建世界、玩家行为、Chunk 流送与退出世界。

## 必读

1. 完整读取 [references/golden-path-map.md](references/golden-path-map.md)。
2. 读取本次生产代码 diff 和命中的 FlatWorld 领域 Skill。
3. 读取 `Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathCommand.cs` 与现有 `FlatWorldGoldenPathScenarios*.cs`。

## 强制工作流

1. 先完成生产代码与领域 Smoke 测试，再评估黄金路径。
2. 只要新行为能在标准单机世界内通过公开生产 API 确定性执行，就必须添加或更新一个黄金路径场景；不要等用户另行要求。
3. 将场景挂到现有阶段：进入世界后、移动 Tick、Chunk Ready、退出世界前或统一清理。只有新功能确实需要新的生命周期边界时，才修改主编排命令。
4. 用命名清晰的状态化子场景实现“安排行为 → 跨 Tick 观测 → 断言 → 恢复状态”。回调不得阻塞 Editor 主线程；复杂场景拆到 `FlatWorldGoldenPathScenarios.<Subsystem>.cs` partial 文件。
5. 使用隔离存档、固定种子和带超时的条件等待。禁止真实输入、用截图代替可观察状态断言、无界等待、随机重试和静默跳过。
6. 保留原有断言；失败时修复生产代码或测试接线，不得放宽断言制造通过。
7. 执行受影响领域的 Smoke 分类，然后执行：

   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path
   ```

   黄金路径采用“配置 + 执行器”契约。需要构造特定场景时，优先传临时局部 JSON；也可重复使用 `--golden-set` 调整单个字段。配置只改变本次隔离世界中的真实运行时实例，不得改写 Prefab 或项目默认值：

   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path --golden-config .agents/skills/flatworld-golden-path/references/wrapped-river-fast.json
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path --golden-set world.radius=64 --golden-set player.maximumMoveSpeed=40.0 --golden-set player.screenshotOrthographicSize=24.0
   ```

   AI 可以按本次目标临时配置世界种子/尺寸/Chunk、拓扑、玩家视野与移动、启用的子场景、河流生成参数、超时和覆盖量门槛。新增可调系统参数时，必须同时更新 C# 配置默认值、Python 默认字典、验证、执行器应用逻辑、结果回显和示例；未知字段或类型不匹配必须在启动 Unity 场景前失败。

8. 按下方“运行结果审计”检查结构化结果、运行时报错和全部截图；不得只看到终端 `PASS` 就结束。
9. 最终总结中说明新场景所在阶段、黄金路径结果、运行时报错检查结论和截图检查结论。

## 最多三次规则

- 每个已经完成并进入验收的独立系统或修改批次，单独拥有最多 3 次 Golden Path 执行机会；计数不按对话累计，也不跨系统、跨修改批次继承。
- 同一批次因第 1 或第 2 次失败而进行的修复仍属于该批次，不重置计数。
- 开始另一个系统，或用户在上一批次结束后要求一轮新的独立修改时，重新获得最多 3 次机会。
- 第 3 次仍未满意或失败时立即停止重跑，提交三次结果 JSON、截图、Console 错误与不满意原因，等待用户指导；未经用户明确授权不得执行第 4 次。

## 运行结果审计（强制）

每次执行 `--golden-path` 后都必须完成以下检查：

1. 打开终端输出指向的 `Library/FlatWorldSkillTests/golden-result-<request-id>.json`，要求 `state` 为 `completed`、`outcome` 为 `Passed`、`failed` 为 `0`，并且 `failures` 为空。退出码为 `0` 或终端显示 `PASS` 不能替代这一步。
2. 明确检查运行时报错。运行前通过 Unity MCP `read_console(action="clear")` 清空 Console；运行后只通过 `read_console(action="get", types=["error"], format="detailed")` 读取 Console 窗口中的本次 `Error`、`Exception` 和 `Assert`，不要读取或扫描完整 `Editor.log`。黄金路径命令也会监听本次 Play Mode 错误并将首个错误转成结构化失败；只要结果或 MCP Console 出现错误，就必须阅读消息与堆栈、修复并重跑。MCP 暂时断线时先恢复/重连 Unity 实例，不用文件日志代替。
3. 从结果 JSON 的 `screenshotPaths` 读取截图路径，要求正好包含可访问的 `initial.png`、`middle.png`、`final.png`。逐张使用 `view_image` 或当前环境等效的图像查看能力实际打开，不能只检查文件存在或大小。
4. 目视确认三张图均不是黑屏、空白、纯色或紫色材质错误；真实玩家、世界地形与主要 HUD 可见；没有报错弹窗或异常遮罩；三个阶段的地形/相机画面与移动流程相符。三张图完全相同或任一图明显异常时，先诊断再重跑。
5. 截图是额外的视觉回归检查，不能替代生产状态、Chunk、存档或玩法断言。任一结构化结果、运行时报错或截图检查未通过，黄金路径都不得报告完成。
6. 最终总结至少给出结果 JSON 路径、是否确认无运行时 `Error/Exception/Assert`、三张截图是否全部目视通过，以及截图目录或三条路径。

## 允许不扩展的情况

纯 Editor 工具、纯视觉布局、不参与运行时的数据整理，或必须依赖不可确定外部服务的功能可不加入。必须在最终总结中给出具体原因，并仍保留领域级自动测试。

## 当前有限世界场景（2026-08-06）

GoldenPath 在 `OnWorldReady` 后、首次截图与原长距离流程前，通过真实 `Mover.Move` 执行一次右边界环绕；等待对侧 Chunk Ready，验证玩家数据、Chunk/MapSave 规范键，然后恢复原位置并继续完整流程。复杂状态机位于 `FlatWorldGoldenPathScenarios.WorldTopology.cs`。

## 配置执行器契约（2026-08-06）

- `FlatWorldGoldenPathConfiguration` 是版本化、强验证的输入和结果快照；运行结果必须回显最终有效配置，便于复现 AI 临时构造的场景。
- `FlatWorldGoldenPathExecutor` 是生产系统入口适配层：通过 `NewWorldCreationRequest` 创建真实世界，通过玩家模块 API 调视野，通过 `WorldGenerationRuntimeHooks` 在 `Map.Act()` 前配置当前 Map 实例。
- `player.cameraOrthographicSize` 控制移动阶段视距；`player.screenshotOrthographicSize` 仅在三次截图前临时拉远相机，通过真实 `Mod_Cam` 刷新可见 Chunk 窗口并等待全部 Ready，截图后恢复移动视距。
- `WorldGenerationRuntimeHooks` 默认没有订阅者；执行器结束或 Domain Reload 时必须解除订阅，所以临时河流/地形参数不会污染 Prefab、后续游戏或存档默认配置。
- 状态化子场景继续负责跨帧的调用、观测、断言和清理；配置决定场景是否启用及运行参数，执行器不绕过生产系统直接伪造结果。
- 快速有限世界/强化河流示例见 `references/wrapped-river-fast.json`。
