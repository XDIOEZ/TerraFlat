---
name: flatworld-test-automation
description: "Use when validating FlatWorld Unity changes, deciding whether tests are warranted, running GameTest categories, checking compilation, or diagnosing automated test failures without screenshots or interactive Unity test-tool calls. Ordinary cleanup and small changes stop at static checks, compilation, and necessary Console checks; run gameplay/full tests only for system-level major changes or explicit user requests. Keywords: GameTest, Smoke, run tests, regression, Unity Test Framework, test category."
---

# FlatWorld 测试自动化

优先执行确定性测试脚本。不要用截图、视觉模型或交互式测试卡片验证编译、资源路径、Prefab 组件、数据、状态机或纯逻辑行为。

## 测试触发门槛
默认不运行 Unity Test Runner、`Runtime.GoldenPath`、多进程联机验证或实机/完整测试。普通整理和小修改只执行静态检查、相关程序集编译与必要的 MCP Console 错误检查。
普通改动包括：
- 文件夹整理、资源或脚本移动/改名，且 GUID、序列化结构和运行行为不变。
- 文档、注释、格式、命名、编辑器工具布局或局部配置修正。
- 影响范围明确、可由引用扫描、静态诊断和编译充分验证的小改动。
仅在以下任一条件成立时运行 Test Runner、Golden Path、实机或完整测试：

## 运行测试
测试分类保持精简边界：
- `Smoke` 是跨领域精简集合，每个子系统最多保留一个最关键检查；`<Domain>.Smoke` 用于单独运行该领域的同一个检查。
- `Runtime.GoldenPath` 单独负责真实启动、创建世界、玩家生成、Chunk 流送、保存退出；不得把这条重流程复制进快速 `Smoke`。
- 不再保留通用的 `<Domain>.Unit` / `<Domain>.Contract` 回归脚本；确有长期价值的专项验证使用已有明确分类，避免重新膨胀全局冒烟。
1. 先确认改动达到“测试触发门槛”，再从当前领域 Skill 的“修改后自动测试/验证”段选择最小相关分类。
2. 修改了可在真实单机世界执行的运行时玩法时，同时使用 `$flatworld-golden-path`，主动扩展完整流程场景。
   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category AI.Smoke
   ```
```powershell
# 运行精简冒烟：每个子系统一个关键检查
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --smoke

# 只检查全部 C# 的 UTF-8 编码与非法替换字符
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --check-encoding

# 默认黄金路径：启动游戏、代码创建世界、执行玩法场景并验证 Chunk 流送
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path

# 临时局部配置，或重复 --golden-set 覆盖单个配置字段
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py `
  --golden-path `
  --golden-config .agents/skills/flatworld-golden-path/references/wrapped-river-fast.json

# 同时运行多个相关分类
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py `
  --category Combat.Smoke --category Audio.Smoke

# 精确运行一个测试
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py `
  --test FlatWorld.GameTest.Combat.CombatSmokeTests.HitSlowdownReducesAndRestoresMoverSpeed

# 查看项目中声明的全部 Category
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --list-categories

# 仅在跨系统改动确实需要时运行整个 FlatWorld.GameTest 程序集
python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --all
```

## 处理失败
- 先阅读脚本打印的失败测试、消息和堆栈，再定位生产代码。
- 测试结束后必须直接向用户反馈主要错误：按根因合并重复/连锁项，优先用文字列出最影响流程的 3～5 项及其阶段、影响和建议修复方向；错误较少时全部列出，错误较多时同时报告总数。不得只给结果 JSON 或要求用户自行打开文件查错。
- 禁止删除测试、弱化断言或改写输入来制造通过。
- 编译失败时先修复编译；不要让旧程序集的测试结果冒充新代码验证。
- 若测试分类不存在，运行 `--list-categories` 并更新领域 Skill 的分类记录。
- 历史 GBK 源码可先预览并无损转换：`powershell -File .agents/skills/flatworld-test-automation/scripts/normalize_source_encoding.ps1`，确认列表后追加 `-Apply`；脚本只转换能按 CP936 字节往返的 `.cs`。

## 视觉验证边界
仅当任务实际改变布局、颜色、动画、粒子、Shader 最终观感或相机呈现，且这些结果无法由对象、组件、材质和参数断言覆盖时，才在自动测试通过后做一次针对性的视觉检查。普通代码、数据、Prefab 接线和回归测试不得调用视觉模型。
需要视觉检查时只获取游戏渲染画面，不截取 Unity Editor、整个应用窗口或操作系统桌面：
1. `Runtime.GoldenPath` 已在运行时通过 `ScreenCapture` 生成完整 Game 画面。优先从结果 JSON 的 `screenshotPaths` 读取 `initial/middle/final`，再用 `view_image` 打开；不得为同一轮验证额外操作 Unity 窗口截图。
2. 没有现成截图、且确实需要临时查看当前游戏画面时，使用 `manage_camera(action="screenshot", capture_source="game_view", camera="MainCamera", include_image=True, max_resolution=512)`。明确指定 `game_view`，不得改用 `scene_view` 或桌面截图作为玩法画面。
3. 截图前后保持用户当前 Editor 布局、Game View 缩放、宽高比、分辨率、停靠状态和 Play Mode 状态不变。禁止额外点击或调用菜单、快捷键、窗口管理命令来聚焦、弹出或最大化 Game View；禁止为了截图暂停、停止或重启用户正在进行的游戏。
4. 若 Game 画面无法在不改变上述状态的前提下取得，停止视觉检查并报告原因；不得回退到放大窗口、全屏桌面截图或重新排布 Editor。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-11：`Quest.Smoke` 增加四个 `debugOnly` 测试任务的清单、手动接取、代表性目标与多阶段契约；`EffectsTools.Smoke` 扩展 F4 GM 任务分页公共 API/无独立轮询检查，`quest.progression` 保护普通入世不自动接取调试任务。
- 2026-08-11：`UI.Smoke` 增加任务追踪 Prefab、右上锚点、条目进度条、输入穿透、Player 挂载及无轮询契约；既有 `quest.progression` 同步断言追踪 HUD 入世显示与任务完成后移出，稳定 Golden 操作 ID 不变。
- 2026-08-11：Golden Path 默认配置启用全部 24 个接口化真实操作，`GOLDEN_OPERATION_IDS` 新增 `quest.progression`；JSON 支持全量减项或系统白名单，运行前验证稳定 ID，结果回显实际操作集合。单项操作异常隔离后继续同阶段其他系统；运行期警告按消息聚合写入 `warnings`，脚本直接显示主要 5 类及次数。
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-11：Golden Path 默认配置启用全部 23 个接口化真实操作；JSON 支持全量减项或系统白名单，运行前验证稳定 ID，结果回显实际操作集合。单项操作异常隔离后继续同阶段其他系统；运行期警告按消息聚合写入 `warnings`，脚本直接显示主要 5 类及次数。
- 2026-08-11：Golden Path 新增 JSON 字段 `execution.errorCollectionSeconds`；首个运行时错误不再立即退出，而是在可行范围内继续覆盖并收集去重错误，计时结束后统一写入结构化失败；Agent 随后必须按根因文字反馈主要 3～5 项错误，不能只交付 JSON。
- 2026-08-11：裸跑 `--golden-path` 默认启用已验证的 WorldModel 强化水文参数，并新增帧后移动驱动、整数 Chunk 休眠环带与静止 AI 探针；`wrapped-river-fast.json` 仍用于缩小世界的快速完整回归。
- 2026-08-11：Golden Path Editor 桥接在接管请求时把 Addressables Play Mode 切到 Fast Mode，避免实机流程复用旧 Bundle；之后仍按原流程刷新 AssetDatabase、编译并进入标准 Domain Reload。
- 2026-08-10：UI 常驻轮询、通用按钮 DOTween、`BasePanel` 共享层级快照、虚拟光标按需射线及滚动焦点帧末合并改造完成 UTF-8、差异与 Unity 编译检查；新增 `UI.Smoke` 静态契约、快照复用和同 ScrollRect 最后目标回归，`UI`、`GamePlay`、`FlatWorld.GameTest` 相关程序集定向编译通过，独立 batch 日志确认本次文件无诊断，整项目仍只被既有 `GMReflectionConsole` 三处错误阻断；按项目默认未运行 Test Runner。
- 2026-08-10：区块阴影帧末合并、延迟缩槽和动态 View 池完成 UTF-8、差异与 Unity 定向编译检查；`GamePlay.dll` 已更新且本次文件无诊断，整项目仍被既有 `GMReflectionConsole` 三处错误阻断；按用户要求未运行 Test Runner/Golden Path。
- 2026-08-10：幽灵接触伤害改为复用 `Mod_Damage`，移除专用 `GhostContactDamage`，并将根触发盒保持为 `0.6×0.9`；同步更新 `AI.Smoke` 组件/参数回归断言，按局部 Prefab/战斗配置改动只完成静态诊断、Unity 编译和 Console 检查，未主动运行 Test Runner。
- 2026-08-10：矿洞死亡返回地表修复完成 GamePlay/Automation 静态编译，并扩展黄金路径的矿洞到主世界复活路由断言；按项目默认未主动运行 Test Runner 或完整 Golden Path。
