# AI 性能分析前置说明

本说明统一放在 `Assets/67_ProfileData/` 根目录，供所有报告共用；各时间文件夹只保存 JSON 和截图，性能 JSON 的 `instructionsFile` 指向 `../00-READ-ME-FIRST.md`。先按下面顺序读取，只展开相关数据；用户导出后可以继续游戏，不需要保持 Unity 暂停。

## 选对报告

- 用户指定文件夹时只分析该目录；只说“最新”时，在 `Assets/67_ProfileData/` 中选时间最新且同时有两个 JSON 的正式目录，忽略 `.pending-*`。不要混用不同目录的文件；缺少截图时说明缺口，不擅自换成另一帧。
- `profiler-analysis.json` 是性能证据，`console-logs.json` 是日志，`screen.png` 是导出时的整个桌面；FlatWorld 顶栏/菜单还导出 `world-scene.json`（已加载世界物品现场），文件名登记在性能文件的 `additionalFiles`。本文件是阅读说明。
- 两个 JSON 的 `snapshotId`、`selectedDisplayFrame` 应相同。`schemaVersion` 分别描述各自格式，不要求两个版本号相等。先检查性能文件 `warnings`、`selectedFrameAnalysis.success/frameCount/skippedFrameCount`，缺数据时明确说明。

## 按需读取，控制上下文大小

1. 用 JSON 解析器读取文件，先输出元信息、`selectedFrameAnalysis.frameSummary`、`warnings`，以及 `topSelfTimeMarkers`、`topGcMarkers` 各前 10 项；不要直接打印完整 JSON 或整个 `callTrees`。
2. 有 `screen.png` 时看一次，确认可见的场景和 Profiler 状态。截图与日志都是导出时的现场，不能证明它们发生在所选录制帧。
3. 对比 `nearbyFrames` 中选中帧与邻帧耗时，区分尖峰和附近持续慢帧。格式 3 会额外读取附近超过 50ms 的最慢 3 帧，排除选中帧；查看 `additionalFrameDetails` 的帧号、`available/status/error`，有完整调用树时再展开。附近没有超过阈值的帧时不额外导出树，旧格式也可能只有邻帧耗时。
4. 从排行挑最有影响的 1～3 个方法，再按线程查 `callTrees` 中同名节点，沿 `parentId` 向上还原调用链，并查看直接子节点。每次只输出相关链及最耗时的几个子节点。
5. 日志先按 `entries[].severity` 计数，按正文/堆栈合并重复项；优先读 Error/Exception/Assert 和与热点同名的模块日志，再按需展开完整堆栈。不要只读错误，普通 Debug 日志可能包含分阶段计时和业务计数。
6. 有项目源码时，按方法名、Marker 名或日志 `file/line` 定位正式实现和调用方；不要为了分析已有文件重新采集、暂停或切换用户的 Unity。
7. 查看 `performanceDebugAtExport.available/status/statisticsEnabled`，需要处理规模时再按 Marker 筛 `points`。这是导出时本地 Editor 进程中项目 Registry 的累计快照，包含已完成区段，不对应所选录制帧，也不代表远程 Player。未开启、未加载或读取失败必须如实说明，不能把缺失或零数字理解为没有成本。
8. 有 `world-scene.json` 时，先核对 `snapshotId/selectedDisplayFrame`，读取 `status/notes/summary` 和区块运行/显示数量，再按热点相关的 `definitionId/sources` 筛 `items`，通过 `dataRef` 查 `itemData` 的模块和库存。不要打印整个现场。`loaded-chunk-placement` 只说明已加载区块中的剩余生成基线，不能当作正在 Tick 的实体；容器内容也不计入世界实体数。同一 GUID 的多个来源已合并，按来源的数量不能再相加当总数。

## 字段速查

所有耗时为毫秒，分配大小为字节。

| 路径 | 含义 |
| --- | --- |
| `selectedDisplayFrame` / `selectedFrameIndex` | Profiler 显示帧号从 1 开始；内部索引从 0 开始 |
| `selectedFrameAnalysis.frameSummary` | 仅选中这一帧的耗时统计；其中 P95/P99 不能当成多帧统计 |
| `selectedFrameAnalysis.topMarkers` | 按包含子调用的总耗时排序，最多 100 项 |
| `selectedFrameAnalysis.topSelfTimeMarkers` | 按自身耗时排序，最多 100 项；行内为 `name/selfTotalMs/totalMs/count/threads` 等字段 |
| `selectedFrameAnalysis.topGcMarkers` / `topGcPaths` | 分配相关 Marker 与 GC 调用路径；Marker 的 `gcBytesTotal` 含子调用 |
| `selectedFrameAnalysis.threadSummary` | 线程的 active/idle 统计，只用于判断方向；不能直接当成 CPU 忙碌率 |
| `callTrees[].threadIndex/name/nodes` | 只属于选中帧的每线程完整聚合调用树，节点数量不限于排行的 100 项 |
| `nodes[].id/parentId/name` | id 只在当前线程内有效；`parentId=-1` 为根；同父节点下的同名调用已经合并 |
| `nodes[].totalMs/selfMs/maxCallMs/calls` | 包含子调用的累计耗时、自身累计耗时、最长单次调用耗时、调用次数 |
| `nodes[].gcBytesWithChildren/gcAllocationsWithChildren` | 包含子调用的分配字节与次数，依赖录制中实际存在的 GC.Alloc 元数据 |
| `nearbyFrames[].displayFrame/available/timeMs` | 默认前后各 30 帧的耗时，显式请求半径最多 300；`available=false` 表示该项不可用于比较 |
| `additionalFramePolicy` | 附加帧选择规则：附近有效帧中严格超过 50ms、按耗时降序取最多 3 帧；相同时按帧号升序；排除已选中帧 |
| `additionalFrameDetails[].displayFrame/timeMs/available/status/error/callTrees` | 每个附加尖峰独立的帧号、先前观察耗时与完整线程树；缓冲滚动导致失效或读取失败时保留状态，不猜原因。不复制主报告的选中帧排行 |
| `performanceDebugAtExport.capturedAtUtc/source/timeScope/status` | 导出时本地进程累计统计的时间、来源和可用状态；不是 Profiler 帧内数据，不含远程 Player Registry |
| `performanceDebugAtExport.statisticsEnabled/slowSummaryEnabled/slowThresholdMilliseconds/hasPendingSlowEvents` | 读取时性能统计、慢摘要、慢阈值和未消费慢事件状态；导出不改变开关，不清空统计，不消费慢摘要 |
| `performanceDebugAtExport.points[].name/calls/work/gauge/peakGauge/hasGauge/totalMilliseconds/maximumMilliseconds` | 自上次清空/会话重置以来、开启收集期间的累计调用/工作量、最近和峰值规模、包含式总耗时和单次最大值；工作量单位按点位说明，不能混加 |
| `screenshot.file/capturedAtUtc/width/height` | 图片文件名、截图时间与像素尺寸 |
| `additionalFiles` | 格式 4 的项目附加文件名；FlatWorld 导出入口附带 `world-scene.json` |
| 现场 `status/capturedAtUtc/captureCompletedAtUtc/unityFrameAtExport` | 本地导出现场的可用性、采集时间范围和 Unity 当前帧；`not-in-play-mode` 不是空世界证据 |
| 现场 `summary/chunks/items/itemData` | 分类数量、已加载区块状态、按 GUID 合并的世界实体和含库存内容的物品数据；实体和物品数据数量含义不同 |
| 日志 `entries[].index/severity/message/stackTrace/file/line/rawText` | 日志索引、级别、正文、堆栈、源码位置及完整原文；原文可能自带业务时间戳 |

## 避免误判

- `totalMs` 包含子调用，父子耗时不能相加；多个线程的耗时也不能相加当成帧耗时。递归方法的累计 inclusive 时间可能重复覆盖同一时间段。
- 跨线程的方法排行用于筛选热点，归因时回到对应线程的调用树。较大的 inclusive 时间配合很小的 self 时间，通常需要继续看子节点。
- 祖先节点的 GC 数已包含后代，不能层层相加。GC 数为零不证明“没有分配”，也可能没有采集到相应元数据。
- `EditorLoop`、`Profiler.ParseThreadData`、`Gfx.Wait*`、VSync 等编辑器/等待标记要与游戏业务分开；等待时间大不能直接证明 GPU 或某个脚本就是根因。CPU 调用树不提供 GPU 时间线。
- `deepProfilingAtExport`、`profilerTargetAtExport`、运行/暂停状态描述导出时设置；加载的历史录制可能不同。深度分析会影响采样成本，不能把一次 Editor 录制当成正式 Player 基准。
- Console 没有逐条日志对应的 Profiler 帧号或原生时间戳；`selectedDisplayFrame` 是这次导出的关联标识，不是每条日志的发生帧。已清空日志不能恢复。
- 附加帧在读取时可能已被正在录制的环形缓冲淘汰，主报告选中帧与附近耗时都只能说明导出时读到了什么。未附带的帧可能低于阈值、超出数量/半径/缓冲，并非已经排除性能问题；完整聚合树仍不是原始时间线。
- 性能统计默认关闭，关闭后可能保留此前开启时的累计数字。统计开关、快照和 Console/截图分别在导出过程中读取，后台任务仍可能推进；不能当作同一时刻的原子快照，也不能与历史所选帧作逐条时间关联。
- 世界现场也是导出时本地 Editor 的数据，不还原历史帧，不代表远程 Player，不是可直接加载的完整存档。只读已加载区块，不包含未加载存档区域；普通 Item 的持久模块状态可能落后于即时值，优先对照 `runtimeModules[].runtimeScalars/liveInventories`，未通用解码的模块状态保留 Base64。自然 ECS 的内置热状态写入独立副本，扩展模块保存回调不执行；不能把最近已有的扩展状态认定为即时值。
- 证据文件只支持定位和提出方案，不能单独证明修改后的性能改善；实际效果由用户在相同场景下验收。

## 回答方式

先写最值得处理的 1～3 个问题，每项给出“线程 → 调用链、耗时/调用次数/GC、相关日志、源码位置、优化方向”。分清已经看到的证据与仍需确认的原因，不整段复述原始 JSON。只有用户要求修改时再改代码；遵守项目模块边界，优先修重复计算、数据访问和分配来源，不靠不断拉长执行间隔掩盖开销。

## 下次怎样带上处理规模

固定 Profiler Marker 按 Unity 的录制规则工作，无需开启 GM 统计开关。需要 `work/gauge` 时，在 F4 / GM 的“设备数据 → 性能 DEBUG”中开启“性能统计”，复现一小段后再导出；按需清空统计可以划分观察区间，导出按钮本身不会替你清空。偶发长操作可另外开启“慢操作摘要”，设置阈值；现有日志泵仍按原规则输出，导出只读取现有日志和累计快照。

掉落水态首先看 `DroppedItems.Tick.Water` 下的 `DroppedItems.Water.{CleanupTerrainWatches,PendingEnvironment,WetBatch}`，再展开 `CheckEnvironment/TerrainSample/EntryTransform/ResolveBuoyancy/SampleCurrent/ResolveDestination/CommitDrift`。Cleanup 的 work/gauge 为地形观察块数，Pending 的 work 为本轮计划出队条数（包括失效条目）、gauge 为处理前队列长度，WetBatch 的 work/gauge 为本轮湿物件快照规模。`MovedItems.work` 累计已提交漂流次数，`PendingWetChecksMerged.work` 累计交给同轮湿物件批次统一检查的队列条数；它们不是材料堆量。Marker 的调用次数可区分漂流前检查与移动后检查，移动后复查不能当作重复查询全部删掉。
