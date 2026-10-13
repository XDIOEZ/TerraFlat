# 通用 Lua 测试场景

## AI 快速测试流水线：EditMode → PlayMode

### Unity 图形测试中心（多选）

在 Unity 顶部打开 **`FlatWorld → 测试 → 测试中心（多选案例）`**：

1. 从下拉框选择 `quick-suite.json`、`test-suite.json` 或其他已登记的 JSON 清单。
2. 给需要执行的 Lua 案例**逐个勾选复选框**，也可全选、反选或清空（至少勾一个）。
3. 点击 **「开始测试已勾选的 N 个案例」**，EditMode 全部通过后，只进入 **一次** PlayMode，在同一测试世界内按清单顺序执行这些案例。
4. 窗口会显示阶段进度、断言结果、玩法案例 PASS/FAIL 和失败详情，并可定位或复制完整报告。

勾选状态按测试清单保存在 EditorPrefs 中；实际执行的案例 ID 仅写到
`Library/FlatWorldGameplayTests/Pipeline/selected-suite.json`。
**不修改原始测试清单、不重新导入资源、不为每个案例重新启动 PlayMode**。
原清单里标记 `enabled: false` 的用例在界面中显示为禁用，不能误选。
测试期间如果 Unity 已在 PlayMode，启动按钮会禁用，避免干扰当前玩家或调试现场。

**AI 只负责写清单和 Lua、启动一轮测试、读取报告并修复问题，不再逐步调用移动/攻击 MCP。**
项目已安装 Unity Test Framework 1.4.5，使用两套独立测试程序集：
`Assets/Tests/FlatWorld/EditMode/` 和 `Assets/Tests/FlatWorld/PlayMode/`。

在**已经打开、未运行 PlayMode** 的 Unity Editor 内通过菜单
`FlatWorld/测试/快速执行 EditMode 然后 PlayMode`，或调用 MCP 工具：

**更推荐直接让 AI 用 HomePC2 执行一条命令，等报告一次性返回（不需要循环调用 MCP）：**

```powershell
python Tools/GameplayTesting/run_suite.py GameplayTests/quick-suite.json
# AI/CLI 也可多选，用例 ID 用空格隔开：
python Tools/GameplayTesting/run_suite.py GameplayTests/quick-suite.json --cases enter_world player_movement
```

Python 只写入 `Library/FlatWorldGameplayTests/Pipeline/inbox/` 的本地请求，
已打开的 Editor 自动接单、运行两个阶段，再将 PASS / FAIL 和报告路径返回给同一条命令。
不额外启动 Unity、不反复调用玩法 MCP；退出码 0 为全部通过，1 为测试失败，
2 为 Editor 拒绝（例如已在 PlayMode），3 为 Editor 未响应或超时。
默认等待接单 45 秒、运行上限 900 秒，可用 `--editor-timeout` / `--run-timeout` 修改。

也可以使用 MCP 工具：

```text
gameplay_test_pipeline(action="run", suitePath="GameplayTests/quick-suite.json")
gameplay_test_pipeline(action="run", suitePath="GameplayTests/quick-suite.json", caseIds=["enter_world","player_movement"])
gameplay_test_pipeline(action="status")
gameplay_test_pipeline(action="report")
```

MCP 的第一条命令只负责启动整个流水线，后续由 Unity 自己执行；`status` 返回阶段与进度，
`report` 额外包含真实世界用例结果、Console 错误与调用堆栈。结果在：

```text
Library/FlatWorldGameplayTests/Pipeline/<runId>.json
Library/FlatWorldGameplayTests/Pipeline/latest-run.txt
```

EditMode 阶段通过生产清单解析器校验严格 JSON、脚本文件、**Lua 沙箱预编译与步骤限制**、用例有效性和测试场景，
由 Test Framework 自动执行 `FlatWorld.Gameplay.EditModeTests` 的所有测试。
**只有至少一项成功，且没有任何失败、跳过或未确定的测试时，才允许进入 PlayMode**。
PlayMode 阶段只进入 Unity PlayMode 一次、加载一次 `GameplayTestScene`、
建立一个隔离世界，在该世界中顺序跑完所有启用的 Lua 用例；不用每步往返 MCP，
也不重复启动 Unity Editor 进程。

`quick-suite.json` 设置 `buildShowroom: false`，跳过全物品陈列，只准备玩家所在真实区块。
这适合性能、温度和交互等专项快速回归。原 `test-suite.json` 默认保留全物品陈列，
专项需要检查物品目录时仍可开启。两个阶段的失败会写入同一份流水线报告，
包含独立的 EditMode / PlayMode 断言结果，PlayMode 会链接场景原有的每用例报告。

### AI 修复循环

1. AI 编写/更新一份 `GameplayTests/*.json` 清单及独立 Lua 文件，尽量保证用例前置条件可重复。
2. 调用 `run`，之后用 `status` 或 `report` 取得最终状态，而非逐步遥控。
3. 失败时根据最先失败的断言、日志/堆栈以及真实代码定位根因，修改后重新 `run` 原清单。
4. 重复至所有阶段通过；如果环境故障、连续无进展或当前用户正在 PlayMode，则停止并明确汇报阻塞，不改动/暂停正在使用的运行现场。

不要为了获得 PASS 而删除、禁用或放宽失败用例，也不要在未修改问题的情况下无限重复运行。
每轮都会生成新的独立报告和存档，方便 AI 比较修复前后的错误。测试驱动程序并不具备自主改代码的能力；修复仍由外部 AI Agent 执行。

**注意：** 两阶段范围是 FlatWorld 自己的 EditMode/PlayMode 测试程序集，
不自动执行第三方 Package 的全部测试。用户正在 PlayMode 时启动命令会拒绝执行，不会擅自停止游戏。

在 Unity 中打开 `Assets/3_Scenes/Development/GameplayTestScene.unity`，点击 Play 即可。场景复用正式 WorldManager 和建世界流程，自动加载完整资源、创建默认模板玩家，再建立平整的物品陈列厅，不需要操作主菜单。

陈列读取本次资源会话的物品目录，每个非 Actor 物品 ID 放置一个，按网格排列并留出通道。机器、自然资源与普通物品分别走各自的正式生成入口。角色、动物和怪物不作为物品陈列。新增物品会自动进入下一次陈列，不需要手动维护列表。

## 多 Lua 测试清单

场景的 `GameplayTestRunner` 组件默认读取 `Assets/StreamingAssets/GameplayTests/test-suite.json`，按照 `cases` 数组的顺序逐个执行启用的 Lua。组件的 `Test Suite Path` 可以指定另一份相对于 StreamingAssets 的 JSON 清单。每次点击 Play 都创建一个全新的测试世界，一份清单内的所有用例共用这个世界。

默认清单包括入世、移动、快捷栏和 GM 专项回归。`quick-suite.json` 包含 7 个用例，跳过物品陈列；`test-suite.json` 包含 8 个用例，额外执行全物品陈列及创造背包。所有用例独立 Lua 文件，可在测试中心按 ID 单独勾选；GM 用例自己准备条件，不依赖上一用例是否成功。

例如，之后分别写好电力和机械动力的业务测试脚本，再把下列条目追加到清单的 `cases` 数组：

```json
{
  "id": "electricity",
  "name": "电力系统测试",
  "luaPath": "GameplayTests/electricity.lua",
  "enabled": true
},
{
  "id": "mechanical_power",
  "name": "机械动力测试",
  "luaPath": "GameplayTests/mechanical_power.lua",
  "enabled": true
}
```

这两个条目是添加方式示例，当前没有提供相应业务脚本。应先写出具体测试步骤和业务检查，再启用条目；直接启用缺少文件的条目会记录为失败。现有 `test.check` 只支持下文列出的通用状态，电量、机械转速等业务断言需要在正式系统接口的基础上扩展检查能力。

清单格式：

| 字段 | 规则 |
| --- | --- |
| `schemaVersion` | 必填，当前为 `1` |
| `continueAfterFailure` | 可省略，默认 `true`；某个用例失败后继续下一个。设为 `false` 时，后续启用用例记为 `skipped` |
| `buildShowroom` | 可省略，默认 `true`；设为 `false` 时只准备玩家脚下区块，节省搭建全物品陈列的时间 |
| `cases` | 必填，包含 1 至 64 个用例，至少启用一个 |
| `id` | 每个用例必填，1 至 64 个英文字母、数字、下划线或连字符；忽略大小写时也不能重复，不能使用 Windows 保留文件名 |
| `name` | 每个用例必填，非空显示名称 |
| `luaPath` | 每个用例必填，StreamingAssets 下的相对 `.lua` 路径；禁止绝对路径、上级目录和无效路径字符 |
| `enabled` | 可省略，默认 `true`；设为 `false` 时不执行，报告记为 `disabled` |

未知字段、重复 JSON 字段和字段类型错误都会拒绝执行清单，避免拼错配置却继续运行。

每个用例使用自己的 Lua 环境、玩家控制租约、步骤列表和日志，结束或失败后释放控制，再进入下一个用例。世界、玩家位置、背包和机器状态会延续到下一个用例；有前置条件的测试需要在自己的脚本里确认或准备这些条件。`continueAfterFailure` 只决定用例失败后是否继续，世界进入失败、场景退出或总流程超时仍会结束整轮测试。

## Lua 命令

Lua 使用点调用语法，先编排步骤，再由 C# 按顺序分帧执行。单个步骤失败会停止当前 Lua 的后续操作；接下来是否执行下一个 Lua 由清单决定。陈列中某个物品放置失败会继续尝试其他物品，并把各项失败写入总报告。

| 命令 | 用途 |
| --- | --- |
| `test.check(name, label)` | 检查运行状态，label 可省略 |
| `test.log(text)` | 记录流程说明 |
| `test.wait(seconds)` | 按真实时间等待 |
| `test.move(x, y, seconds)` | 给真实玩家输入一个移动方向，结束时归零 |
| `test.move_to(x, y, timeout, tolerance)` | 通过正式导航前往世界坐标；默认超时 10 秒、到达距离 0.25 |
| `test.look_at(x, y)` | 设置玩家瞄准的世界坐标 |
| `test.interact()` | 通过正式交互接口操作当前位置的目标 |
| `test.select_hotbar(index)` | 选择快捷栏，下标从 0 开始 |
| `test.use()` | 调用当前手持物的正式使用入口 |
| `test.gm(command, argument)` | 通过白名单 GM 命令准备测试条件；argument 根据命令可省略 |

可检查的名称：`world_ready`、`player_ready`、`terrain_ready`、`showroom_complete`、`player_moved`。其中 `player_moved` 比较当前用例开始时的真实玩家位置，要求移动距离大于 0.1；`move_to` 只有导航返回已到达且实际距离满足阈值才算成功。`use` 的成功表示使用入口完成调用，具体物品效果需要后续另加业务检查。

GM 命令和断言额外提供：

| GM 命令 | 条件与使用 |
| --- | --- |
| `enable_admin`、`enable_invincibility`、`disable_invincibility` | 走正式玩家管理员与无敌 API |
| `set_health_ratio`, `"0.5"` | 仅管理员可使用，设置为 10%～100% 最大血量（测试专用造条件） |
| `set_move_speed`, `"2"` | 仅管理员可使用，走正式速度倍率接口 |
| `apply_self_buff`、`remove_self_buff`，参数 `"core:night_vision"` | 使用正式 Buff 目录与生命周期 |
| `initialize_creative_inventory` | 使用正式 F2 创造背包填充逻辑，耗时较长 |

状态断言：`gm_admin_enabled`、`gm_invincibility_enabled`、`gm_invincibility_disabled`、`gm_damage_blocked`、`gm_damage_normal`、`gm_health_full`、`gm_health_partial`、`gm_move_speed:2`、`gm_buff_active:core:night_vision`、`gm_buff_absent:core:night_vision`、`gm_creative_inventory_enabled`、`hotbar_slot:0`（数字可替换）。每个检查都会读取真实模块状态；GM 命令未被白名单允许会在 EditMode Lua 预检阶段报错。用例结束或提前失败时会恢复本用例修改的无敌、移动倍率、生命和新增 Buff；启用的管理员身份及创造背包内容保留在本轮隔离测试世界中。

每个 Lua 最多 256 步，单步等待或移动最多 30 秒，累计动作时间最多 180 秒。Lua 编排阶段有指令预算，死循环会记录错误并结束当前用例。场景另设资源、世界进入和整份清单的总流程超时，可在组件上调整；增加较长的业务测试时，要同时调整总流程超时。

## JSON 报告与存档

编辑器输出位于项目目录：

```text
Library/FlatWorldGameplayTests/Reports/<运行时间_运行ID>.json
Library/FlatWorldGameplayTests/Reports/<运行时间_运行ID>/<用例ID>.json
Library/FlatWorldGameplayTests/Saves/<运行时间_运行ID>/
```

独立 Player 的同类目录位于 `Application.persistentDataPath/GameplayTests/`。测试期间的首次保存、自动保存和退出保存都留在本轮的独立 Saves 目录；退出保存完成并返回菜单后，恢复正式存档目录。

总报告包含运行时间、当前阶段、最终状态、测试清单及每个用例的状态和报告路径、每个陈列物品的 ID/坐标/后端/放置结果，以及 Unity 日志、错误和堆栈。每个用例另有一个 JSON，保存自己的步骤结果、起止玩家位置和日志，查看电力测试时可以直接打开 `electricity.json`。

总报告最终状态为 `passed`、`failed` 或 `interrupted`，执行中为 `running`。用例状态区分 `pending`（等待）、`running`（执行中）、`passed`（通过）、`failed`（失败）、`interrupted`（中断）、`skipped`（跳过）和 `disabled`（未启用）。即使启用失败后继续，之前发生的失败仍会保留，总报告也不会被后续成功改成通过。Unity Error、Exception、Assert 或任意失败步骤都会使本轮失败，Warning 单独计数。普通日志有条数上限，超出的数量在报告中保留。

正常结束后会释放测试玩家的控制租约，保留世界供人工检查；停止 Play Mode、组件停用、退出世界或超时都会尽力收尾并写入报告。Console 中的 `[GameplayTest]` 结束消息会给出报告的完整路径。

本场景作为开发入口直接在编辑器运行，未加入正式游戏的 Build Settings。
