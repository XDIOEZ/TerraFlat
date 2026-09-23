---
name: flatworld-fix-loop
description: "Use when 用户要求针对已知 FlatWorld Bug 循环 DEBUG/FIX：真实 Play Mode 复现、修复、复测直到解决；发现异常先暂停 Unity 并保留现场。开放式探索用 flatworld-gameplay-bug-hunt。"
---

# FlatWorld 循环 FIX

针对用户指出的单个 Bug，通过真实 Play Mode 复现、修复和复测直到解决。主动探索未知 Bug 使用 `flatworld-gameplay-bug-hunt`；用户明确不要运行时复测时，遵从其要求。

## 开始与基线

- 读取 `AGENTS.md`、`flatworld-gameplay-mcp` 和 Bug 所属领域 Skill；检查 Git 状态并保留已有修改。默认用隔离存档。
- 复现条件不清时先运行并记录基线；步骤明确时可先定位修复。记录步骤、最后动作及参数、关键状态、相关 Console；视觉问题保存截图。
- 只改根因和必要观测。Debug 只用于观察，避免刷屏；复用现有诊断接口。不得用测试专用作弊绕过正式玩法链，也不直接写 Transform、血量、库存或私有字段来伪造结果。

## 发现异常：先暂停现场

复现或复测中出现目标相关的新 Error、Exception、Assert、Warning，或状态/画面结果不符时：

1. 若仍在 Play Mode，先读 `mcpforunity://instances`，用 `set_active_instance` 选中本任务的 `Name@hash`，调用 `manage_editor(action="pause")`，再读 `mcpforunity://editor/state` 确认仍在 Play Mode 且已暂停。MCP 失败时用该实例工具栏的 Pause 并确认；不要向未确认的实例发命令。
2. 暂停前不发 `gameplay_act(action="stop")`；暂停后不调用会推进或改变现场的动作、UI、等待、恢复或退出操作。
3. 在暂停状态收集只读证据：Console 条目/堆栈、可读取的 `gameplay_observe`、最后动作参数；视觉异常补截图。暂停时读不到的信息如实记录，不为此恢复游戏。不要清 Console，也不要把运行态写进正式场景或存档。
4. 先取证再改代码。`Script Changes While Playing` 设置会影响重编译时是否继续、延后或停止 Play Mode；重编译也可能重置非序列化状态。不得假定暂停现场能跨编译保留。若设置延后编译，取证后再有序退出以应用修复；编译后检查 Editor 状态和 Console。
5. Edit Mode 或编译阶段的错误无法冻结 Play Mode：保留当前编辑器状态和日志，不为暂停而启动游戏。若 Unity 自动恢复运行，发现后立即暂停。

本冻结顺序优先于 GamePlayMCP 通用流程的“先 stop”。完成取证并准备复测后，才按当前能力清理遗留输入。

## 修复与复测

1. 根据证据找根因并做最小修复；仅在必要时添加低噪声、可判定的 Debug。移除一次性日志，保留有长期价值的诊断。
2. 按 `flatworld-gameplay-mcp` 的当前能力进入真实 Play Mode：`capabilities → session(status) → continue_save(isolated=true，必要时) → control(acquire) → observe`。短动作执行，每个关键动作后重新观察，严格重放原步骤。UI 操作走 `gameplay_ui`，截图只用于视觉判断。
3. 编译与 Console 是实机前的门禁，不单独代表验收。脚本重编译、Domain Reload 或重新进入世界后，重新执行 `status → acquire → observe`。
4. 每轮检查原问题、权威状态、相关 Debug/Console；视觉问题看截图。仍有问题就更新假设再循环，证据未变时不重复同一修改。首次通过后完整重放一次，并检查相邻状态。

## 完成标准与汇报

原复现步骤不再触发、关键状态正确、相关 Console 无新错误；视觉问题有截图证据；临时 Debug 已清理或整理为长期诊断。结束时汇报根因、实际修改、复测步骤及结果、截图/Console 证据和遗留问题。
