# 通用 Lua 测试场景

在 Unity 中打开 `Assets/3_Scenes/Development/GameplayTestScene.unity`，点击 Play 即可。场景复用正式 WorldManager 和建世界流程，自动加载完整资源、创建默认模板玩家，再建立平整的物品陈列厅，不需要操作主菜单。

陈列读取本次资源会话的物品目录，每个非 Actor 物品 ID 放置一个，按网格排列并留出通道。机器、自然资源与普通物品分别走各自的正式生成入口。角色、动物和怪物不作为物品陈列。新增物品会自动进入下一次陈列，不需要手动维护列表。

## 多 Lua 测试清单

场景的 `GameplayTestRunner` 组件默认读取 `Assets/StreamingAssets/GameplayTests/test-suite.json`，按照 `cases` 数组的顺序逐个执行启用的 Lua。组件的 `Test Suite Path` 可以指定另一份相对于 StreamingAssets 的 JSON 清单。每次点击 Play 都创建一个全新的测试世界，一份清单内的所有用例共用这个世界。

默认清单包含两个实际用例：`enter_showroom.lua` 检查世界、默认模板玩家、地面和物品陈列是否就绪；`player_movement.lua` 让玩家右走半秒、确认真实位置发生变化，再左走半秒。默认移动发生在陈列区旁边的空通道。

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

可检查的名称：`world_ready`、`player_ready`、`terrain_ready`、`showroom_complete`、`player_moved`。其中 `player_moved` 比较当前用例开始时的真实玩家位置，要求移动距离大于 0.1；`move_to` 只有导航返回已到达且实际距离满足阈值才算成功。`use` 的成功表示使用入口完成调用，具体物品效果需要后续另加业务检查。

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
