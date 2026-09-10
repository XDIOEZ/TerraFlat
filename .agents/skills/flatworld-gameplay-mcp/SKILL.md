---
name: flatworld-gameplay-mcp
description: "Use when: 让 AI 通过 Unity Editor 的 GamePlayMCP 自主游玩 FlatWorld、持续测试、探索世界、复现 Bug、修复后继续游玩，或在现有动作协议不足时扩展 MCP。关键词：GamePlayMCP、自主游玩、AI 测试、持续测试、自动玩游戏、capability_gap。"
---

# FlatWorld GamePlayMCP 自主游玩与自进化测试

## 目标

GamePlayMCP 默认不做操作系统级鼠标键盘自动化，也不让视觉模型逐帧看截图。桌面 UI 快捷键或仅通过键盘暴露的生产行为可以使用 Input System 虚拟键盘，但仍必须受外部控制租约和正式 InputAction 约束。

它用于让 AI 在 Unity Editor 的真实 Play Mode 中：

1. 通过结构化数据观察玩家与附近世界。
2. 通过结构化 UI 树理解当前界面，并通过真实 EventSystem 点击可见控件。
3. 通过正式生产 API 控制真实主角游玩。
4. 主动探索、战斗、交互、使用物品并发现异常。
5. 发现 Bug 后定位生产代码、修复、重新编译并继续复现。
6. 遇到“现有 MCP 无法表达的正常玩家操作”时，扩展 GamePlayMCP 协议，然后继续原任务。

这是一个持续迭代闭环，不是一次性自动化脚本。

## 入口

- Editor MCP 工具：`Assets/Editor/FlatWorld/GameplayMCP/`
- 外部控制租约：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/GameController.ExternalControl.cs`
- 输入仲裁：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/GameController.cs`
- 交互入口：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/Mod_InteractSender.cs`
- 快捷栏入口：`Assets/5_Scripts/5-3_GamePlay/Items/Inventory/Inventory_HotBar.cs`
- UI 观察与点击：`Assets/Editor/FlatWorld/GameplayMCP/GameplayUi{Tool,Runtime}.cs`

GamePlayMCP 复用项目已有 MCPForUnity 自定义工具发现机制，不另起一套任意代码执行服务器。

## 每次自主游玩任务的强制启动顺序

只要任务要求 AI 自主游玩、持续测试、探索或“自己玩一会看看问题”，必须优先启动 GamePlayMCP，而不是先截图。

1. 先用 PCC 查看本次目标相关的真实项目代码，并读取本 Skill 与相关领域 Skill。
2. 先调用 `gameplay_session(action="status")` 查看当前 Scene；如果不在世界且当前编辑场景不是 `Assets/3_Scenes/GameStartScene.unity`，先用 Unity MCP 场景工具切到正式 `GameStartScene`，不要从任意开发场景直接启动自主测试。
3. 确认 Unity 已连接；不在 Play Mode 时调用 Unity MCP `manage_editor(action="play")`。
4. Domain Reload 完成后调用 `gameplay_capabilities`，确认当前协议版本与可用动作/会话能力。
5. 再次调用 `gameplay_session(action="status")`。
6. 如果尚未进入世界：调用 `gameplay_session(action="continue_save", isolated=true)`。
   - 未指定 `saveName` 时默认使用最近正式存档。
   - `isolated=true` 是默认值；GamePlayMCP 会把存档复制到 `Library/FlatWorldGameplayMCP/Saves/` 后再游玩。
   - 除非用户明确要求操作正式存档，不得把自主测试直接写回玩家真实存档。
7. 调用 `gameplay_control(action="acquire")` 获取唯一主角控制租约。
8. 调用 `gameplay_observe` 获取第一份结构化状态，然后开始游玩循环。

需要操作主菜单、教程、背包、制作、设置等 UI 时，不要求先进入世界或获取玩家控制租约。直接调用 `gameplay_ui(action="tree")` 获取当前激活 Canvas 的语义 UI 树，从 `clickable=true` 且 `interactable=true` 的节点选择 `id`，再调用 `gameplay_ui(action="click", targetId=<id>)`。点击后界面可能同步变化，继续操作前必须重新读取 UI 树，不复用旧树猜测下一个节点。

需要创建全新正式世界时使用 `gameplay_session(action="create_world")`；它直接调用生产 `GameManager.CreateNewWorld(NewWorldCreationRequest)`。需要保存并返回主菜单时使用 `gameplay_session(action="save_exit")`；它直接调用生产退出协程并保存当前世界。

脚本重编译、Domain Reload、退出世界或重新进入 Play Mode 后，旧控制租约不可假定仍有效。必须重新执行 `status -> acquire -> observe`。

## 自主游玩循环

循环应保持短、可观察、可复现：

1. `gameplay_observe`：读取玩家位置、速度、生命、体力、营养、输入锁、快捷栏、背包摘要与附近实体。
   - 需要从大量自然物中快速寻找目标时，使用只读 `gameplay_query`。`source=runtime` 查询已实例化 Item，`query` 可填写稳定 ID 或任意已配置 Locale 下的完整物品名（例如 `Ore_Stone` / `石头` / `Stone`）；传入 `radius` 时只查询玩家周围该半径内的 Item，并复用 `ItemMgr` 空间索引，`radius` 最大 64 世界单位。运行时结果按玩家距离排序并强制分页，默认只返回最近 3 条、单页最多 32 条，通过 `total_count/truncated/next_offset` 继续读取；不填写 ID/名称时可直接取得附近不同物品，每条结果都包含稳定 `id` 与明确的 `position.x/position.y`，可直接交给 `gameplay_act(move_to)`。`source=ecology` 查询已加载 ChunkRuntime 的确定性自然物放置结果；`source=terrain` 按环境层阈值查询已加载地形格。它不能生成、传送或直接拾取实体。
2. 选择一个小目标，例如：
   - 沿一个方向探索一段距离。
   - 接近一个自然物并交互。
   - 选择武器并攻击附近目标。
   - 使用当前手持物。
   - 接近某个实体并观察其状态变化。
3. 通过 `gameplay_act` 执行一到数个有界动作。
4. 再次 `gameplay_observe`，比较动作前后的真实状态。
5. 定期及出现异常时调用 MCPForUnity `read_console` 检查 Error / Warning。
6. 若结果正常，继续探索新玩法；若异常，进入 Bug 闭环。

不要长时间无目的地 `wait`。每轮都应有一个可验证的玩法意图。

## 当前基础动作

当前协议通过 `gameplay_act` 至少支持：

- `move`：按二维方向持续移动一段时间。
- `move_to`：向世界坐标移动，带到达容差与超时。
- `look_at`：按坐标或 `targetGuid` 设置世界瞄准点。
- `interact`：按真实交互规则交互，可指定 `targetGuid`。
- `press_key`：通过独立虚拟 Keyboard 完成一次有界按下/松开。优先传 `inputAction`（如 `B/E/H/P/ESC/OpenChat`）以跟随玩家当前改键；`key` 用于明确模拟某个物理键。
- `attack`：走现有 `AttackStarted/AttackEnded` 武器链攻击。
- `use`：调用当前真实手持物的 `Item.Act()`。
- `select_hotbar`：通过正式快捷栏切换流程选槽。
- `stop`：停止持续移动/攻击并关闭奔跑。
- `wait`：让真实游戏继续运行短时间。

UI 使用独立的 `gameplay_ui`：

- `tree`：返回当前激活 Canvas 的分页语义树。纯布局 Transform 会被压缩，节点保留完整 `path`；默认最多 64 个语义节点，单次最多 128 个，避免把完整 UI Transform 树塞进上下文。
- `click`：接收 `tree` 返回的运行时 `targetId`，先按目标矩形执行 EventSystem 射线，只有目标当前真实可见、可交互且未被其它 UI 挡住时，才发送左键 `PointerDown -> PointerUp -> PointerClick`。这与项目现有槽位输入链一致，不直接调用 `Button.onClick` 或业务方法。
- UI 工具不依赖玩家控制租约，因此主菜单和进入世界前的界面也能使用。

已有 `interact`、`select_hotbar`、`use` 等专用玩法语义时仍优先使用这些动作；`press_key` 主要服务桌面面板快捷键、返回/聊天等 InputAction，以及确实只通过键盘暴露的行为，不应退化成用按键猜测替代结构化玩法 API。

单次持续动作保持短且有界（当前最多约 20 秒），避免跨过 MCP 桥接单次命令超时；长距离探索应拆成多轮 `observe -> act`。

不要只依赖本文档中的列表；每次任务开始仍以 `gameplay_capabilities` 返回值为当前真实能力。

## Bug 闭环

以下情况应视为值得诊断的异常信号，而不是盲目重复动作：

- 动作报告成功，但玩家位置/血量/库存/目标状态没有预期变化。
- `move_to` 在合理距离持续超时。
- 正常玩法出现无法解释的 `inputLocked`。
- 可交互实体在合法距离内持续交互失败。
- 攻击状态、目标血量、武器状态明显不一致。
- 玩家卡住、状态停止推进或出现不合理跳变。
- Console 出现新的 Error、Exception、Assert 或与当前玩法直接相关的 Warning。

发现异常后：

1. 先 `gameplay_act(action="stop")`，避免角色继续执行旧输入。
2. 保存最小复现信息：观察前状态、动作参数、观察后状态、相关 Console 信息。
3. 使用 PCC 重新读取与该 Bug 直接相关的生产代码、配置和 Skill。
4. 修根因，不给 GamePlayMCP 写“让测试通过”的特殊作弊逻辑。
5. 修改完成后检查编译与 Console。
6. 重新建立 GamePlayMCP 会话与控制租约。
7. 重放最小复现步骤。
8. 修复成立后继续原来的自主游玩循环，不因修完一个 Bug 就自动结束长期测试任务。

## GamePlayMCP 自进化规则

`gameplay_act` 返回 `capability_gap`，或 AI 明确判断“正常玩家能做，但当前协议没有结构化动作/观察方式”时：

**这不是游戏 Bug，而是 GamePlayMCP 能力缺口。**

此时必须：

1. 停止当前角色持续输入。
2. 用 PCC 定位该玩法现有真实生产 API、调用关系和领域 Skill。
3. 在 `Assets/Editor/FlatWorld/GameplayMCP/` 扩展一个可复用动作或观察字段；新动作实现 `IGameplayMcpAction` 并添加 `GameplayMcpActionAttribute`，注册表会通过 TypeCache 自动发现。
   - 如果缺口属于正常 UI 操作，优先扩展 `gameplay_ui` 的语义观察/标准 UI 事件，而不是为某个具体按钮创建专用动作。
4. 必要时只给生产系统补充小型、通用、职责明确的公开入口，例如 `TryInteractTarget`、`TrySelectSlot`。
5. 新入口必须继续走正式玩法规则，不能直接写血量、库存、Transform 或私有字段来伪造玩家操作。
6. 不提供 `ExecuteCSharp`、任意反射调用、任意 Console Command、任意私有字段写入等万能后门。
7. 不手工维护中心动作 switch；`gameplay_capabilities` 从动作注册表自动生成动作清单。只有会话/控制协议变化时才修改对应工具说明。
8. 如果扩展形成新的通用维护约束，再同步本 Skill；不要记录一次性流水账。
9. 编译完成后重新调用 `gameplay_capabilities`，确认新能力已注册，然后继续原始游玩目标。

协议应该随 AI 实际遇到的玩法需求逐步生长，不要预先为每个物品、建筑或怪物创建专用动作。

## 视觉使用规则

结构化观察是主通道。截图是低频兜底。

只在以下情况使用 `manage_camera(... screenshot ...)`：

- 需要验证 Shader、动画、粒子、排序、UI 等纯视觉问题。
- 结构化状态正常，但玩家体验仍明显异常。
- GamePlayMCP 尚未提供某个必要的可观察信号，且短期截图能帮助确定是否值得扩展协议。
- 修复视觉 Bug 后做最终定向验收。

禁止把“截图 -> 视觉模型判断 -> 模拟点击”作为普通游玩主循环。
普通 UI 操作应使用“`gameplay_ui(tree)` -> 读取文本/路径/控件状态 -> `gameplay_ui(click)`”这一结构化链路；截图只用于确认布局、遮挡、样式等纯视觉问题。

## 与其它测试体系的关系

- GamePlayMCP：负责开放式、自主、探索式游玩和发现未知问题。
- Runtime.GoldenPath：负责已经明确、可确定执行的生产行为回归。
- 领域 Smoke：负责局部边界与确定性断言。

GamePlayMCP 自主游玩发现一个稳定可复现的重要行为后，如果该行为适合确定性回归，应按 `flatworld-golden-path` 规则考虑补进 Golden Path，而不是永久只依赖开放式 Agent 游玩。

## 结束与交接

自主游玩任务真正结束前：

1. `gameplay_act(action="stop")`。
2. `gameplay_control(action="release")`。
3. 检查本轮实际修改和 Unity Console。
4. 汇总：发现了什么、修了什么、GamePlayMCP 新增了哪些通用能力、还有哪些未解决观察。

如果用户要求的是“持续测试直到发现/修完某类问题”，不要在一次正常动作后提前宣布完成。
