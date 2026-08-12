---
name: flatworld-test-automation
description: "Use when validating FlatWorld Unity changes, deciding whether tests are warranted, running GameTest categories, checking compilation, or diagnosing automated test failures. Small changes stop at static checks, compilation and Console; run gameplay/full tests only for system-level changes or explicit requests."
---

# FlatWorld 测试自动化

## 门槛

- 默认不运行 Test Runner、Golden Path、多进程联机或完整测试。文档、整理、资源移动且 GUID/行为不变、小范围可静态证明的改动，只做差异/编码检查、相关程序集编译和 Unity Console。
- 用户明确要求，或改动影响跨系统生命周期、序列化/协议、运行时主流程、确定性生成/流送、复杂 Prefab 接线时，才选择最小相关分类。
- 能在真实单机世界确定性执行的运行时玩法变更，同时使用 `flatworld-golden-path`。

## 命令

```powershell
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category AI.Smoke
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --smoke
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --test <FullTestName>
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --list-categories
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --all
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --check-encoding
```

- 分类从领域 Skill 选择；`Smoke` 保持每领域关键检查，真实启动/入世/Chunk/退出只由 `Runtime.GoldenPath` 负责。
- Actor JSON 静态覆盖在 `AI.Smoke`，MOD Actor DTO/Lua 外壳覆盖在 `Modding.Smoke`；真实加载覆盖在 `Runtime.GoldenPath`。
- 历史 GBK 可先预览 `scripts/normalize_source_encoding.ps1`，确认后才 `-Apply`。

## 失败与视觉

- 先修编译，再按堆栈定位生产代码；禁止删除测试、弱化断言或改输入制造通过。
- 向用户按根因汇总主要失败、阶段和影响，不只给 JSON。
- 随机/时间/地图测试注入确定输入；结束后清理临时存档、对象、端口和进程。
- 只有布局、颜色、动画、粒子、Shader 或相机最终观感变化才做定向视觉检查；优先打开 Golden Path 已生成的 Game 截图，不改变用户 Editor 布局或 Play Mode。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
