---
name: flatworld-gameplay-bug-hunt
description: "通过 GamePlayMCP 探索 FlatWorld 的未知逻辑和视觉 Bug；单个已知问题见 flatworld-fix-loop。"
---

# FlatWorld 探索式调试

探索链路：`进入隔离世界 → observe → 有界玩法动作 → observe → 定向截图/Console → 复现现场 → 修复 → 原步骤复测`。协议与控制生命周期见 [GamePlayMCP](../flatworld-gameplay-mcp/SKILL.md)。

## 观察对象

- 权威状态：移动、区块流送、拾取/交互、战斗、手持与库存、建造、输入锁和 UI 状态。
- 视觉：地图/实体显示、遮挡、动画、UI 布局、Shader、粒子和排序；截图用于这些结构化数据未覆盖的表现。
- 异常信号：新异常堆栈、动作结果与实际状态矛盾、持续卡滞、合法行为失败、无来源输入锁，或画面与状态不一致。

## 诊断边界

现场包含动作参数、前后状态及本轮日志，视觉问题附截图；历史日志与新增异常分开判断。正常玩法无法通过协议表达时属于 `capability_gap`，扩展复用生产 API 的通用能力。探索和修复范围由用户目标决定，不按问题数量凑改动；用户当前 Play Mode 按项目规则处理。
