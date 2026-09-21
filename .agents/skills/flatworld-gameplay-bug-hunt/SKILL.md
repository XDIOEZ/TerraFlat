---
name: flatworld-gameplay-bug-hunt
description: "Use when: 让 AI 启动 FlatWorld、进入世界、自主游玩，并通过截图和 Console 主动发现、修复、复测 Bug，直到当前测试范围内没有新的运行或视觉异常。适合开放式 Debug 循环；如果用户已经给出单个明确 Bug 并要求反复修到复现消失，使用 flatworld-fix-loop。关键词：自主测试、自动玩游戏、截图检查、找 Bug、修 Bug、持续测试、Debug 循环。"
---

# FlatWorld 自主游玩 Bug Hunt

## 目标

执行真实游戏测试闭环：

`启动 → 进入世界 → 观察 → 游玩 → 截图/Console → 发现异常 → 保留现场 → 修复 → 复现验证 → 继续测试`

除非用户限定单个 Bug，否则修复后必须继续测试，不能修完第一个问题就结束。

## 前置规则

1. 先读取项目 `AGENTS.md` 和 `flatworld-gameplay-mcp`。
2. 遇到具体问题后，再读取对应领域 Skill。
3. 开始前检查 Git 状态，不覆盖、回退或清理用户已有修改。
4. 默认使用隔离存档。
5. 外部 Unity/MCP 技术事实需要网络核实；项目事实以真实代码、Unity 和运行结果为准。
6. 禁止通过反射、直接改 Transform、血量、库存等方式伪造玩家行为。

## 启动

开始前确认：

- Unity MCP 连接的是正确实例。
- Scene 正确。
- Unity 未在编译或 Domain Reload。
- Console 没有阻塞 Error。

存在阻塞错误时先修复。

进入 Play Mode 后按当前 `flatworld-gameplay-mcp` 的协议执行：

`gameplay_capabilities`
→ `gameplay_session(status)`
→ 必要时 `gameplay_session(continue_save, isolated=true)`
→ `gameplay_control(acquire)`
→ `gameplay_observe`

始终以 `gameplay_capabilities` 的实际返回为准，不假定文档里的动作清单永远最新。

首次进入世界后截图一次，检查地图、玩家、UI、Sprite、Sorting、Shader、粒子等视觉状态。

## 自主测试循环

每轮只选择一个明确目标，例如：

- 移动探索。
- 跨区块。
- 拾取或交互。
- 使用物品。
- 攻击。
- 切换快捷栏。
- 操作 UI。

执行：

`observe → 选择目标 → act → observe → 判断结果 → 必要时检查 Console`

规则：

- 长距离行动拆成短步骤。
- 不长时间无目的 `wait`。
- UI 优先使用 `gameplay_ui(tree/click/scroll/drag)`。
- 不通过截图猜按钮坐标。
- 每完成约 2～4 个有效目标、进入新区域、完成重要 UI 操作或出现明显状态变化时做一次视觉抽查。
- 怀疑视觉 Bug 或修复视觉问题后必须截图。

## 异常判定

出现以下任一情况进入 Bug 闭环：

- 新增 Error、Exception、Assert 或相关 Warning。
- 动作成功但状态没有预期变化。
- 玩家卡住、异常跳变或状态停止推进。
- `move_to` 在合理条件下反复超时。
- 无原因 `inputLocked`。
- 合法交互、攻击、物品行为持续失败。
- 游戏状态互相矛盾。
- 地图、区块、实体显示异常。
- UI 错位、遮挡、重复或无法操作。
- Sprite、动画、Shader、粒子、排序异常。
- 截图与结构化状态明显矛盾。

Console 没报错不代表没有 Bug。

## Bug 闭环

发现异常立即：

`gameplay_act(stop)`

保存最小复现现场：

- 异常前状态。
- 动作与参数。
- 异常后状态。
- Console 日志。
- 必要的截图。

然后：

1. 判断所属系统并读取对应 Skill。
2. 定位真实代码或配置。
3. 修复根因，不给 GamePlayMCP 添加只为让测试通过的作弊逻辑。
4. 等待编译并检查 Error + Warning。
5. Domain Reload 后重新建立会话和控制权。
6. 重新进入隔离世界。
7. 重放最小复现步骤。
8. 视觉 Bug 再截图比较。

只有原复现步骤恢复正常，才算修复成立。

随后继续测试相邻系统，检查回归，并回到自主测试循环。

## capability_gap

正常玩家可以完成，但 GamePlayMCP 无法表达的操作属于 `capability_gap`，不是游戏 Bug。

处理：

`停止输入 → 找正式玩法 API → 扩展通用 MCP 能力 → 编译 → gameplay_capabilities 验证 → 继续测试`

禁止万能执行、任意反射和绕过正式玩法规则的后门。

## Console

至少在以下节点检查 Error + Warning：

- 进入 Play Mode 后。
- 进入世界后。
- 区块流送或重要交互后。
- 发现异常时。
- 修复并重新编译后。
- 最终结束前。

区分旧日志和本轮新增日志，不把历史错误误判成本轮回归。

## 结束条件

全部满足才结束：

- 已完成多轮真实游玩。
- 玩家实际执行过移动和其它玩法。
- 已进行视觉截图抽查。
- 没有仍可稳定复现的逻辑或视觉 Bug。
- 本轮相关 Error / Warning 已解决。
- 本轮修复均完成复测。

结束：

`gameplay_act(stop)`
→ `gameplay_control(release)`
→ 最终检查 Console 和文件修改。

## 汇报

只汇报：

- 测试了什么。
- 发现了什么 Bug。
- 修复了什么。
- 截图是否发现额外异常。
- 最终 Console 状态。
- 是否仍有未解决问题。

没有发现问题时：

“已完成真实 Play Mode 自主游玩、移动、视觉抽查及 Console 检查，本轮未发现新的可确认 Bug。”
