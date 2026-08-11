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

- 用户明确要求运行测试。
- 改动属于系统级重大变更，例如启动/世界生命周期、存档格式、网络协议或权威边界、世界生成、核心架构/API、跨系统事务或大规模 Prefab/数据迁移。
- 改动改变真实运行时玩法主流程，且仅靠静态检查、编译和 Console 无法覆盖关键风险。

各领域 Skill 中列出的测试分类只是满足上述门槛后的候选测试集合，不代表每次修改都自动获准执行。拿不准是否达到门槛时，先完成静态验证并在总结中给出建议，不主动升级到实机或完整测试。

## 运行测试

测试分类保持精简边界：

- `Smoke` 是跨领域精简集合，每个子系统最多保留一个最关键检查；`<Domain>.Smoke` 用于单独运行该领域的同一个检查。
- `Runtime.GoldenPath` 单独负责真实启动、创建世界、玩家生成、Chunk 流送、保存退出；不得把这条重流程复制进快速 `Smoke`。
- 不再保留通用的 `<Domain>.Unit` / `<Domain>.Contract` 回归脚本；确有长期价值的专项验证使用已有明确分类，避免重新膨胀全局冒烟。

1. 先确认改动达到“测试触发门槛”，再从当前领域 Skill 的“修改后自动测试/验证”段选择最小相关分类。
2. 修改了可在真实单机世界执行的运行时玩法时，同时使用 `$flatworld-golden-path`，主动扩展完整流程场景。
3. 执行：

   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category AI.Smoke
   ```

4. 若 `python` 不在 PATH，先获取 Codex 工作区依赖，再用返回的 Python 绝对路径执行同一脚本。
5. 根据进程退出码和结构化失败信息处理结果；不要仅根据 Unity 进程退出码判断测试通过。
6. 需要检查 Unity 日志时只使用 MCP Console：运行前 `read_console(action="clear")`，运行后 `read_console(action="get", types=["error"], format="detailed")`。不要读取或全文扫描 `Editor.log`；MCP 不可用时先恢复 Unity 连接。

常用调用：

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

脚本自动选择执行通道：

- `FlatWorld.GameTest` 当前是 PlayMode 测试程序集，脚本默认使用 `PlayMode`；只有拆出 Editor-only 测试程序集后才显式传 `--mode EditMode`。
- Unity Editor 已打开：向 `FlatWorldSkillTestBridge` 写入请求，由当前 Editor 内的 TestRunner 直接执行。
- Unity Editor 未打开：定位项目声明的 Unity 版本并使用 batchmode 执行。
- 两种通道都把机器可读结果写入被 Git 忽略的 `Library/FlatWorldSkillTests/`。
- 每次测试前先扫描全部 `Assets/**/*.cs`；发现非 UTF-8 或 `U+FFFD (�)` 时一次性列出并终止，不再让 Unity 逐文件编译报错。
- 打开的 Editor 会在接管请求前同步刷新 AssetDatabase，确保失焦或关闭 Auto Refresh 时也会先编译 AI 的最新修改。
- `--golden-path` 使用隔离临时存档并只调用公开生产 API；不点击 UI、不发送物理输入。`--golden-config` 接收局部 JSON，重复的 `--golden-set section.field=<json-value>` 可做最后覆盖；未知字段和类型错误必须在运行前拒绝。
- 黄金路径运行结束后必须按 `$flatworld-golden-path` 读取结构化 JSON、检查本次 `Error/Exception/Assert`，并逐张目视 `initial/middle/final`；截图只做视觉审计，不替代状态断言。
- Editor 桥接通道在 PlayMode 前后保存并按字节恢复已知易变字体资源，避免动态字形写回污染 Git；不主动切换或保存用户场景。
- Editor 桥接通道会跨 PlayMode Domain Reload 从 `running/pending` 状态恢复请求、重新挂接 TestRunner 回调，并持久化易变资源快照；测试不依赖用户的 Enter Play Mode 设置。

退出码：`0` 表示全部通过；`1` 表示存在测试失败；`2` 表示没有匹配测试或请求无效；`3` 表示超时、编译失败或执行基础设施错误。

## 处理失败

- 先阅读脚本打印的失败测试、消息和堆栈，再定位生产代码。
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

- 2026-08-11：裸跑 `--golden-path` 默认启用已验证的 WorldModel 强化水文参数，并新增帧后移动驱动、整数 Chunk 休眠环带与静止 AI 探针；`wrapped-river-fast.json` 仍用于缩小世界的快速完整回归。
- 2026-08-11：Golden Path Editor 桥接在接管请求时把 Addressables Play Mode 切到 Fast Mode，避免实机流程复用旧 Bundle；之后仍按原流程刷新 AssetDatabase、编译并进入标准 Domain Reload。
- 2026-08-10：UI 常驻轮询、通用按钮 DOTween、`BasePanel` 共享层级快照、虚拟光标按需射线及滚动焦点帧末合并改造完成 UTF-8、差异与 Unity 编译检查；新增 `UI.Smoke` 静态契约、快照复用和同 ScrollRect 最后目标回归，`UI`、`GamePlay`、`FlatWorld.GameTest` 相关程序集定向编译通过，独立 batch 日志确认本次文件无诊断，整项目仍只被既有 `GMReflectionConsole` 三处错误阻断；按项目默认未运行 Test Runner。
- 2026-08-10：区块阴影帧末合并、延迟缩槽和动态 View 池完成 UTF-8、差异与 Unity 定向编译检查；`GamePlay.dll` 已更新且本次文件无诊断，整项目仍被既有 `GMReflectionConsole` 三处错误阻断；按用户要求未运行 Test Runner/Golden Path。
- 2026-08-10：幽灵接触伤害改为复用 `Mod_Damage`，移除专用 `GhostContactDamage`，并将根触发盒保持为 `0.6×0.9`；同步更新 `AI.Smoke` 组件/参数回归断言，按局部 Prefab/战斗配置改动只完成静态诊断、Unity 编译和 Console 检查，未主动运行 Test Runner。
- 2026-08-10：矿洞死亡返回地表修复完成 GamePlay/Automation 静态编译，并扩展黄金路径的矿洞到主世界复活路由断言；按项目默认未主动运行 Test Runner 或完整 Golden Path。
- 2026-08-10：矿洞出口首次 E 时序修复完成 GamePlay/Automation 静态编译并新增黄金路径同目标交互重试断言；按项目默认只检查编译与 Console，不主动运行 PlayerInteraction Smoke 或完整 Golden Path。
- 2026-08-09：新增 Buff 状态 HUD 的 `UI.Smoke` Prefab/Player 绑定、节点、左侧中部锚点和输入穿透契约；完成静态诊断、Prefab 导入、Unity 编译与 Console 检查，按项目默认未运行 Test Runner。
- 2026-08-09：URP 2D 光照遮挡子层完成 GamePlay/Editor 静态诊断、Unity Console 检查和 Prefab 导入检查；新增建筑黄金路径断言但按项目默认未主动运行 Test Runner。
- 2026-08-09：新增保存状态 HUD 与手动异步保存回归契约，完成相关脚本静态诊断、Prefab 导入、Unity 编译和 Console 检查；按项目默认未主动运行 UI Smoke/Golden Path Test Runner。
