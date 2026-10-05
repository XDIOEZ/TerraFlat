# FlatWorld MCP 游戏测试实战经验

这里保存已经在真实 Unity / GamePlayMCP / Profiler 使用中验证过的经验、踩坑和判断规律。稳定的操作步骤与工具契约仍放在同级 `SKILL.md`；这里不重复完整流程，也不记录一次性帧号、某次 Bug 的流水账。

## Play Mode、编译与会话

- **有 C# 编译错误时先别测性能。** Unity 会直接拒绝进入 Play Mode；此时继续发玩法或 Profiler 命令只会制造无效诊断。
- 脚本重编译、Domain Reload、退出世界或重新进 Play 后，MCP 连接可能短暂断开，旧控制租约也不能继续信任。实战中最稳妥的是等待 Editor 恢复，再重新确认会话与控制状态。
- `create_world` / 进入世界返回 `world_entry_timeout` 时，不代表世界一定失败。实测过场景已经切换但资源初始化仍在继续的情况；先轮询 `gameplay_session(status)`，不要立刻重复创建世界。
- 新世界资源初始化不能只依赖“已经存在的单例”。实际遇到过资源会话尚未建立时永久等待的问题，因此创建入口必须能主动建立 `GameRes.Instance` 所需的资源会话。

## MCP 连接与手工协议调用

- MCPForUnity 与 PuerTS Unity MCP 可以同时连接同一个 Editor。前者更适合项目自定义 `gameplay_*` 工具，后者的 RawFrameData Profiler 报告更适合性能热点分析。
- 本项目实际使用中常见 MCPForUnity 监听 `6400+`，PuerTS Editor MCP 使用 `18990`。这些端口只适合连接诊断，不能成为玩法代码常量。
- `uvx` 首次拉起 MCPForUnity 可能有包安装/冷启动延迟；不要把首次无响应直接判断为 Unity MCP 损坏。
- 手工走 stdio JSON-RPC 时，**持续进程 + 逐行写入** 比反复启动新进程稳定。Domain Reload 后重新确认服务状态，再决定是否重建会话。
- 在 PowerShell 中把 JSON 嵌入多层命令字符串很容易丢掉双引号，实际会得到 `key must be a string` / JSON parse error。遇到这类错误先怀疑 shell 转义，不要误判为 MCP 参数格式或 Unity 端实现错误；优先用持久 stdio 会话逐行发送，或直接调用已有 HTTP MCP 端点。
- 用户中途改目标或打断测试时，应及时结束自己启动的长采样/临时 MCP 进程，否则后台进程可能继续占用连接或改变运行状态。

## Profiler 与性能判断

- **Profiler 本身会扰动 Editor 帧时间。** 低干扰 `gameplay_*_debug(sample)` 更适合判断版本整体帧时间和 backlog；Profiler 更适合回答“时间花在哪”。两种数据不能直接混成同一基准。
- 低干扰采样前要看采样结果里的 `profilerEnabled`，不能只凭“我没打开 Profiler 窗口”判断 Profiler 已关闭。实际遇到过 Profiler 仍在记录、关闭后同一场景帧推进明显增加的情况；PuerTS 性能抓取结束后也要确认它是否恢复了原记录状态。
- PuerTS RawFrameData 报告必须先过“新鲜度门禁”：`recordStarted=true` 且抓取前后 Profiler 帧范围确实推进；逐条核实警告，`current` 目标的连接提示可在已确认目标后保留。若出现 `Profiler recording completed, but no new frames were added after the previous Profiler buffer.`，该报告可能只是分析已存在/环形缓冲中的帧，不能用于本轮回归结论。
- Unity 2022.3 的 Deep Profile 开关须配合脚本重载；仅写 `ProfilerDriver.deepProfiling=true` 不足以证明已加载程序集插桩，报告中应出现真实托管方法调用链。深度数据只与同为深度采样的数据比较，不能拿其 FPS 对比普通采样。
- 深度采游戏时先关闭 `profileEditor`，PuerTS 抓取使用 `target=current`；`target=editor` 会临时开启整个 Editor 的录制。先用亚秒级窗口，报告导出可能长时间阻塞并消耗大量内存；卡住或连接超时须核对进程、日志和产物，不能直接断言 Unity 退出。域重载后从 `instances.json` 重读端口，旧 HTTP 端口仍监听不代表命令端点可用。
- 用户要求启动游戏时须显示并核实实际 Game 画面；隐藏窗口下的 `ready=true` 只能证明世界状态，不能代替可见运行验收。
- `gameplay_spawner_debug(sample)` 等工具返回的 `frames / elapsed` 可以帮助发现 Editor 更新明显变慢，但它不是正式 FPS 基准；其中 `measured.*` 主要描述该诊断工具负责的子系统工作，整帧根因仍要靠 Profiler 或更专门的诊断 Marker。
- Editor Profiler 的绝对 FPS 不能直接当发行版 FPS。实战中 GameView、UI Toolkit、Profiler 解析、MCP Server 都会进入采样；版本对比时更应关注同条件下的 P50/P95 和项目 Marker 变化。
- 热点排序优先看 **Self Time**。父级 Marker 的 Total Time 经常只是把真正热点包在里面，单看 Total 容易把入口函数当成根因。
- `Idle`、`Semaphore.WaitForSignal` 多数是线程等待，不应该直接拿来当优化目标。
- `Gfx.WaitForGfxCommandsFromMainThread` 很高时，常见含义是 Render Thread 等主线程继续提交；**没有 GPU Frame Time 不能据此断言 GPU 是瓶颈**。
- GC 必须沿 **Top GC Allocation Paths** 看业务调用链，只看 `GC.Alloc` 总量很难判断来源。
- Unity Mono 环境下 `GC.GetAllocatedBytesForCurrentThread` 曾出现不可用/恒零的情况。涉及“零分配”结论时应以 Profiler 子树里的 `GC.Alloc` 元数据或自检结果为准。
- Debug `OnGUI` 很容易制造字符串、GUIContent 和 IMGUI 分配。动物参数、蜂巢参数、路径等 Overlay 开着时，GC/CPU 报告会明显被调试显示污染。
- 旧 Profiler 报告只能保留历史方向。核心系统（尤其 AIECS、BRG、区块流送）一旦重构，必须重新抓同场景数据，不能把旧热点直接当成当前瓶颈。

## FlatWorld 热点定位经验

- **区块看不见不等于生成慢。** 先分辨 `pendingCommits / readyDataWithoutView / pendingBaseTerrain / 表现队列`：数据已经 Ready 而 View 迟迟没出现时，根因通常在主线程表现绑定而不是后台地形生成。
- 大存档进入世界时见过“后台生成已经结束，但可见窗口仍有数百个待表现，加载页继续超过 12 秒”的现场；这类情况应优先查 `view.queue_wait / view.start / renderer.*`，不要继续加后台生成线程。
- 区块**进入预算**和**离开预算**必须分开看。真实跨 Chunk 采样中，玩家移动触发 `Mod_ChunkLoader.UpdateChunks -> ChunkMgr.RefreshRuntimeWindow`，随后旧 View 直接走 `RecycleRuntimeChunkView -> ChunkView.Unbind -> ChunkNaturalItemRenderer.CaptureState`；一次跨边界会同步回收一整条旧窗口边（当前 10 格视距就是约 21 个 Chunk），出现过 `window.refresh ≈ 655ms`、其中 `CaptureNaturalItems ≈ 258ms` 的单帧尖峰。`presentationStart/continuation` 的 4ms/3ms 预算**管不到这条卸载/保存链**，所以看到加载队列预算正常也不能认为流送已经受控。
- 上述卸载尖峰会出现在玩家移动的 `FixedUpdate` 调用树下，因此如果只看 `Mod_Mover.FixedUpdate` 会误以为“玩家移动算法很慢”。必须继续展开子 Marker；发现 `FlatWorld.ChunkStreaming.CaptureNaturalItems / DespawnNaturalItems / UnregisterBatchOwner` 后，再回到窗口刷新与 View 解绑逻辑定位根因。
- `gameplay_chunk_render_debug(status)` 的 `total` 和 `recent 128` 都是**当前诊断会话历史**。世界刚进入时如果堆过很长的表现队列，即使此刻 `windowPresentationsReady=true`、`pendingPresentations=0`，`view.ready_latency / view.queue_wait` 仍可能被启动期旧样本长期污染；要判断“刚才移动一小段”的流送延迟，应使用新的 `sample` 窗口、重置后的诊断会话或只比较新增事件，不能直接拿累计平均值下结论。
- 区块表现的单次 `MoveNext()` 即使属于“分帧”流程，也可能一次吃掉数毫秒。判断预算是否有效要看单步 Marker 和队列吞吐，不能只看“代码里有 yield”。
- AIECS 测试要同时看 `Alive / Visible / Tick / Backlog / Burst`。实体很多但 Backlog 很低，通常说明模拟仍能跟上；只看 FPS 容易把渲染、Editor 或其它系统误归因给 AI。
- 做 AIECS/生态性能基准前必须确认旧存档生物已经成功迁移或恢复。实际遇到过 Bird/Chicken 旧快照仍走 GameObject 恢复入口，被新 ECS 入口拒绝，结果 `registered/active=0`；这种运行仍可用于测区块、Item、阴影等世界成本，但**不能**拿来评价新 AIECS 在真实生态数量下的性能。
- 某 MonoBehaviour 单次耗时很小但调用 `count` 极高时，往往是“每实体一个 Update/FixedUpdate/LateUpdate”的架构税。此类热点继续抠单次循环收益有限，集中式 Manager/Scheduler 通常更值得评估。
- 尤其警惕“每实体 `FixedUpdate`”的正反馈：帧率下降后 Unity 为追固定时间会在一个渲染帧内执行更多 Fixed Step；如果每个 Fixed Step 又遍历成百上千个实体组件，固定更新成本会继续放大，进一步拉低帧率。`WrappedItemPhysicsAdapter.FixedUpdate` 已在真实大存档中出现这种现象；分析时同时看 **渲染帧数、FixedBehaviourUpdate 次数、目标 Marker 调用 count**，不要只看单次耗时。
- 阴影和角色表现也要看“每帧遍历对象数”而不是只看算法：真实大存档里 `ActorShadowManager.LateUpdate` 与大量 `ActorRenderEffectController.LateUpdate` 会随着已实例化 Item 数上升而变成主线程热点。批量化自然物只能真正兑现收益，前提是它们不要继续进入这些逐实体表现管理链。
- Burst Job 自身耗时高时，还要继续确认主线程是否马上 `Complete()`。实战里同步发布边界可能比 Job 内部数学代码更值得先优化。
- 自然物、阴影、物理镜像等系统会互相放大：一个额外 GameObject 不只增加实例化成本，还可能继续进入 Item Tick、阴影、碰撞镜像、渲染效果等后续系统。评估“数据化/批处理化”时要看整条链的连锁收益。

## 截图与视觉验收

- MCP 返回“截图已保存”的路径不代表已经完成视觉验收。实战中必须真正打开 PNG 看画面，再与结构化状态和 Console 对照。
- 单个没有 Error 的短窗口不能替代整轮运行检查。动画、排序、流送、粒子等问题经常只在移动或跨区块后出现。
- 普通 UI 操作若已经有语义树与 EventSystem 动作，用截图猜按钮位置反而更脆；截图最有价值的是确认布局、遮挡、Shader、动画和排序这类结构化数据表达不了的结果。

## 并行开发现场

- 性能测试前先看 `git status`。实际项目经常同时有其它 Agent 正在改 AIECS、资源或配置；遇到属于别人未完成改动导致的编译错误，不要为了“先跑起来”擅自修掉对方施工中的代码。
- 运行状态也属于现场。别人正在保留暂停帧、Profiler 现场或复现状态时，自动进入/退出 Play、移动玩家、改时间流速都可能破坏证据；先确认当前测试目的再操作。
- 版本间要比较“变快/变慢”，至少保证存档/场景、视野、加载距离、Debug Overlay、实体量和 Profiler 开关状态接近。实际最容易造成假提升/假回退的是可见实体数或调试 Overlay 状态不同。
- 还要记录 `applicationFocused`。真实采样中 MCP 操作窗口切换会让 Unity 变成未聚焦，即使 `runInBackground=true` 游戏仍继续运行，Editor 的 VSync、重绘和输入环境也可能不同；严格回归测试应保持相同焦点状态，至少在报告中明确标注。

