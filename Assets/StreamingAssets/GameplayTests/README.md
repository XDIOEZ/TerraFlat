# 游戏系统 Lua 回归测试

此目录按实际职责组织 **38 个系统测试域**。每个域已有可执行的规则 Lua，系统之间的关键流程另有隔离场景入口。系统列表、源码归属、依赖、规则和场景映射在 [systems.catalog.json](systems.catalog.json)，人读的覆盖入口在 [系统索引](系统索引.md)。

**当前全部为部分覆盖。** 2026-10-15 缓存与全系统 DEBUG 循环已完成：39 个快速规则、13 个 EditMode 安全前置、同一隔离世界连续执行的全部 51 个 PlayMode 案例均通过。详见 [缓存与38系统DEBUG循环_2026-10-15.md](缓存与38系统DEBUG循环_2026-10-15.md)；历史失败记录保留。各系统 README 的覆盖缺口继续有效，玩法与画面由用户验收。

## 日常执行：先选系统，规则先跑

复用已打开、编译完成且未处于 PlayMode 的 Unity Editor，在仓库根目录执行：

```powershell
# 查看统计；只读文件，不启动测试
python Tools/GameplayTesting/run_suite.py --list-systems

# 修改哪个系统，通常只跑它的规则；系统 ID 见索引
python Tools/GameplayTesting/run_suite.py --systems ai
python Tools/GameplayTesting/run_suite.py --systems mechanical electrical

# 已知改动文件：选所属系统及使用它的系统；未登记路径会报错
python Tools/GameplayTesting/run_suite.py --changed Assets/5_Scripts/5-3_GamePlay/World/Machines/MechanicalNetwork.cs

# 先展示选择范围及场景缺口，不接触 Editor
python Tools/GameplayTesting/run_suite.py --changed Assets/5_Scripts/5-3_GamePlay/World/Machines/MechanicalNetwork.cs --plan

# 规则通过后，相关现场案例合并到一轮隔离世界
python Tools/GameplayTesting/run_suite.py --systems mechanical industrial_fluids space --integration

# 全部快速规则；没有隐含场景测试
python Tools/GameplayTesting/run_suite.py
```

`--systems` 精确选择系统。`--changed` 按 `sourcePaths` 选直接归属，再沿 `dependsOn` 找使用这些规则的系统；测试基础设施改动会选择全部规则。两者可联合使用；不能再混用位置清单或 `--cases`。`--integration` 根据 `integrationCaseIds` 去重后，只提交一次 `all-systems-gameplay.json`，减少重复建世界。

单个 `gameplay` 清单的案例还可填写 `dependsOn: ["前置案例ID"]`：多选或 `--cases` 单独选择后续案例时，会自动启用前置案例并按清单原顺序运行。依赖必须声明在后续案例前且已启用，缺失、重复、循环或禁用依赖会直接拒绝清单；此字段不同于 `systems.catalog.json` 的系统级依赖。

系统没有现场案例时，`--integration` 仍运行已有案例，但返回 **4** 并列出缺口。已有现场入口也只是部分覆盖，仍须看 README。其余退出码：0 全部所选案例通过，1 测试失败，2 Editor 拒绝，3 请求/文件/等待错误。只选规则不检查完整 PlayMode、联机或设备行为。

Python 仅在 `Library/FlatWorldGameplayTests/Pipeline/inbox/` 写本地请求；Editor 自动接单、执行和回报，不启动新 Unity 进程。接单默认45秒、CLI执行上限默认900秒，可用 `--editor-timeout` / `--run-timeout` 修改。快速规则调度另有120秒上限。

## 为什么快：规则和真实世界分层

| 清单模式 | 执行方式 | 适用断言 |
| --- | --- | --- |
| `rules` | Editor 更新循环直接执行 Lua，不进入 Test Framework/PlayMode，不启动 GameRes 或建立存档世界 | 正式规则、独立对象事务、状态机、边界、拒绝、回滚 |
| `gameplay` | EditMode 安全前置通过后，一次 PlayMode、一套正式资源和隔离世界 | 实际入世、移动、生命、Buff、库存、农业/建造、机器/气液/太空及存档流程 |

规则夹具只调用登记的生产 API，不在测试里重新算一套业务结果。部分规则使用独立且不激活的 Unity 对象，结束时销毁；纯规则没有玩家控制租约。所有场景操作只允许隔离测试玩家，正式存档目录不会被现场探针使用。

Lua 增加参数组合、断言和步骤无需重新编译 C#；新增白名单采样操作仍需要一次 C# 编译。正常速度收益来自跳过资源/世界启动以及精确选择范围；单轮实测耗时见执行记录，不据此承诺所有组合速度。

示意图见 [系统测试分层与修改回归](../../../游戏机制SVG文件夹/测试/系统测试分层与修改回归.svg)。

## Lua 专用 GameRes 与场景切换缓存

测试流水线和专用测试场景自动启用 `Library/FlatWorldGameplayTests/GameResCache/`。首次加载保存持久源资产及物品解析 DTO，普通玩法 C# 编译后可恢复；资源、配置、解析契约、Unity/包版本、平台和 Addressables 构建器变化会冷建。每轮仍新建资源句柄、运行时模板、当前工厂和模块绑定，继续 MOD 注册与最终引用校验。失败或中断不发布本轮缓存；元数据损坏时回到正式加载。菜单 `FlatWorld/测试/清理Lua测试GameRes缓存` 只在编辑态清理。

`testing_resource_cache_gameplay` 读取真实命中数量、目录状态和资源总耗时（包含指纹计算）；冷、热、真实编译后的热运行分别保留报告。Lua 资源阶段使用 8ms 协作预算，并去掉已完成批次后的额外空帧，普通游戏加载预算沿用原值。

单机地面与太空之间保留 9 个解绑的 ChunkView 壳、2 个完成装配的星区星体根；飞船另有 16 个船根和 2048 个部件的有界池。缓存画面关闭，旧世界数据、导航与交互释放；返回后重新接入当前模型、资源与场景。`presentation_cache` 检查两轮真实往返的对象身份、容量、解绑和当前世界就绪，`space_gameplay` 同时比较各船型起飞与降落的本船表现身份。正式退出和资源代际变化清理旧缓存，联机沿用原生命周期。

边界图见 [Lua与场景切换缓存](../../../游戏机制SVG文件夹/测试/Lua与场景切换缓存.svg)。

## 目录与分类

```text
GameplayTests/
├─ systems.catalog.json              源码归属、依赖、案例与覆盖说明
├─ all-systems-rules.json             全部系统快速规则入口
├─ all-systems-gameplay.json          现场案例总入口（按系统选子集）
├─ 系统索引.md                       38个系统覆盖入口
├─ mechanical-suite.json             既有七组机械集成
├─ fluid-suite.json                  既有九组气液专项
├─ universe-suite.json               六组宇宙太空专项（含缓存往返）
└─ Systems/<系统>/
   ├─ README.md                     已覆盖规则、夹具、关键流程缺口
   ├─ rules.lua / rules-suite.json   对应系统的规则案例
   ├─ gameplay.lua / gameplay-suite.json  该系统已实现的现场案例（可选）
   └─ Gameplay/                     太空/气液原专项脚本及说明
```

每个系统最多十份 Lua，优先把同主题边界并入现有文件并保留独立中文标签。原有20份机械、气液、太空 Lua 迁入相应系统目录，断言保留。之前删除的根目录通用案例不恢复；新增玩家、Buff、库存等使用本次系统入口。

## 新增 PlayMode 组合现场

2026-10-15 为此前没有现场入口的 20 个系统补上 12 套组合现场集。高耦合系统共用一份清单，并由系统目录表分别映射；总清单按案例 ID 去重。

| 现场入口目录 | 关联系统 | 实际链路 |
| --- | --- | --- |
| `Systems/AI/` | AI、导航 | 正式生物推进命令、异步寻路、实际到达、路径代价拒绝和撤销 |
| `Systems/Survival/` | 角色生存 | 真实加载生物体温缓冲、冷热疾病 Buff 与卸载恢复 |
| `Systems/Crafting/` | 手工制作 | 主角制作面板、正式库存扣料产出、满输出槽拒绝 |
| `Systems/Terrain/` | 地形、生态 | 真实加载地格覆盖栈、资源实体、导航占地、快照与恢复 |
| `Systems/Time/` | 时间 | 正式时间服务私有钟跨日、活动场景引用与实际采光，保留原世界钟 |
| `Systems/Dimension/` | 维度 | 正式矿洞入口、出口配对、角色重建和地表往返 |
| `Systems/Quests/` | 任务、事件、对话、引导 | 实际背包与拾取、任务推进/奖励、引导 Facts、正式事件与屏幕气泡 |
| `Systems/ItemRuntime/` | 物品运行、MOD、联机 | 实际物品加载/卸载、MOD Lua调用、网络快照与远程副本隔离 |
| `Systems/UISettings/` | UI、本地化 | 实际设置窗口、语言下拉与TMP绑定、保存、关闭放弃和输入解锁 |
| `Systems/Audio/` | 音频 | 正式声源和句柄、空间衰减、音量/静音、停止与池回收 |
| `Systems/Rendering/` | 渲染 | 真实物品接入角色 MPB、世界排序、特效池绑定与回收 |
| `Systems/Testing/` | 测试框架 | 真实命令顺序、跨帧等待、同步与跨帧清理在下个 Lua 前完成 |

本次已实际编译并纳入全 51 案例连续运行，结果见本轮执行记录；玩法与画面由用户验收。所有系统仍为部分覆盖；尤其网络副本现场没有开启 Host/Client，MOD现场没有加载用户安装包，时间现场没有推进自然生态跨日。各系统 README 列出具体边界。测试框架的两份 Lua 是前置与核对配对，不能只选后半份。

跨帧 `test.runtime` 操作由 C# `RegisterAsync` 驱动，C#仅提供正式接口采样，Lua持有业务断言。截图保留现场先登记 `RegisterCleanup`；需要协程回程则登记 `RegisterAsyncCleanup`。正常结束或失败先等跨帧恢复，再同步释放；强制中断的场景迁移仍依赖宿主退出世界收尾。

组合与执行顺序示意：[PlayMode组合现场测试](../../../游戏机制SVG文件夹/测试/PlayMode组合现场测试.svg)。

## Lua 编写接口

```lua
-- 检查正式奔跑消耗规则
 test.system("player", "movement_cost", {moving = true, running = true}, function(data)
    assert(math.abs(data.nutrition - 3.2) < .00001, "奔跑营养消耗倍率错误")
    assert(math.abs(data.water - .25) < .00001, "奔跑水分消耗倍率错误")
end, "玩家奔跑消耗")

-- 真实写盘只能进入gameplay清单，不能混进rules
 test.runtime("save", "isolated_roundtrip", {amount = 3}, function(data)
    assert(data.backupMatchesFirst, "正式写盘备份未保留上一版")
end, "隔离存档写入和读回")
```

生产采样、参数组合及完整业务断言参考 [玩家规则](Systems/Player/rules.lua)、[存档现场](Systems/Save/gameplay.lua)。

`test.system(systemId, operationId, args, validator, label)` 与 `test.runtime(...)` 会在排队时复制纯数据参数；只接受有限数字、字符串、布尔值、普通表，拒绝循环引用、函数和C#对象。对象键必须是字符串，数组键必须从1连续编号，空表按对象处理。C#生成原始采样，Lua回调验证；每个回调必须实际调用 `assert`，无断言直接失败。回调不能追加步骤。

| 接口 | 用途 |
| --- | --- |
| `test.system(...)` | 白名单独立规则操作，规则/场景都可使用 |
| `test.runtime(...)` | 隔离场景内真实流程采样 |
| `test.log(text)` | 报告说明；不能单靠log构成规则用例 |
| `test.check(name,label)` | 真实世界/玩家/地形就绪和正式GM状态 |
| `test.gm(command,argument)` | 白名单准备管理员、生命、Buff和系统夹具 |
| `test.fluid(caseId,label)` | 原气液专项，独立子案失败继续收集，整组仍失败 |
| `test.space(operation,validator,label,argument)` | 原太空规则及真实会话 |
| `test.move(x,y,seconds)` / `test.move_to(x,y,timeout,tolerance)` | 实际玩家移动/正式导航 |
| `test.look_at(x,y)` / `test.interact()` / `test.select_hotbar(index)` / `test.use()` | 正式交互与手持入口，具体效果仍须断言 |
| `test.wait(seconds)` | 真实时间等待，规则模式禁止 |
| `test.screenshot(label)` | 实际现场图，每Lua可0～3次，规则模式禁止 |

Lua 沙箱不开放 `CS`、`require`、文件或网络；单次编排/断言有100万条指令预算。每Lua最多256个展开步骤，单步等待/移动最多30秒，累计动作180秒。超过限制会拒绝或失败，不能靠无界循环等条件。

场景脚本通常共用世界、背包、位置和机器状态，必须自备条件、只清理自己的夹具。截图由AI在有意义的现场明确插入，不自动补结尾或失败图。真实 `test.space` 若需要截图，紧接采样命令排截图，下一条非截图命令才恢复现场；同步 `test.runtime` 已清理的事务不再拍空地充当现场。详见 [气液专项](Systems/IndustrialFluids/Gameplay/README.md) 与 [太空专项](Systems/Space/Gameplay/README.md)。

## 清单格式和编辑器入口

```json
{
  "schemaVersion": 1,
  "executionMode": "rules",
  "buildShowroom": false,
  "continueAfterFailure": true,
  "cases": [{"id":"ai_rules","name":"AI规则","luaPath":"GameplayTests/Systems/AI/rules.lua","enabled":true}]
}
```

严格JSON拒绝重复/未知字段和错类型。`executionMode`为`rules`或`gameplay`，省略时为`gameplay`；规则模式禁止`buildShowroom:true`。每清单1～512案例、至少启用一个；案例ID1～64位英文字母/数字/下划线/连字符、不重复、不使用Windows保留名；路径只能是StreamingAssets内合法相对Lua路径。

Unity菜单 **FlatWorld → 测试 → 测试中心（多选案例）** 可选择总规则、对应系统或已有专项，勾选所需案例并执行，显示独立规则阶段或EditMode/PlayMode结果。选择存于EditorPrefs和Library，不修改原清单。**已有PlayMode会拒绝测试，不暂停或退出用户游戏。** 直接打开测试场景点Play仍使用场景中显式保存的机械专项；规则清单请使用测试中心。

MCP同样支持：

```text
gameplay_test_pipeline(action="catalog")
gameplay_test_pipeline(action="run", systemIds=["ai","navigation"])
gameplay_test_pipeline(action="run", suitePath="GameplayTests/fluid-suite.json", caseIds=["fluid_transport","fluid_tanks"])
gameplay_test_pipeline(action="status")
gameplay_test_pipeline(action="report")
```

## AI修改系统后的工作顺序

1. 读取业务Skill及 [Lua测试Skill](../../../.agents/skills/flatworld-lua-testing/SKILL.md)，按索引定位归属和现有断言。
2. 把修改影响的正常、边界、拒绝、回滚和关键流程逐项对应到现有用例；缺少的先增强Lua，再改生产代码。确需新的生产采样时扩展对应探针，不开放任意C#或反射访问。
3. 同主题优先并入原Lua。新增主题后更新本系统`rules-suite.json`或`gameplay-suite.json`及README，再运行 `python Tools/GameplayTesting/sync_catalog.py` 同步总入口和案例映射；新系统在catalog登记源码和依赖。
4. 用户允许验证且Unity不在PlayMode时，按系统跑快速规则；影响实际生命周期/世界/输入/存档/机器/跨系统流程时再跑`--integration`。未映射路径和缺少场景用例都是补覆盖信号。
5. 从首个失败步骤、原始采样及Lua堆栈找根因，修Lua/生产代码后重跑原范围。不得删除、禁用或放宽正确断言换PASS，亦不得原样无限重跑。
6. 汇报实际选中系统、运行/未运行、失败与覆盖缺口；用户要求仅静态修改时不编译、不运行。

## 结果文件

```text
Library/FlatWorldGameplayTests/
├─ Pipeline/<runId>.json             阶段报告
├─ Runs/<runId>/report.md / report.json / pipeline.json
│  └─ cases/<caseId>/report.json      输入、原始采样、独立标签和错误位置
│      screenshot-01.png …           仅实际执行截图命令后生成
└─ Saves/<runId>/                    场景隔离档；规则模式不建世界存档
```

总报告及每Lua用例区分pending/running/passed/failed/interrupted/skipped/disabled；原始步骤保留失败，任意失败断言或采集到的Unity Error/Exception/Assert使整轮失败，Warning单独计数。新截图报告schemaVersion5，每图保留标签、序号、时间、位置、路径或取图错误；没有执行就没有图片。结果文件夹就是测试包，保留最近十轮，不另建ZIP或复用上轮证据。
