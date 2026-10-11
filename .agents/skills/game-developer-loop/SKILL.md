---
name: game-developer-loop
description: "GameDeveloperLoop：沿用隔离存档，通过真实游玩、截图和 Console 发现问题并迭代改进。"
---

# GameDeveloperLoop

链路：`同一隔离存档 → 游玩与观察 → 记录问题证据 → 改进 → 保存 → 授权重启 → 原场景复查`。控制、动作和 UI 协议见 [GamePlayMCP](../flatworld-gameplay-mcp/SKILL.md)。

- [Memory/存档记录.md](Memory/存档记录.md)：隔离存档、种子、已保存进度和后续目标；未保存或未知信息分别注明。
- [Memory/UnityMCP连接经验.md](Memory/UnityMCP连接经验.md)、[Memory/性能优化经验.md](Memory/性能优化经验.md)：连接和性能诊断经验，按问题读取。
- `scripts/gameplay_client.py`、`scripts/collect_drop.py`：现有游玩与采集辅助入口。
- GM 无敌保护入口为 `gameplay_gm(command="enable_invincibility")`，是否启用按当前测试目标确定。

截图覆盖画面与交互表现，Console 和结构化状态提供逻辑证据。循环时长、截图频率及调试保护按当前请求确定，实际改进依据问题证据，不设置固定数量。用户正在操作的 Play Mode 只做静态修改，最终验收由用户完成。
