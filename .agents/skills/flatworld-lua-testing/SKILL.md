---
name: flatworld-lua-testing
description: "FlatWorld Lua 测试系统导航：编写与分组案例、定时截图、统一视觉复核、隔离执行及报告定位。"
---

# FlatWorld Lua 测试系统

系统代码修改后应按本Skill选择回归范围；用于编写、调整、执行 `GameplayTests/*.json` 与 Lua 测试，或修改测试夹具、截图、报告和测试中心。手动游玩与 MCP 输入控制转 [flatworld-gameplay-mcp](../flatworld-gameplay-mcp/SKILL.md)，业务根因转对应领域 Skill；测试夹具调用正式接口，不替生产代码重算规则。

**明确指定 PlayMode Lua 时优先遵循用户范围**：用户说“测试/启动 PlayMode Lua 测试集”“只测 PlayMode”等，**仅执行 gameplay 清单的 PlayMode Lua**，不自动运行 EditMode 测试、安全前置测试或编辑态快速 Lua 规则。完整入口用 `python Tools/GameplayTesting/run_suite.py --playmode-only`；指定系统用 `--systems <系统ID> --playmode-only`；MCP 用 `gameplay_test_pipeline(action="run", suitePath="GameplayTests/all-systems-gameplay.json", playModeOnly=true)`。**不要**误用不带该标志的 gameplay 清单（默认会先执行 EditMode），也不要换成 `--systems ... --integration`（默认先跑规则）。只有用户明确要求规则、EditMode 或完整多阶段回归时才运行它们；跳过前置不等于它们通过。

**Lua 测试 + Debug 循环模式**：当用户同时要求两者时，本 Skill 的测试集是发现、复现和验收 Bug 的主链路，**不转 GamePlayMCP 让 AI 游玩**。循环执行“选择 Lua 测试 → 运行并读报告 → 修复生产 Bug → 检查覆盖盲区并补强 Lua 断言 → 重跑及扩大关联回归”，即使已有用例通过也要补强必要的边界测试，直到目标范围测试全部通过且相关报错消除；不把跳过、未覆盖或运行受阻当成无 Bug。测试流水线 MCP 可负责启动和查询自动测试，但不得代替 Lua 断言或进行玩家操作。模式入口见 [GameDeveloperLoop](../game-developer-loop/SKILL.md)。

## 导航

以 `Assets/5_Scripts/5-3_GamePlay/Development/Testing/` 为测试代码根：

| 入口 | 职责 |
| --- | --- |
| `GameplayTestScene.cs` | 正式建世界、隔离存档、案例调度、步骤和收尾 |
| `GameplayTestLuaRunner.cs` | Lua 沙箱、命令编排、预检、运行与断言 |
| `GameplayTestReport.cs` / `GameplayTestArtifacts.cs` | 报告数据、自动截图、Markdown 汇总与结果包 |
| `GameplayTestSuite.cs` / `GameplayTestSelection.cs` | 严格 JSON、执行模式、路径/案例身份与多选清单 |
| `GameplayTestSystemCatalog.cs` / `GameplayTestSystemProbe.*.cs` | 38个系统归属和生产规则白名单，Lua持有业务断言 |
| `GameplayTestRulesSuiteRunner.cs` / Editor `GameplayTestPipelineCoordinator.Rules.cs` | 编辑态直接跑规则，不启动资源/世界/PlayMode |
| `GameplayTestRuntimeProbe*.cs` / `GameplayTest*RuntimeProbe*.cs` | 隔离世界现场采样、跨帧流程与每用例收尾；对话/引导探针在各自程序集内自注册 |
| `GameplayTestShowroom.cs` / `GameplayTestGm.cs` | 可选全物品陈列、白名单 GM 前置条件 |
| `GameplayTestSpaceScenario.cs` / `GameplayTest{Universe,Ship}Probe.cs` | 真实太空现场与独立领域采样 |
| `GameplayTestMechanicalPlant.cs` / `GameplayTestFluid*.cs` | 机械与管道气液夹具 |

- 场景：`Assets/3_Scenes/Development/GameplayTestScene.unity`。
- Editor：`Assets/Editor/FlatWorld/GameplayMCP/GameplayTestPipelineCoordinator.cs` 串联 Test Framework；`GameplayTestDashboardWindow.cs` 是测试中心，`GameplayTestPipelineTool.cs` 提供 MCP 入口。
- CLI：`Tools/GameplayTesting/run_suite.py`；安全前置与集成测试分别在 `Assets/Tests/FlatWorld/{EditMode,PlayMode}/`。
- Lua 专用资源缓存：`GameplayTestResourceCache.cs` + Editor `GameplayTestResourceCacheStore.cs`，仅测试入口启用，保存源资产与解析 DTO 到 `Library/FlatWorldGameplayTests/GameResCache/`；普通玩法 C# 编译可复用，资源/解析契约/平台变化失效。菜单 `FlatWorld/测试/清理Lua测试GameRes缓存` 可冷建；`testing/resource_cache` 报告实际命中和完整资源阶段耗时，工厂、模块绑定及最终校验仍按当前 C# 重建。
- 清单与 Lua：`Assets/StreamingAssets/GameplayTests/`。命令、清单字段及运行选项见 [通用说明](../../../Assets/StreamingAssets/GameplayTests/README.md)；领域覆盖见 [系统索引](../../../Assets/StreamingAssets/GameplayTests/系统索引.md)、[太空](../../../Assets/StreamingAssets/GameplayTests/Systems/Space/Gameplay/README.md)、[气液](../../../Assets/StreamingAssets/GameplayTests/Systems/IndustrialFluids/Gameplay/README.md)。
- 默认 `all-systems-rules.json` 只跑各系统的快速规则，各系统在 `Systems/<系统>/`；现有机械、气液、太空专项保留并迁入相应系统目录。`systems.catalog.json` 登记代码归属、依赖、规则案例、现场入口及覆盖说明；目前均为部分覆盖，不将存在入口当成全覆盖。

## 修改后选择与补覆盖

- 陈列厅平地/无生态覆盖只作用于陈列厅世界；已有活动星球时不回退使用出生世界的 ReadyPlanetData 判断。其它星球的冷采样/生成专项用 `SuppressProfileOverrides` 租约，在返回前释放，并核对正式 Profile 和真实生态实体。
- 先把改动的正常、边界、拒绝、回滚和关键流程对应到具体Lua断言；缺少则先增强同系统Lua，必要时扩展正式白名单采样，不复制业务算法或开放反射/任意C#。
- `test.system(system,operation,args,validator,label)` 调用独立规则；`test.runtime(...)` 只用于正式隔离世界。每个回调必须实际调用assert，无断言即失败。参数排队时冻结，回调不能追加命令。
- 气液 PlayMode 的无库存连接图、过滤、独立罐、固定容量、三槽装填和地表泵/出口通过 `test.runtime("industrial_fluids",...)` 返回原始采样，Lua持有业务断言；既有 `test.fluid` 用于尚未迁移的子案例，新增判断不堆回C#。
- 联机假人专项在 `Systems/Networking/gameplay-suite.json`，`test.runtime("networking","bots_*",...)` 接正式 Mirror 内存传输/独立连接和服务端处理；检查实际回复、权威状态及收尾，不将传输送达或服务端删物当成真实 Client 库存接收。
- 规则模式只允许system/log且至少一个system；不进入Test Framework/PlayMode，不建资源/存档世界。场景模式默认 EditMode→PlayMode；显式 `--playmode-only` 则直接进入 PlayMode，保留清单预检与 Lua 断言但不启动 EditMode 测试。
- `gameplay` 清单若跨 Lua 依赖前置现场，在案例上声明 `dependsOn`（只引用更早且启用的用例）；多选/单选自动补齐前置，不能依赖列表相邻的隐含状态。
- 等待加工等异步生产结果优先用 `test.await_check(name,timeout,label)`，达标立即继续；不要用固定 `test.wait` 代替实际结果断言。
- 高耦合系统共用一份现场清单（AI/导航、地形/生态、任务/事件/对话/引导、UI/本地化、物品/MOD Lua/网络副本），在各系统 `integrationSuites` 指向同一路径；总入口按案例 ID 去重，不复制清单。
- 需要观察真实动作的调用时，在动作前后排 `test.profiler_start` / `test.profiler_stop`，详见 [Lua 接口与性能采集](../../../Assets/StreamingAssets/GameplayTests/README.md#lua-编写接口)；默认导出最慢保留帧，也可指定 Marker 找最近命中帧。调用树仅是选中帧，不等于整个采集区间或全部 C# 方法。
- 跨帧现场用 `RegisterAsync`，Lua仍调用 `test.runtime`；保留截图现场时先登记 `RegisterCleanup`，需要正式异步回程时登记 `RegisterAsyncCleanup`。每用例结束或失败先等跨帧恢复，再逆序同步清理；强制中断的异步迁移交宿主退出世界兜底，不报告已恢复。迁移后只重绑同档案、同 Data_Player 的隔离角色。
- 新主题更新本系统清单和README后运行 `python Tools/GameplayTesting/sync_catalog.py` 同步总清单与映射；新系统登记sourcePaths/dependsOn。Lua组合无需编译，新增C#操作仍需编译。
- `--systems` 精确选择；`--changed` 根据源码归属包含使用它的系统，未登记路径报错，测试框架改动选全部规则。影响实际链路再加 `--integration`，现场子集一次建世界。没有现场入口返回4；已有入口也须检查README中的缺口。
- 交互注册表与掉落回滚句柄会核对世界场景；临时现场夹具须将 GameObject 根移入隔离玩家的 `player.gameObject.scene`，不要默认使用 Editor 活动场景。
- 用户明确不需要验证时仅静态修改，未编译/未运行写清楚；其他任务在Unity未处于PlayMode时执行对应范围并读取真实报告，不能用静态检查冒充通过。

## 编写与截图

- 每个系统测试集保持 10 份 Lua 以内，单份编排最多256步；新增检查优先并入已有主题，超过预算按主题拆分，不提高限额。每个子检查保留独立中文标签和真实断言。纯数据与真实世界测试写清覆盖边界，摆放成功或调用完成不代表业务成功。
- Lua 先编排队列，C# 再按顺序执行；`test.space(operation, 回调, label, argument)` 的回调只验证采样，不在回调里追加步骤。普通失败停止本 Lua，气液组独立子案可继续收集错误但整组仍失败；`continueAfterFailure` 控制是否执行下一份 Lua。
- **PlayMode 场景宿主每 10 秒自动截图（场景参数可调），不再提供 `test.screenshot()` 命令。** 每份已执行 Lua 即使已有定时图、失败或提前结束，也必须在执行器 `beforeCleanup` 回调中停止定时、等待在途截图并强制拍结束图，之后才清理临时设备和租约；回调异常仍继续清理，结束图失败则用例失败。EditMode 规则不截图。
- 图片与 Lua 断言独立，定时采集不能替代关键阶段断言；太空跨场景时按正式测试流程恢复，截图不得阻止恢复和清理。采集失败保留实际错误，不把缺图记为通过。
- 截图宿主检查主角贴图启用、透明度、相机裁剪层和视口，连续两帧就绪后再取图；8 秒仍未就绪就失败并记录原因。实际图片仍需检查主角、HUD 与光照，组件状态或图像哈希不同不能证明画面有用，不用固定延时掩盖缺失现场。
- **测试整轮结束后统一视觉复核**：CLI `run_suite.py` 调用 `visual_review.py` 检查黑屏、纯色、缺图和损坏，生成 `visual-contact-sheet-*.png`。AI 必须实际逐页看完整套图，必要时打开原图，检查 HUD、UI、角色、遮挡及光照；证实异常就修复并重跑相关用例。**程序预检不是语义识图**，仅有 `needs_semantic_review` 不能报告“视觉通过”。MCP/测试中心结束后也可执行 `python Tools/GameplayTesting/visual_review.py <本轮Runs/report.json>` 并看图。
- 运行 PlayMode Lua 后，最终回复直接嵌入本轮关键实际截图，简短说明阶段和结果，不能只给报告路径或让用户去工具输出找图。

## 执行与范围

```powershell
# 用户明确说“PlayMode Lua 测试集”时使用，不运行 Editor 测试集
python Tools/GameplayTesting/run_suite.py --playmode-only
python Tools/GameplayTesting/run_suite.py --systems ai navigation --playmode-only

# 仅当用户要求规则或多阶段回归时使用以下入口
python Tools/GameplayTesting/run_suite.py --systems ai navigation
python Tools/GameplayTesting/run_suite.py --changed Assets/5_Scripts/5-3_GamePlay/World/Machines/MechanicalNetwork.cs
python Tools/GameplayTesting/run_suite.py --systems space --integration
python Tools/GameplayTesting/run_suite.py --list-systems
```

- 复用已打开且未处于 PlayMode 的 Editor；测试中心 `FlatWorld/测试/测试中心（多选案例）` 或 `gameplay_test_pipeline(run/status/report)` 也可执行。用户正在玩的 PlayMode 不停止、不暂停，只静态修改并说明本轮未运行。
- `gameplay` 清单**默认**对 FlatWorld EditMode 测试程序集做安全前置，再进入一次 PlayMode；`--playmode-only` 或 MCP 的 `playModeOnly=true` **跳过整个 EditMode 阶段和快速规则阶段**，直接在同一隔离世界执行选中 Lua，报告中的 EditMode 为 `not_requested`，不记作通过。`--cases` 只筛玩法案例，原清单不改；`buildShowroom: false` 跳过全陈列。
- 一份 Lua 一个沙箱与控制租约，世界、背包和玩家位置通常延续；前置条件自行准备。隔离保存租约覆盖首次、自动和退出保存，退出写盘并返回菜单后才恢复正式存档目录，不写正式玩家档案。
- 太空迁移恢复会重建玩家；宿主的`player_ready`按本轮同档案和隔离存档边界重绑ItemMgr当前本地玩家。下一条玩法命令前恢复现场，再显式检查world/player，保证后续Lua不因旧对象引用被跳过。
- 失败先看首个失败步骤、原始采样及堆栈，再修复并重跑相关范围；用户要求循环时逐轮推进，不以删除、禁用或放宽断言换取通过。未执行、环境阻塞和视觉/联机未验收应如实区分。
- 跨帧、太空或同步现场清理失败后，后续案例统一标为 skipped 并注明来源，不在可能受污染的世界中继续执行。
- 编辑态 UI 夹具用临时 ExecuteAlways 子类实际登记 EventSystem/Raycaster，清理时仅恢复存活的原事件系统；下拉模板必须先激活再调用布局入口，否则 LayoutUtility 会忽略隐藏行的首选高度，产生并非正式展开行为的假失败。
- 现场 UI 夹具手动添加 `UIWindowDragSurface` 等 `MaskableGraphic` 时须同时挂 `CanvasRenderer`；`RectMask2D` 会实际裁剪，不能用只有 `RectTransform` 的裸节点代替。
- 空地夹具按 ChunkTerrainData.GrassPresent 判断有草；GrassEmpty 为 1，不能把非零全部当作有草。冷生成等同步长步骤计入整场超时；应按预算拆分专项现场，记录未执行案例，不能把超时后的跳过计作通过。
- 固定步采样等待正式 `SpaceSession.State.SimulationSeconds` 达到目标，同时保留真实时间超时；慢帧或编辑器操作耗时不能计作物理推进。无外力惯性使用空星区自有夹具，同时检查位移、停推和燃料；星体引力或大气阻力范围内不能断言总速度不变。太空跨区另核验 UI 根身份、统一时间源与实际采光，防止逻辑通过但画面失效。

## 报告契约

```text
Library/FlatWorldGameplayTests/
├─ Pipeline/<运行ID>.json
├─ Runs/<运行ID>/
│  ├─ report.md / report.json / pipeline.json
│  ├─ cases/<用例ID>/
│  │  ├─ report.json
│  │  └─ screenshot-01.png …（每十秒自动生成，短用例结束补拍）
│  ├─ visual-review.json / visual-review.md（CLI / 视觉复核脚本输出）
│  └─ visual-contact-sheet-01.png …（本轮总览页）
└─ Saves/<本轮隔离目录>/
```

- 新报告 `schemaVersion: 5`；每用例的 `screenshots` 列表按自动采集顺序保存 `index/label/path/error/scene/utc/elapsedSeconds/playerPosition`。Markdown 按 Lua 与截图序号展示，流水线和 CLI 的 `screenshotPaths` 只列实际存在图片。
- 实际执行的 Lua 至少一张有效图；无渲染画面、截图失败或被中断时记录原因，不补造旧图。程序初检若发现异常使 CLI 失败；**生成总览图后 AI 仍须逐页视觉检查**，有问题修复重跑。历史报告与旧图片不改动。
- `Runs/<运行ID>/` 就是测试包，不另建 ZIP；收尾保留最近 10 轮。独立 Player 使用 `Application.persistentDataPath/FlatWorldGameplayTests/`。
