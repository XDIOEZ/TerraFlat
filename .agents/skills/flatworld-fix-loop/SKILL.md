---
name: flatworld-fix-loop
description: "针对已知 FlatWorld Bug 的复现、根因修复与循环复测；开放式探索见 flatworld-gameplay-bug-hunt。"
---

# FlatWorld 已知 Bug 修复

用户同时要求“启动 Lua 测试 + Debug 循环”时，按 [GameDeveloperLoop 的 Lua 测试驱动模式](../game-developer-loop/SKILL.md) 执行：以 Lua 测试报告复现、修复、补强断言并循环复测，**不通过 GamePlayMCP 让 AI 游玩**。以下 MCP 现场取证仅用于其他调试模式。

## 复现与证据

最小现场包括原复现步骤、动作参数、前后权威状态、相关 Console 堆栈及必要截图。逻辑证据来自生产代码和 GamePlayMCP，视觉证据来自实际 Game View。编译和无报错不等于问题已消失。

## 现场生命周期

- 用户正在使用的 Play Mode 保持运行，按项目规则只做静态修改；冻结或重启仅适用于已授权由 AI 控制的测试会话。
- 已授权冻结现场时，确认目标实例后暂停并确认状态；取证前不 stop、不清日志、不推进动作。暂停中无法读取的信息保留未知。
- 重编译能重置非序列化状态，`Script Changes While Playing` 也影响现场保留；现场证据先于编译。编辑态错误没有可冻结的运行现场。

## 修复与复测

修复作用于正式规则和调用契约，诊断接口只提供观察。隔离存档、控制租约及动作入口见 [GamePlayMCP](../flatworld-gameplay-mcp/SKILL.md)。复测重放原步骤并比较同一组状态；Domain Reload 或换世界后租约重新获取。循环范围及是否运行复测由用户请求确定。
