---
name: game-developer-loop
description: "GameDeveloperLoop：默认真实游玩调试；同时要求 Lua 测试和 Debug 循环时，改用 Lua 测试驱动的修复循环。"
---

# GameDeveloperLoop

## 模式选择

- 用户同时提出 **“启动 Lua 测试”与“Debug 循环”**（包括“Lua测试+Debug循环”等表述）时，**优先使用下方 Lua 测试驱动循环**。不使用 GamePlayMCP 控制 AI 游玩、移动、交互或点击 UI；`gameplay_test_pipeline` 只允许用于自动测试的启动、状态与报告查询。
- 仅要求真实游玩、探索或截图调试而未提出上述组合时，才使用下方 MCP 游玩循环。两种模式不混用；PlayMode Lua 测试由宿主定时截图、结束后统一看图，不需要 MCP 玩家操作。

## Lua 测试驱动 Debug 循环

1. 阅读 [Lua 测试 Skill](../flatworld-lua-testing/SKILL.md) 和 `Assets/StreamingAssets/GameplayTests/systems.catalog.json`，选受影响系统的规则测试；涉及真实游戏流程时补跑 `--integration`。缺少有效断言先补 Lua 测试。
2. 通过 `python Tools/GameplayTesting/run_suite.py --systems <系统名>`（或 `--changed <代码路径>`、测试中心、测试流水线）启动测试，读取实际报告、首个失败步骤及相关 Console 日志；跳过、未执行、环境阻塞不得算通过。
3. 按证据修复正式代码中的 Bug，并增强对应 Lua 断言，覆盖复现条件、正常流程及必要的边界/异常/回滚；不能靠删除、禁用或放宽测试掩盖问题。
4. 重跑失败用例和关联回归；即使已有用例全绿，也要检查测试盲区、补强必要断言后重跑。持续 **发现 Bug → 修根因 → 加强 Lua 测试 → 重测**，直到本轮目标范围的测试全部通过、相关报错消除，已修复问题有断言防回归。不能据此宣称整个游戏绝对没有 Bug；如有未覆盖或无法执行的范围须明确报告。

用户正在使用 Play Mode 时不得擅自中断，按项目规则只做静态修改，并报告未执行的验证。

## MCP 游玩循环

链路：`同一隔离存档 → 游玩与观察 → 记录问题证据 → 改进 → 保存 → 授权重启 → 原场景复查`。控制、动作和 UI 协议见 [GamePlayMCP](../flatworld-gameplay-mcp/SKILL.md)。

- [Memory/存档记录.md](Memory/存档记录.md)：隔离存档、种子、已保存进度和后续目标；未保存或未知信息分别注明。
- [Memory/UnityMCP连接经验.md](Memory/UnityMCP连接经验.md)、[Memory/性能优化经验.md](Memory/性能优化经验.md)：连接和性能诊断经验，按问题读取。
- `scripts/gameplay_client.py`、`scripts/collect_drop.py`：现有游玩与采集辅助入口。
- GM 无敌保护入口为 `gameplay_gm(command="enable_invincibility")`，是否启用按当前测试目标确定。

截图覆盖画面与交互表现，Console 和结构化状态提供逻辑证据。循环时长、截图频率及调试保护按当前请求确定，实际改进依据问题证据，不设置固定数量。用户正在操作的 Play Mode 只做静态修改，最终验收由用户完成。
