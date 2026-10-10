# Lua 游戏事件：守夜试炼

这是游戏本体 `Resources/Config/GameEvents/Lua/adaptive-vigil.lua.txt` 的示例说明，不是可安装 MOD 包。脚本作为 Unity TextAsset 加载；资源名称末尾的 `.lua` 由事件运行时归一化，因此配置中的 `script` 写 `adaptive-vigil`。

## 实际玩法

事件默认 `enabled = false`，使用 `manual` 触发，不会自行改变普通世界。启用后，初始半径内的玩家形成固定参与名单，以按稳定玩家 ID 排序后的首位玩家位置作为中心；没有在循环世界直接平均原始坐标，也不显示虚构的地图标记。

准备 15 个游戏秒后，玩家在中心 4 格内静止算守阵，在 4～12 格范围内移动算巡守。移动按循环世界距离除以实际采样间隔判断，默认速度阈值 `moveSpeed = 0.15` 格/游戏秒，避免高帧率时每帧距离很小而永远被判静止。守阵积累稳定度较快，巡守较慢并增加紧张度。离开范围削减稳定度；所有初始参与者离场超过 25 个累计采样游戏秒便结束。后来到场的其他玩家不自动加入。

每次积累 20 点稳定度开启一轮：根据当前人数、巡守比例和紧张度，冻结本轮天气、狼/幽灵种类及生成数量。天气子行动设置 `durationGameSeconds = maxSeconds`，以总试炼上限作为单轮天气的兜底期限；从该子行动首次开始计算，恢复不会延长期限。天气实际应用后，脚本每次 Tick 调用 `RunAction` 推进本轮原生 `creature.waves` 行动。完成生成后提前停止这一轮天气，休整 12 游戏秒，再重新蓄稳。三轮生成完成即结束，不要求杀光怪物，没有奖励发放接口。

天气和生物提供场景中的可观察变化；阶段播报使用 `ctx:Log` 写 Unity Console，**不是玩家 HUD 或弹窗**。游戏时间大跳时每次位置采样最多计 2 秒驻留，避免一次采样冒充整段连续操作；整个试炼超过 300 游戏秒会结束，避免出生位置长期不满足时一直挂起。已有生物使用普通生命周期，停止生成或取消试炼不会删除它们。

## Lua 与 JSON 共用动作

Lua 文件返回 `catalog`、`actions`、`conditions`，可另提供 `triggers`；事件目录仍使用本体的定义、校验、条件、冲突组和存档流程。普通静态事件继续使用 JSON；需要分支与顺序阶段时再引用 Lua 动作。

下面是完整的 JSON 调用例子，可以另存为 `Resources/Config/GameEvents/Definitions/lua-vigil-json.json`。它使用不同事件 ID，避免和 Lua 自带目录重复；同一个脚本动作可被多个事件复用。

```json
{
  "schemaVersion": 1,
  "events": [{
    "id": "lua_vigil_from_json",
    "displayName": "JSON 调用守夜试炼",
    "enabled": false,
    "priority": 90,
    "conflictGroup": "hostile_raid",
    "oncePerWorld": false,
    "cooldownDays": 1,
    "durationDays": 0,
    "trigger": {"type": "manual", "parameters": {}},
    "conditions": [{
      "type": "lua",
      "parameters": {"script": "adaptive-vigil", "handler": "participants", "args": {"minimum": 1}}
    }],
    "actions": [{
      "id": "vigil",
      "type": "lua",
      "parameters": {
        "script": "adaptive-vigil", "handler": "vigil",
        "args": {"rounds": 3, "chargeSeconds": 20, "maxSeconds": 300}
      }
    }]
  }]
}
```

事件 `actions` 数组中的兄弟行动各自开始和推进，数组顺序不代表“上一项完成才执行下一项”。需要等待、分支和先后关系时，用一个 Lua 行动的 `state.phase` 明确控制阶段。本例同一轮先应用天气，再逐帧推进刷怪；不同轮次使用不同子 ID。

## 回调与受限上下文 API

模块字段、分类、handler 名称和回调名称都区分大小写。分类使用小写 `actions/conditions/triggers`；回调使用小写 `validate/begin/resume/tick/finish/evaluate/collect`。上下文属性则使用大写开头的 `EventId/WorldKey/Now/OldTime/DayLength/StartedAt/IsGmForced/WorldSeed/TriggerCount/LastTriggeredAt`，方法使用 `ctx:GetPlayers()`、`ctx:GetPayload()`、`ctx:Distance(x1,y1,x2,y2)`、`ctx:Log(message)`。属性依回调场景填充，触发器使用 `OldTime/WorldSeed/TriggerCount/LastTriggeredAt`，行动使用 `StartedAt/IsGmForced`。

| 类别 | 回调签名 | 返回值与作用 |
| --- | --- | --- |
| 通用参数校验 | `validate(args)`，可选 | `true`，或 `false, reason`；在配置加载校验中执行 |
| 动作开始/推进 | `begin(ctx,args,state)`、`tick(ctx,args,state)` | `true` 完成，`false` 继续 |
| 动作恢复/结束 | `resume(ctx,args,state)`、`finish(ctx,args,state,cancelled)`，可选 | 返回值忽略；恢复状态或清理结束 |
| 条件 | `evaluate(ctx,args)` | `boolean, reason`；没有可持久化的条件 state |
| 自定义触发器 | `collect(ctx,args,state)` | 连续候选数组或 `nil`；触发器 state 单独保存 |

自定义触发器示意如下，它按玩家数达到要求后的首次检查产生一次候选；这是 API 用法片段，没有安装进默认示例：

```lua
triggers = {
    firstGathering = {
        collect = function(ctx, args, state)
            if state.emitted or #ctx:GetPlayers() < (args.minimum or 2) then return nil end
            state.emitted = true
            return {{time = ctx.Now, cause = "players.gathered", payload = {participants = #ctx:GetPlayers()}}}
        end
    }
}
```

目录中引用方式是 `trigger = {type="lua", parameters={script="脚本名", handler="firstGathering", args={minimum=2}}}`。候选数组每次最多 256 个；`time` 默认 `ctx.Now`，必须落在 `[ctx.OldTime, ctx.Now]`，`cause` 默认 `"lua"`，`payload` 使用对象。没有候选应返回 `nil` 或 `event.array({})`，不能用普通 `{}` 冒充空数组。候选仍需经过条件、冲突和冷却判定；示意中的 emitted 是“候选已发出”，不能当成“事件已成功启动”。

脚本只能使用纯工具白名单：`assert/error/ipairs/pairs/next/pcall/xpcall/select/tonumber/tostring/type` 和 `math/string/table/utf8` 副本，以及 `event.array`。不提供 `require`、`CS`、文件系统、包加载或 Unity 管理器。顶层、validate、触发器与条件不能启动原生行动；`RunAction` 只允许权威端动作的 begin/tick。

本例动作 `validate` 拒绝零、负数、非有限值与超过上限的参数，限制 `0 < innerRadius < radius <= 256`、轮数 1～16，并要求总超时大于准备时间；参与人数条件限制为 1～64 的整数。

## 状态、停止和恢复

`begin/tick` 返回 `true` 表示 Lua 行动完成，`false` 表示继续。`resume` 用于存档恢复；`finish(ctx,args,state,cancelled)` 用于正式结束或取消。`state` 只能保存可序列化的值和普通 Lua 表；不要保存 ctx、函数、userdata、循环引用。普通空表 `{}` 是 JSON 对象，空数组要写 `event.array({})`。

本例把固定名单、阶段、稳定度、紧张度、轮次、冻结参数和播报标记全部存入 `state`。原生子行动的 Started、Completed 和内部生成进度由父行动状态共同保存。恢复会重设位置采样基线，不补算离线站位，不重新播报已有阶段，也不重复开始已经保存的生成。这里的保证针对存档所记录的进度；读回更早的存档会正常回到该快照。

`RunAction(childId,type,parameters)` 首次启动并验证原生行动，以后继续推进；没有自动逐帧子调度，需要 Lua 自己再次调用。子行动的 ID、类型和参数固定，不能使用同 ID 改写本轮类型或数量。`StopAction(childId)` 正式结束该子行动，该 ID 不能再次启动；后续轮次用新 ID。当前只允许复用非 Lua 原生子行动，避免无限嵌套。

父行动完成、取消或异常时由运行时清理子行动。退出世界/切换维度属于释放当前运行引用、保留存档续跑的边界，不应冒充正式取消；返回源世界后恢复事件及需要恢复的子效果。本例 `finish` 只记录取消提示，不重新生成任何东西。

存在活动 Lua 动作事件时，重载配置会明确拒绝，当前配置和虚拟机继续使用；先正式结束相关事件，再重载。子行动只对实现 `IGameEventSuspendableAction` 的原生 Handler 调用 Suspend 清理临时效果，保留进度，返回源世界后用 Resume 恢复；内置天气实现该接口，生成行动的进度直接存档，无需挂起清理。扩展有临时效果的原生 Handler 时应实现此接口，不用正式 End 冒充暂停。

主机/单机权威端执行条件、触发和行动；联网客户端不能运行世界修改行动。Lua Console 日志不是客户端玩家通知；现有开始/结束通知也不代表本例的每轮播报已经同步给客户端，若需要客户端 HUD 应另接明确的通知展示接口。

## 手动 GM 验收

1. 把示例目录的 `enabled = false` 临时改为 `true`，让 Unity 导入资源；进入实际游戏世界后打开 GM 的“事件”页并重载配置。默认禁用的事件不会出现在已加载事件列表中。
2. 在“Lua 守夜试炼”卡片手动触发，查看 Console 中的中心坐标和准备提示。GM 强制触发会绕过条件、冷却与环境限制，因此玩家不足条件需要通过普通触发路径另验，不能用 GM 成功证明条件有效。
3. 分别在中心静止、外围移动、离开半径再返回，观察后续轮次的天气、数量、狼/幽灵分支。生成需镜头外合法位置、表现区块已就绪；看到“本轮生成完成”代表生成完成，不代表战斗胜利。
4. 一轮生成尚未完成时保存，退出并读回；阶段与已生成数应沿保存进度继续，不重复播报已发生阶段。切换维度再返回也应恢复源世界事件。
5. 全员离开参与半径直到超时、让总耗时达到上限、通过 GM 取消，各走一次结束路径；应停止后续生成、恢复事件天气，保留已出生生物。
6. 如需联机验收，分别观察主机与客户端：仅权威端执行，客户端不能重复生成；Console 播报不会自动变成客户端 HUD。验收后把示例恢复为禁用。

本例未进行 Play Mode 或设备验收；这些步骤留给实际游玩验证。
