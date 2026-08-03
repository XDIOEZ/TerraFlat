---
name: flatworld-test-automation
description: "Use when validating FlatWorld Unity changes, running GameTest categories, checking compilation, or diagnosing automated test failures without screenshots or interactive Unity test-tool calls. Keywords: GameTest, Smoke, run tests, regression, Unity Test Framework, test category."
---

# FlatWorld 测试自动化

优先执行确定性测试脚本。不要用截图、视觉模型或交互式测试卡片验证编译、资源路径、Prefab 组件、数据、状态机或纯逻辑行为。

## 运行测试

1. 从当前领域 Skill 的“修改后自动测试/验证”段选择最小相关分类。
2. 执行：

   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category AI.Smoke
   ```

3. 若 `python` 不在 PATH，先获取 Codex 工作区依赖，再用返回的 Python 绝对路径执行同一脚本。
4. 根据进程退出码和结构化失败信息处理结果；不要仅根据 Unity 进程退出码判断测试通过。

常用调用：

```powershell
# 同时运行多个相关分类
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py `
  --category Combat.Smoke --category Audio.Smoke

# 精确运行一个测试
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py `
  --test FlatWorld.GameTest.Combat.CombatSmokeTests.RequiredEntryPointsAndAssetsExist

# 查看项目中声明的全部 Category
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --list-categories

# 仅在跨系统改动确实需要时运行整个 FlatWorld.GameTest 程序集
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --all
```

脚本自动选择执行通道：

- `FlatWorld.GameTest` 当前是 PlayMode 测试程序集，脚本默认使用 `PlayMode`；只有拆出 Editor-only 测试程序集后才显式传 `--mode EditMode`。
- Unity Editor 已打开：向 `FlatWorldSkillTestBridge` 写入请求，由当前 Editor 内的 TestRunner 直接执行。
- Unity Editor 未打开：定位项目声明的 Unity 版本并使用 batchmode 执行。
- 两种通道都把机器可读结果写入被 Git 忽略的 `Library/FlatWorldSkillTests/`。
- Editor 桥接通道在 PlayMode 前后保存并按字节恢复已知易变字体资源，避免动态字形写回污染 Git；不主动切换或保存用户场景。

退出码：`0` 表示全部通过；`1` 表示存在测试失败；`2` 表示没有匹配测试或请求无效；`3` 表示超时、编译失败或执行基础设施错误。

## 处理失败

- 先阅读脚本打印的失败测试、消息和堆栈，再定位生产代码。
- 禁止删除测试、弱化断言或改写输入来制造通过。
- 编译失败时先修复编译；不要让旧程序集的测试结果冒充新代码验证。
- 若测试分类不存在，运行 `--list-categories` 并更新领域 Skill 的分类记录。

## 视觉验证边界

仅当任务实际改变布局、颜色、动画、粒子、Shader 最终观感或相机呈现，且这些结果无法由对象、组件、材质和参数断言覆盖时，才在自动测试通过后做一次针对性的视觉检查。普通代码、数据、Prefab 接线和回归测试不得调用视觉模型。
