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
5. 使用隔离存档、固定种子和带超时的条件等待。禁止真实输入、截图判断、无界等待、随机重试和静默跳过。
6. 保留原有断言；失败时修复生产代码或测试接线，不得放宽断言制造通过。
7. 执行受影响领域的 Smoke 分类，然后执行：

   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path
   ```

8. 最终总结中说明新场景所在阶段和黄金路径结果。

## 允许不扩展的情况

纯 Editor 工具、纯视觉布局、不参与运行时的数据整理，或必须依赖不可确定外部服务的功能可不加入。必须在最终总结中给出具体原因，并仍保留领域级自动测试。
