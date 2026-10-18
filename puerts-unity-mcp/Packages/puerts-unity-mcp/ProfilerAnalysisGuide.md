# AI 性能分析前置说明

你正在分析 FlatWorld 导出的离线证据。先按下面顺序读取，只展开相关数据；用户导出后可以继续游戏，不需要保持 Unity 暂停。

## 选对报告

- 用户指定文件夹时只分析该目录；只说“最新”时，在 `Assets/67_ProfileData/` 中选时间最新且同时有两个 JSON 的正式目录，忽略 `.pending-*`。不要混用不同目录的文件；缺少截图时说明缺口，不擅自换成另一帧。
- `profiler-analysis.json` 是性能证据，`console-logs.json` 是日志，`screen.png` 是导出时的整个桌面；本文件是阅读说明。
- 两个 JSON 的 `snapshotId`、`selectedDisplayFrame` 应相同。`schemaVersion` 分别描述各自格式，不要求两个版本号相等。先检查性能文件 `warnings`、`selectedFrameAnalysis.success/frameCount/skippedFrameCount`，缺数据时明确说明。

## 按需读取，控制上下文大小

1. 用 JSON 解析器读取文件，先输出元信息、`selectedFrameAnalysis.frameSummary`、`warnings`，以及 `topSelfTimeMarkers`、`topGcMarkers` 各前 10 项；不要直接打印完整 JSON 或整个 `callTrees`。
2. 有 `screen.png` 时看一次，确认可见的场景和 Profiler 状态。截图与日志都是导出时的现场，不能证明它们发生在所选录制帧。
3. 对比 `nearbyFrames` 中选中帧与邻帧耗时，区分尖峰和附近持续慢帧；邻帧仅有耗时，没有邻帧的调用树。
4. 从排行挑最有影响的 1～3 个方法，再按线程查 `callTrees` 中同名节点，沿 `parentId` 向上还原调用链，并查看直接子节点。每次只输出相关链及最耗时的几个子节点。
5. 日志先按 `entries[].severity` 计数，按正文/堆栈合并重复项；优先读 Error/Exception/Assert 和与热点同名的模块日志，再按需展开完整堆栈。不要只读错误，普通 Debug 日志可能包含分阶段计时和业务计数。
6. 有项目源码时，按方法名、Marker 名或日志 `file/line` 定位正式实现和调用方；不要为了分析已有文件重新采集、暂停或切换用户的 Unity。

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
| `callTrees[].threadIndex/name/nodes` | 每个线程完整导出的聚合调用树，节点数量不限于排行的 100 项 |
| `nodes[].id/parentId/name` | id 只在当前线程内有效；`parentId=-1` 为根；同父节点下的同名调用已经合并 |
| `nodes[].totalMs/selfMs/maxCallMs/calls` | 包含子调用的累计耗时、自身累计耗时、最长单次调用耗时、调用次数 |
| `nodes[].gcBytesWithChildren/gcAllocationsWithChildren` | 包含子调用的分配字节与次数，依赖录制中实际存在的 GC.Alloc 元数据 |
| `nearbyFrames[].displayFrame/available/timeMs` | 前后最多各 30 帧的耗时；`available=false` 表示该项不可用于比较 |
| `screenshot.file/capturedAtUtc/width/height` | 图片文件名、截图时间与像素尺寸 |
| 日志 `entries[].index/severity/message/stackTrace/file/line/rawText` | 日志索引、级别、正文、堆栈、源码位置及完整原文；原文可能自带业务时间戳 |

## 避免误判

- `totalMs` 包含子调用，父子耗时不能相加；多个线程的耗时也不能相加当成帧耗时。递归方法的累计 inclusive 时间可能重复覆盖同一时间段。
- 跨线程的方法排行用于筛选热点，归因时回到对应线程的调用树。较大的 inclusive 时间配合很小的 self 时间，通常需要继续看子节点。
- 祖先节点的 GC 数已包含后代，不能层层相加。GC 数为零不证明“没有分配”，也可能没有采集到相应元数据。
- `EditorLoop`、`Profiler.ParseThreadData`、`Gfx.Wait*`、VSync 等编辑器/等待标记要与游戏业务分开；等待时间大不能直接证明 GPU 或某个脚本就是根因。CPU 调用树不提供 GPU 时间线。
- `deepProfilingAtExport`、`profilerTargetAtExport`、运行/暂停状态描述导出时设置；加载的历史录制可能不同。深度分析会影响采样成本，不能把一次 Editor 录制当成正式 Player 基准。
- Console 没有逐条日志对应的 Profiler 帧号或原生时间戳；`selectedDisplayFrame` 是这次导出的关联标识，不是每条日志的发生帧。已清空日志不能恢复。
- 证据文件只支持定位和提出方案，不能单独证明修改后的性能改善；实际效果由用户在相同场景下验收。

## 回答方式

先写最值得处理的 1～3 个问题，每项给出“线程 → 调用链、耗时/调用次数/GC、相关日志、源码位置、优化方向”。分清已经看到的证据与仍需确认的原因，不整段复述原始 JSON。只有用户要求修改时再改代码；遵守项目模块边界，优先修重复计算、数据访问和分配来源，不靠不断拉长执行间隔掩盖开销。
