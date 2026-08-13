---
name: flatworld-golden-path
description: Continuously evolve FlatWorld's real single-player Runtime.GoldenPath after deterministic runtime gameplay changes; add bounded observable scenarios with cleanup and run the complete path after required subsystem tests.
---

# FlatWorld Golden Path

## 必读与入口

- 完整读取 `references/golden-path-map.md`、本次领域 Skill、生产 diff。
- 入口：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathCommand.cs` 与 `FlatWorldGoldenPathScenarios*.cs`。

## 何时扩展

- 新行为能在标准单机世界经公开生产 API 确定性执行时，添加/更新场景；纯 Editor、纯静态视觉、数据整理或依赖外部服务可不扩展，并说明原因。
- 先完成生产代码和达到门槛的领域 Smoke，再运行完整 Golden Path。

## 场景契约

- 实现/适配 `IFlatWorldGoldenPathOperation`，使用稳定 `Id/SystemId`；挂到现有生命周期阶段，复杂逻辑放对应 subsystem partial。
- 生命周期为“安排 → 跨 Tick 观察 → 断言 → 恢复”。使用隔离存档、固定种子、公开 API、可观察状态和有界超时。
- 禁止真实输入、阻塞 Editor、随机重试、无界等待、截图代替状态断言或静默跳过。
- 若真实流程会销毁并重建玩家（例如维度往返），必须在其它玩家场景初始化前执行，并从 `ItemMgr.User_Player` 重新绑定玩家、Mover、ChunkLoader 与执行器配置；旧 Unity 对象引用不得继续传给后续操作。
- 保留既有断言；失败时修生产代码或接线。每次重跑前必须有新证据/修复，直到完整通过或遇到项目外阻塞。
- 真实启动资源阶段断言四个本体 Actor 已注册且 Addressable Sprite/AnimatorController 非空。
- 配置由版本化 `FlatWorldGoldenPathConfiguration` 强校验并回显；操作间依赖用白名单/配置显式表达。

```powershell
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path --golden-config .agents/skills/flatworld-golden-path/references/production-surface.json
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path --golden-set world.radius=64
```

## 结果审计

1. 打开输出的 `Library/FlatWorldSkillTests/golden-result-<id>.json`，要求 completed、Passed、failed=0、failures 为空。
2. 审查本轮 Console error/warning；按根因处理，只有证明确实无害的既有警告可保留并说明。
3. 从 JSON 打开且实际查看 `initial/middle/final.png`；确认非黑屏/空白/紫材质，玩家、地形、主要 HUD 与阶段变化合理。
4. 结构化结果、Console 或视觉任一失败都不得报告通过；截图不能替代玩法断言。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
