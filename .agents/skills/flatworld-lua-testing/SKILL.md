---
name: flatworld-lua-testing
description: "FlatWorld Lua 测试系统导航：编写与分组案例、显式关键现场截图、隔离执行、流水线及报告定位。"
---

# FlatWorld Lua 测试系统

用于编写、调整、执行 `GameplayTests/*.json` 与 Lua 测试，或修改测试夹具、截图、报告和测试中心。手动游玩与 MCP 输入控制转 [flatworld-gameplay-mcp](../flatworld-gameplay-mcp/SKILL.md)，业务根因转对应领域 Skill；测试夹具调用正式接口，不替生产代码重算规则。

## 导航

以 `Assets/5_Scripts/5-3_GamePlay/Development/Testing/` 为测试代码根：

| 入口 | 职责 |
| --- | --- |
| `GameplayTestScene.cs` | 正式建世界、隔离存档、案例调度、步骤和收尾 |
| `GameplayTestLuaRunner.cs` | Lua 沙箱、命令编排、预检、运行与断言 |
| `GameplayTestReport.cs` / `GameplayTestArtifacts.cs` | 报告普通数据、阶段截图、Markdown 汇总与结果包 |
| `GameplayTestSuite.cs` / `GameplayTestSelection.cs` | 严格 JSON、路径/案例身份与多选清单 |
| `GameplayTestShowroom.cs` / `GameplayTestGm.cs` | 可选全物品陈列、白名单 GM 前置条件 |
| `GameplayTestSpaceScenario.cs` / `GameplayTest{Universe,Ship}Probe.cs` | 真实太空现场与独立领域采样 |
| `GameplayTestMechanicalPlant.cs` / `GameplayTestFluid*.cs` | 机械与管道气液夹具 |

- 场景：`Assets/3_Scenes/Development/GameplayTestScene.unity`。
- Editor：`Assets/Editor/FlatWorld/GameplayMCP/GameplayTestPipelineCoordinator.cs` 串联 Test Framework；`GameplayTestDashboardWindow.cs` 是测试中心，`GameplayTestPipelineTool.cs` 提供 MCP 入口。
- CLI：`Tools/GameplayTesting/run_suite.py`；安全前置与集成测试分别在 `Assets/Tests/FlatWorld/{EditMode,PlayMode}/`。
- 清单与 Lua：`Assets/StreamingAssets/GameplayTests/`。命令、清单字段及运行选项见 [通用说明](../../../Assets/StreamingAssets/GameplayTests/README.md)；领域覆盖见 [太空](../../../Assets/StreamingAssets/GameplayTests/Universe/README.md)、[气液](../../../Assets/StreamingAssets/GameplayTests/Fluids/README.md)。
- 现有入口按业务系统分为机械动力、工业气液与管网、宇宙与太空三个专项；默认机械动力。独立移动、入场/陈列、背包/快捷栏、生命/无敌和 Buff 通用案例已移除，系统前置检查与 GM 造条件接口继续复用。

## 编写与截图

- 每个系统测试集保持 10 份 Lua 以内，新增检查优先并入已有主题；每个子检查保留独立中文标签和真实断言。纯数据与真实世界测试写清覆盖边界，摆放成功或调用完成不代表业务成功。
- Lua 先编排队列，C# 再按顺序执行；`test.space(operation, 回调, label, argument)` 的回调只验证采样，不在回调里追加步骤。普通失败停止本 Lua，气液组独立子案可继续收集错误但整组仍失败；`continueAfterFailure` 控制是否执行下一份 Lua。
- **AI 编写或调整 Lua 时，在重要命令之后自行选择有解释价值的现场，显式插入 `test.screenshot("中文阶段说明")`。每份 Lua 允许 0～3 次，循环展开后的实际命令数也计入上限；预检与运行都拒绝超限。** 无视觉变化的数据组可为 0 次，不为凑数截图。
- 不再自动拍脚本结尾或失败截图。已完成的截图保留，普通失败后停止后续命令；气液独立子案按继续收集规则执行。需要特定现场时，应在可执行的位置明确安排截图，不能假设失败后自动补拍。
- 真实太空 `test.space` 完成采样和 Lua 断言后先保留现场，**截图命令紧跟该 `test.space`**；截图完成后，下一个非截图命令之前或脚本结束时才恢复地表并清理。不要把截图放在验证回调内，也不要先排 `test.log` / `test.wait` 等其他命令再拍这个现场。
- 截图宿主检查主角贴图启用、透明度、相机裁剪层和视口，连续两帧就绪后再取图；8 秒仍未就绪就失败并记录原因。实际图片仍需检查主角、HUD 与光照，组件状态或图像哈希不同不能证明画面有用，不用固定延时掩盖缺失现场。

```lua
test.space("sector_transition", function(data)
    assert(data.crossed.spaceView, "没有进入太空")
end, "向右跨区", "right")
test.screenshot("向右跨区并折返后的太空现场")
```

截图与断言各有用途：截图展示玩家可见状态，数据断言证明规则；图像哈希不同不能证明内容有意义。

## 执行与范围

```powershell
python Tools/GameplayTesting/run_suite.py GameplayTests/mechanical-suite.json
python Tools/GameplayTesting/run_suite.py GameplayTests/universe-suite.json --cases space_gameplay
```

- 复用已打开且未处于 PlayMode 的 Editor；测试中心 `FlatWorld/测试/测试中心（多选案例）` 或 `gameplay_test_pipeline(run/status/report)` 也可执行。用户正在玩的 PlayMode 不停止、不暂停，只静态修改并说明本轮未运行。
- EditMode 对 FlatWorld 测试程序集全量做安全前置；全部通过后只进入一次 PlayMode，在同一隔离世界顺序执行选中 Lua。`--cases` 只筛玩法案例，原清单不改；`buildShowroom: false` 跳过全陈列。
- 一份 Lua 一个沙箱与控制租约，世界、背包和玩家位置通常延续；前置条件自行准备。隔离保存租约覆盖首次、自动和退出保存，退出写盘并返回菜单后才恢复正式存档目录，不写正式玩家档案。
- 失败先看首个失败步骤、原始采样及堆栈，再修复并重跑相关范围；用户要求循环时逐轮推进，不以删除、禁用或放宽断言换取通过。未执行、环境阻塞和视觉/联机未验收应如实区分。
- 固定步采样等待正式 `SpaceSession.State.SimulationSeconds` 达到目标，同时保留真实时间超时；慢帧或编辑器操作耗时不能计作物理推进。无外力惯性使用空星区自有夹具，同时检查位移、停推和燃料；星体引力或大气阻力范围内不能断言总速度不变。太空跨区另核验 UI 根身份、统一时间源与实际采光，防止逻辑通过但画面失效。

## 报告契约

```text
Library/FlatWorldGameplayTests/
├─ Pipeline/<运行ID>.json
├─ Runs/<运行ID>/
│  ├─ report.md / report.json / pipeline.json
│  └─ cases/<用例ID>/
│     ├─ report.json
│     └─ screenshot-01.png … screenshot-03.png（只在执行命令后生成）
└─ Saves/<本轮隔离目录>/
```

- 新报告 `schemaVersion: 5`；每用例的 `screenshots` 列表按命令顺序保存 `index/label/path/error/scene/utc/elapsedSeconds/playerPosition`。Markdown 按 Lua 与截图序号展示，流水线和 CLI 的 `screenshotPaths` 只列实际存在图片。
- 没有截图命令就没有图片；仅已尝试但无法取图的命令记录失败原因，无画面、中断或失败不补造现场。历史报告与旧图片保留原状，不自动迁移或复用上轮证据。
- `Runs/<运行ID>/` 就是测试包，不另建 ZIP；收尾保留最近 10 轮。独立 Player 使用 `Application.persistentDataPath/FlatWorldGameplayTests/`。
