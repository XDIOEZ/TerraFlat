---
name: flatworld-gameplay-mcp
description: "FlatWorld GamePlayMCP 的隔离会话、控制租约、玩法动作、UI 事件、结构化观察及协议扩展。"
---

# FlatWorld GamePlayMCP

## 系统入口

- `Assets/Editor/FlatWorld/GameplayMCP/`：MCPForUnity 自定义工具、动作注册与诊断；UI 为 `GameplayUi{Tool,Runtime}.cs`。
- `Assets/5_Scripts/5-3_GamePlay/Player/Controller/`：`Mod_GameController{,.ExternalControl}.cs` 输入仲裁/租约、`Mod_InteractSender.cs` 交互。
- `Assets/5_Scripts/5-3_GamePlay/Entities/Item/Modules/Player/Mod_GameMCP_LLM.cs`：导航跟随。
- `Assets/5_Scripts/5-3_GamePlay/Items/Inventory/Mod_HotBar.cs`：快捷栏。

GamePlayMCP 通过正式生产 API 控制真实玩家；结构化状态/UI 树是操作入口，截图补充视觉信息。当前可用动作与参数由 `gameplay_capabilities` 提供。

## 会话与控制

- 正式启动场景为 `Assets/3_Scenes/GameStartScene.unity`。会话状态由 `gameplay_session(status)` 查询；世界玩法需要真实 Play Mode 和就绪世界，主菜单 UI 不需要玩家租约。
- `continue_save(isolated=true)` 默认复制最近正式存档到 `Library/FlatWorldGameplayMCP/Saves/`；`create_world(isolated=true)` 的首次及后续保存也在隔离目录。正式存档写入仅限明确授权。
- 入世等待默认 60 秒、最长 120 秒；已进入加载过程时查询状态，不重复建世界或切存档目录。`world_entry_timeout` 可能只是等待超时。`save_exit` 调用生产保存退出协程。
- `continue_save/create_world` 首次成功入世通过生命公开接口把本地玩家当前/上限初始化为 20000，仅一次；已有世界、状态查询、获取租约和普通菜单启动不改血量。
- `control(acquire)` 建立唯一外部控制租约；脚本重编译、Domain Reload、退出或重进世界后重新执行 `status → acquire → observe`。结束控制用 `act(stop) → control(release)`。
- 无玩家的启动资源失败可用 `session(reload_resources)`，调用 `GameRes.RequestResourceReload`，不依赖租约，也不保存退出。
- 用户正在使用的 Play Mode 按项目规则保持运行；上述启动/退出链用于已授权的测试会话。

## 动作与 UI

- `gameplay_act`：`move/move_to/look_at/interact/attack/use/select_hotbar/press_key/stop/wait`。交互、攻击、手持走原玩法链；`press_key(inputAction=...)` 经独立虚拟 Keyboard 和当前改键，文本走 UI text。
- `move_to` 由 `Mod_GameMCP_LLM → WorldNavigationManager → Mod_GameController → Mod_Mover` 跟随路点，卡滞最多重规划两次。外层连续 8 秒位移不足 0.05 时 `no_progress`，模块可返回 `navigation_stalled`；二者 `timedOut=false/notReached=true`，耗尽时限才是超时。`reached`、剩余距离和新观察表示实际到达。
- 持续动作当前上限约 20 秒。`steps` 批次最多 8 步、总等待最多 8 秒，持续动作显式传 seconds；失败即停，结果为 `executed/remaining/results`，已执行内容不回滚。
- `gameplay_ui(tree)` 为当前激活 Canvas 返回分页语义树，默认 64、最多 128 节点。`click/text/scroll/drag` 使用当前可见可交互节点 ID，经 EventSystem 射线和指针事件；文本进入 TMP/UGUI 输入链。UI 变化后旧节点可能失效。
- UI 不依赖玩家控制租约。`treeAfter=true` 操作后附新树于 `data.tree.data.semantic_tree`；内层 success/error 表示树是否可用。
- `gameplay_gm` 使用能力接口中的白名单，要求世界就绪与租约；`apply_self_buff:<buffId>` 校验目录后调用 `Mod_BuffManager.AddBuff`。未提供的生成行为可走正式 GM UI。

## 观察与查询

- `gameplay_observe` 返回玩家、附近实体、ECS 掉落物与脚下 3×3 权威地块。`player.hand` 为鼠标搬运槽，`player.hotbar.held/equippedWeapon` 才是装备；温度看 `player.temperature/environment`。
- 液体深度/身份/浮力来自 `Mod_TileEffectReceiver`，附近水格来自独立液体层及支撑面；`currentTileData` 只代表 Ground。水格可带河流/海洋表层流向及流量。
- `gameplay_query` 只读来源：`runtime` 实例/空间索引、`ecology` 已加载自然物放置、`terrain` 环境阈值、`tile` 地块身份、`drops` 离线 ECS 掉落物桶。名称可用稳定 ID 或完整本地化名称，不触发生成、传送和拾取。
- runtime 按距离分页，默认 3、最多 32 条，radius 最大 64，`total_count/truncated/next_offset` 表示分页；tile 默认最近 1 格。runtime 机械详情为 RPM/扭矩/加工快照，drops 为实时位置、数量和拾取状态。

## 输出与结果语义

- 通用 `output=compact` 缩减结果，`fields` 选顶层字段，错误/状态/分页/批次始终保留；省略代表未返回，不代表空。能力 compact 只列命令，具体契约看 full。
- `observe=true` 可随动作/租约返回精简新观察；观察 `profile=compact` 可按需加附近/地形/掉落物/库存，full 给完整状态。合并观察只等一次 Editor 更新，不保证异步制作或世界加载完成。
- runtime query 的 compact 省略详情，可用 `includeDetails=true`。诊断 compact 的对象样本通常仅保留前 4 项，`omittedSamples` 表示省略，完整样本在 `reportPath` JSON。
- 外层 `ok/handled` 只表示工具传输；业务结果、内层 success、到达状态及观察才说明动作发生。Console 的 `Agent反应间隔≈` 是串行调用间隔，含通信/排队/人工停顿，不是纯推理耗时。

## 扩展与诊断

- JSON + Lua 自动回归优先 `python Tools/GameplayTesting/run_suite.py GameplayTests/quick-suite.json`，也可用 `gameplay_test_pipeline(run/status/report)` 或测试中心多选。系统入口、编写规则、每份 Lua 最多三次显式截图、隔离执行及报告契约统一见 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md)。

- `capability_gap` 表示正常玩法缺少协议表达。动作实现 `IGameplayMcpAction` 并加 `GameplayMcpActionAttribute`，TypeCache 注册表生成能力清单；扩展复用生产接口/UI 事件，不引入任意代码执行或私有状态写入。
- `gameplay_aiecs_debug(status/sample)` 只读 GM 模拟；`gameplay_chunk_render_debug(status/sample)` 只读区块阶段/心跳/耗时。采样 1～20 秒；区块采样在暂停、换世界、编译时标记无效，报告位于 `Library/FlatWorldGameplayMCP/ChunkDiagnostics/`，菜单 `FlatWorld/调试/区块加载`。
- 功能复现使用真实玩法链，协议不维护一次性测试动作。连接、超时和性能解释见 [MCP游戏测试经验.md](Exp/MCP游戏测试经验.md)，采集查询见 [采集查询.md](Exp/采集查询.md)。
