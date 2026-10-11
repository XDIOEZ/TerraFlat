# 通用 Lua 测试场景

在 Unity 中打开 `Assets/3_Scenes/Development/GameplayTestScene.unity`，点击 Play 即可。场景复用正式 WorldManager 和建世界流程，自动加载完整资源、创建默认模板玩家，再建立平整的物品陈列厅，不需要操作主菜单。

陈列读取本次资源会话的物品目录，每个非 Actor 物品 ID 放置一个，按网格排列并留出通道。机器、自然资源与普通物品分别走各自的正式生成入口。角色、动物和怪物不作为物品陈列。新增物品会自动进入下一次陈列，不需要手动维护列表。

## Lua 流程

场景的 `GameplayTestRunner` 组件默认读取 `Assets/StreamingAssets/GameplayTests/enter_showroom.lua`。修改该脚本就可以追加流程；另建脚本时，把组件的 `Lua Path` 改为相对于 StreamingAssets 的路径即可。每次运行都创建一个全新的测试世界。

Lua 使用点调用语法，先编排步骤，再由 C# 按顺序分帧执行。步骤失败会停止后续 Lua 操作，陈列中某个物品放置失败则继续尝试其他物品，并把各项失败写入报告。

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

可检查的名称：`world_ready`、`player_ready`、`terrain_ready`、`showroom_complete`、`player_moved`。其中 `player_moved` 比较进入世界时的真实玩家位置，要求移动距离大于 0.1；`move_to` 只有导航返回已到达且实际距离满足阈值才算成功。`use` 的成功表示使用入口完成调用，具体物品效果需要后续另加业务检查。

默认流程只做入场基础检查：世界、玩家、地面和完整陈列已就绪，然后右走半秒、确认真实位置变化，再向左走半秒并结束。默认移动发生在陈列区旁边的空通道。

流程最多 256 步，单步等待或移动最多 30 秒，累计动作时间最多 180 秒。Lua 编排阶段有指令预算，死循环会记录错误并结束。场景另设资源、世界进入和总流程超时，可在组件上调整。

## JSON 报告与存档

编辑器输出位于项目目录：

```text
Library/FlatWorldGameplayTests/Reports/<运行时间_运行ID>.json
Library/FlatWorldGameplayTests/Saves/<运行时间_运行ID>/
```

独立 Player 的同类目录位于 `Application.persistentDataPath/GameplayTests/`。测试期间的首次保存、自动保存和退出保存都留在本轮的独立 Saves 目录；退出保存完成并返回菜单后，恢复正式存档目录。

报告包含运行时间、当前阶段、最终状态、每步结果和玩家位置、每个陈列物品的 ID/坐标/后端/放置结果，以及 Unity 日志、错误和堆栈。最终状态为 `passed`、`failed` 或 `interrupted`；Unity Error、Exception、Assert 或任意失败步骤都使本轮失败，Warning 单独计数。流程未结束时报告状态为 `running`，可以区分未完成的进程中断。普通日志有条数上限，超出的数量在报告中保留。

正常结束后会释放测试玩家的控制租约，保留世界供人工检查；停止 Play Mode、组件停用、退出世界或超时都会尽力收尾并写入报告。Console 中的 `[GameplayTest]` 结束消息会给出报告的完整路径。

本场景作为开发入口直接在编辑器运行，未加入正式游戏的 Build Settings。
