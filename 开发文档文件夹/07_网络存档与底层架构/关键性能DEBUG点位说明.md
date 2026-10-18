# 关键性能 DEBUG 点位说明

本轮只补观察入口，不改拾取、手持创建、天气积分、导航发布、存档与太空复制的业务规则，也不提高执行间隔。可以用同一套点位定位“捡一堆 200 个雪球”的冷创建尖峰，以及探索、机器、保存和联机中的持续开销。Unity 当前 PlayMode 保持原样，本轮未在 Editor 编译、刷新或执行验收；磁盘代码需后续导入后生效。

## 1. 怎样开关

1. PC 用现有 F4 / GM 入口打开工具，手机从现有日志悬浮窗的 GM 入口进入。
2. 进入“设备数据”，找到“性能 DEBUG”；也可在 GM 全局搜索中输入“性能”。
3. 打开“性能统计”，复现一个短场景，再点“输出性能摘要”。页面显示同一份摘要，并写入现有日志。
4. 需要抓偶发长操作时打开“慢操作摘要”，输入阈值毫秒并应用。默认 16.67 ms，可设 0.1～10000 ms；只有出现超阈值操作才输出，每秒最多一条合并摘要。
5. 对照下一组操作前点“清空性能统计”。清空保留开关与阈值；重新进入运行会话默认关闭统计和慢摘要，不自动沿用上轮开关。

Provider 仍为 `runtime-debug`，新增键为 `runtimeDebug.performanceStatistics`、`runtimeDebug.performanceSlowSummary`、`runtimeDebug.performanceThresholdMs`。GM 与通用设置契约读取同一状态，原日志悬浮窗设置保留。性能开关与日志悬浮窗开关相互独立。

## 2. 数字怎么读

| 字段 | 意义 |
| --- | --- |
| `calls` | 当前统计轮中完成的计时区段次数，异常或早退也由作用域收尾；不是物品个数 |
| `work` | 本点实际声明/批次追加的累计工作量，具体单位见下面的表；只有计数的点 `calls` 可以为 0 |
| `gauge` / `peak` | 最近观察的当前规模 / 本轮最高观察规模，不是累计新增量；静态点可跨多个实例汇总观察 |
| `total` / `max` | 完成区段的包含式总毫秒 / 单次最高毫秒；嵌套点不能相加当作整帧耗时 |
| 慢摘要 `slow` / `inputWork` | 本批超阈值区段次数 / 在 Measure 开始时已声明的输入规模；没有声明输入规模时省略，不代表没有工作 |

批次结束后才知道的数量通过 `AddWork` 进入累计 `work`，不反向关联单条慢事件。`SetGauge` 只在正常业务边界观察，不主动扫描全世界缓存。关掉统计仍可单独开慢摘要，此时不会累积 `calls/work/gauge`。

Profiler 的 **Total** 包含所有子调用，**Self** 只排除有独立 Marker 的子区段；未单独埋点的 JIT、原生调用或数据准备可能仍落在 Self。摘要使用 Stopwatch 墙钟时间，包含区段内同步等待和已启用的计数采集成本，不是纯 CPU 指令时间；GC 字节仍从 Unity Profiler 的实际调用树看，本工具不猜 GC 归属。

第一次进入代码/首次建壳/首次显示属于冷路径，第二次相同操作可用来观察暖路径。一次 200 个雪球是一份数量为 200 的堆叠，不等于 200 次 Instantiate。PC Editor / Mono 的 JIT 不能直接外推安卓 IL2CPP；应分别看事务次数、建壳、组件、配置轮次和池命中原因。

## 3. 拾取、手持、装配与感知

下表的 `{A,B}` 表示相同前缀下的两个固定 Marker 名，不是在运行时拼接 Marker。

| 固定前缀与点位 | 定位与单位 |
| --- | --- |
| `FlatWorld.Pickup.{DroppedBatch,DroppedTransaction,PayloadClone,DroppedFeedback,AcceptTransaction,InventoryAdd,InventoryRefresh,NotifyCommitted}` | 掉落候选、整堆转移、克隆、入包与通知。DroppedBatch.work 为附近候选记录数，其余为事务/实际调用次数，不是物品单位数 |
| `FlatWorld.Pickup.{DroppedCommittedStacks,DroppedRejectedStacks,CapacityRejectedTransactions,InventoryRejectedTransactions}` | 只累计成功堆叠、拒绝堆叠或拒绝事务数 |
| `FlatWorld.Pickup.{SourceStackWholeUnits,AcceptedWholeUnits}` | gauge 为最近有效请求的原始堆量（不乘倍率）/ 最近成功加入量（含既有倍率，拒绝时保留前值），截断小数并钳制 int.MaxValue；不是累计量或实例数 |
| `FlatWorld.Hotbar.{SyncHeld,LoadHeld,UnloadHeld,ConfigureHeld}` | 当前槽同步与手持生命周期。`Dirty.*` 统计标脏通知，`Decision.*` 区分缺实例创建、换数据引用、同引用复用、空槽；标脏不保证每次实际同步 |
| `FlatWorld.ItemPool.{Acquire,Return,ValidateComponents}` | 取还与池资格检查；ValidateComponents.work 为检查组件数。Hits/Misses/DeadEntries/AcceptedReturns 为真实事件计数，Rejected.* 为互斥拒收原因；AvailableInstances.gauge 为最后全池空闲量 |
| `FlatWorld.ItemSpawn.{InstantiateRuntime,ColdFactoryWithInitialConfigure,ConfigureFinalData,Register,AttachWorld,InstantiatedCallbacks}` | 区分冷工厂的初始配置与最终数据配置；这两轮原行为仍保留 |
| `FlatWorld.ItemDefinition.{ConfigureInstance,ApplyVisual,EnsureModules}`、`FlatWorld.ItemModuleAssembly.{InstantiateModule,InstantiatePrefab}` | 定义配置、视觉、模块复用/新建。EnsureModules.work 为定义模块数；ReusedModules/DataOnlyModules 为解析分支次数 |
| `FlatWorld.Item.{Load,LoadModuleLayout,ModuleInitBatch,BindModuleDependenciesBatch,LoadModulesBatch}` | 模块初始化、依赖绑定和加载。Batch.work 为计划模块数，回调提前终止不表示全部完成 |
| `FlatWorld.Snowball.{ResolveTarget,ShowOutline}`、`FlatWorld.TargetOutline.{Create,InitializeRenderer}` | 选格与首次轮廓创建。CreatedObjects/CreatedComponents/CreatedMaterials/DestroyedMaterials 为真实表现资源数；一个框通常是 1 个 GO、2 个组件、1 个材质，不等于 Object.Instantiate 次数 |
| `ItemMgr.Perception.{JobComplete,Apply,FilterCandidates,DetectorApply,Clear}` | 保留旧名。Apply.work 为检测器数，FilterCandidates 为原始候选数，DetectorApply 为交给回调的候选数；版本过期仍可能丢弃 |
| `FlatWorld.Perception.*` | 原始/有效/无效/重复、Bridge、LOS、最终结果及过期批次的整批累计。Valid 包含之后被 Bridge/LOS 淘汰的候选；LOSChecks 为接口调用数，关闭阻挡可直接通过，不是格子射线次数 |

## 4. 天气、温度、Flow、流送、机器与 HUD

| Marker | work / gauge 的关键边界 |
| --- | --- |
| `FlatWorld.Weather.{AdvanceRegions,SurfacePrepare,AdvanceSurface}` | 实际区域数 / 实际入队块数 / 本批出队工作笔数；后两者 gauge 分别为入队后、推进后队列长度，context 区分 budget、flush、autosave |
| `FlatWorld.Weather.{SurfaceChunk,SurfaceExposure,SurfaceTemperatures,SurfaceCommit}` | 有效块处理尝试数 / 全扫候选格数 / 活动温度格数 / 活动提交候选格数；SurfaceCommit 包含计算、雪层写入和通知，不是纯存档写入 |
| `FlatWorld.Weather.{SurfaceWater,PersistSurfaceChunk,SurfaceNotify}` | 供水请求格数 / 土壤与雪两个变化列表长度之和 / 供水请求格数；Persist 数量不是去重格数 |
| `FlatWorld.Temperature.CaptureAmbientChunk` | 成功资格检查后冻结的块数；gauge 为目标格数 |
| `FlatWorld.Temperature.{RebuildLocalChunk,RebuildRadiantLiquidChunk}` | 本地源×目标格的权重次数 / 参与完整块重建的液体源数；gauge 为本地源总量 / 目标桶数 |
| `FlatWorld.Temperature.{RadiantWeightEvaluations,RadiantSourceCache,RadiantCache.Hit,RadiantCache.Miss}` | 权重次数 / 来源桶 gauge / 完整块权重缓存命中、未命中次数；Hit/Miss 不含单点及单点批次查询 |
| `FlatWorld.Navigation.Flow.{Read,CompleteReaders,FieldsBuild,FieldsBuild.Complete}` | 待读脏块数 / 主线程等待读 Job / 256 格局部场张数 / 本批场张数；FieldsBuild 包含准备与同步等待，不能当成 worker 实际 CPU 时间 |
| `FlatWorld.Navigation.Flow.PublishChunks.{Full,Patch}` | 全量重发 / 补丁块数；gauge 为当前出口数 |
| `FlatWorld.Navigation.Flow.{PublishGoals,Routes.Schedule,SelectExit.Schedule,PublishGoals.Complete}` | 分配的目标槽数（含非活跃槽） / 调度目标数 / 目标×块组合数 / 等待目标数；gauge 分别观察需重路由槽、出口、块数，context 区分 graph/goals |
| `FlatWorld.ChunkStreaming.{CommitAndTick,PresentationStartBatch,PresentationStepBatch}` | 提交推进 / 本帧实际出队启动尝试 / 实际续步。后两者 work 为尝试数，gauge 为对应剩余队列；计时不跨 yield |
| `MachineWorld.{Tick,Fluid.Begin,Logic.Advance,Fluid.Complete}` | 追赶步数 / 候选流体组数 / 实际 SimulateNode 次数 / 候选流体边数；gauge 分别观察节点、网络、冻结库存。Logic 不含 AdvanceFacilities 内部节点；原 Transport/Phase/Tanks/Appearance 子点保留 |
| `FlatWorld.HUD.{Sample,TemperatureSample,PressureFormat}` | 刷新尝试 / 温度读取 / 压力组文案尝试；压力点包含原有读数、本地化与格式化，不因此改变原逻辑 |
| `FlatWorld.Rendering.ZoomClouds.{PassSetupCPU,RecordCommandsCPU}` | 准备 / 命令录制与提交次数；仅为 CPU 区段，原 `FlatWorld Zoom Clouds` GPU 采样器保留，不能把 CPU 毫秒当 GPU 毫秒 |

## 5. 保存与太空网络

| Marker | 定位与单位 |
| --- | --- |
| `FlatWorld.Save.Capture.Sync` | 同步保存的运行态采集；work 为调用旧 Chunk.SaveChunk 的块数，gauge 为差量记录数，不覆盖所有新后端物品数 |
| `FlatWorld.Save.{Capture.Slice,Encode.Slice}` | 自动保存每次 MoveNext 的同步推进次数；不包含 yield 之间等待，不表示每次都编码了一个区块 |
| `FlatWorld.Save.Capture.Core` | 同步路径的机器、太空、核心和掉落捕获；work 为合成载荷字节数，包含 Encode.Core 子段 |
| `FlatWorld.Save.Encode.{Sync,Core,Chunks,BackgroundEnvelope}` | 完整同步编码 / 核心编码 / 同步区块编码 / 后台冻结载荷封装。Chunks.work 为记录数，其余 work 为输出字节；后台不访问游戏对象 |
| `FlatWorld.Save.Write.{Atomic,BytesAndFlush}` | 原子写入总段（含文件锁等待）/ 实际文件 Write 与强制 Flush。Atomic.gauge 为本次载荷字节，BytesAndFlush.work 在 Write 成功返回后累计字节；后续 Flush 失败仍保留已写入量，不等于最终保存成功 |
| `FlatWorld.Save.NetworkSnapshot.GZip` | 入世快照的压缩与字节导出；work 为输入原始字节 |
| `FlatWorld.Space.Network.{Batch,Send}` | 0.2 秒周期批次 / 单连接发送总段；Batch.work 为处理的远端连接数，gauge 为服务器连接表规模（可含 Host）。操作触发的即时发送也进入 Send，原频率不变 |
| `FlatWorld.Space.Network.{CaptureContents,Filter,Json}` | 捕获船体/散落物内容、按观察者筛选、JSON。Filter.work 为各主要候选列表长度之和（不含全部嵌套 Pieces/装配展开次数），gauge 为入选船数；Json.work 为主要输出对象数（不含全部嵌套对象），gauge 为 JSON 字符数 |
| `FlatWorld.Space.Network.{Utf8,GZip,FragmentSend,PayloadBytes}` | UTF8 编码字符数 / 压缩输入字节 / 已提交分片数 / 已提交压缩载荷字节。Utf8.gauge 为编码字节，GZip 为压缩字节，FragmentSend 为计划分片数；字节不含 Mirror/传输头，不等于已送达 |
| `FlatWorld.Space.Network.{Assemble,Expand,Apply}` | 客户端压缩片拼接 / 解压 / 应用快照；前两项 work 为压缩字节，Apply 为展开字节，包含 UTF8 解码与正式状态应用 |

## 6. 开发约束与覆盖边界

公共 API 在 [PerformanceDebugPoint.cs](../../Assets/5_Scripts/Shared/Utilities/PerformanceDebugPoint.cs) 和 [PerformanceDebugRegistry.cs](../../Assets/5_Scripts/Shared/Utilities/PerformanceDebugRegistry.cs)，属于 `FlatWorld.Utilities`。生产系统单向引用通用诊断，不能反向引用 `FlatWorld.Gameplay.Debug`。

固定 `static readonly PerformanceDebugPoint` 持有 Marker；`Measure(long workUnits=0, string context=null)` 返回 struct，必须用 using/finally 成对结束，不跨 yield/await，不转成 IDisposable 装箱。context 只用固定字面量或已有稳定字符串，不在热路径拼接。按已有批次累计数量，不逐格/逐候选建 scope，不改变集合遍历顺序，也不为诊断额外枚举集合。后台批次用返回 scope 的 `AddWork/SetGauge` 绑定开始时的代际；Point 自身同名方法记录当前独立事件，不能拿它给跨重置的旧后台任务补量。

默认两开关关闭时，稳定调用路径不额外读取 Stopwatch、不逐次创建采样对象或输出日志；ProfilerMarker 仍按 Unity 的构建/Profiler 开启规则工作。固定点位首次初始化仍有一次性的对象注册和缓冲初始化成本，比较冷启动数据时应计入观察扰动。开启收集后会增加计时与同步成本，应在同一诊断配置下对照。后台写盘只用线程安全 ProfilerMarker、Stopwatch 和数字缓冲，不用 Time、Unity 对象或 Debug.Log。Scope.Dispose 不输出日志；最多登记 256 个点位、缓冲 128 条慢事件，超额事件计丢弃数，主线程只复制快照后再离锁排版。

清空/运行会话重置会递增代际，旧 scope 的时间及其 AddWork/SetGauge 都不能写回新轮；Point 自身的独立计数按事件发生时归属本轮。关闭域重载时保留静态 readonly 点位注册。慢摘要泵跨场景只保留一份，关闭慢摘要清待输出缓冲，退出游戏停止输出。新增 `ui_settings/performance_debug` Lua 规则覆盖正式聚合、嵌套/异常、当前与峰值规模、清空/重置代际及后台计数，未在 Unity 编译、未执行 Lua。

本轮没有证明零分配实测、耗时改善、真机温升/长时内存、GPU、多人收发、存档恢复或 GM/手机最终布局通过；这些由用户后续验收。诊断数据只提供定位线索，成功、拒绝、保存完成等结论仍看原业务结果。

外部 C# 静态编译已通过 Utilities、Navigation、GamePlay、GM Debug、Networking.Gameplay 及相关依赖，检查基线为 `3f2f40dc` 加本轮 45 个路径的诊断增量。仅在临时验证副本中沿用任务开始前已经存在、SHA 未变的 `Mod_AI_Fish.cs` 单行 Func→Predicate 修正，该行不纳入本轮提交。此结果只对应这份独立源码快照；后续基线变动需另行核对。Unity 没有导入、编译或执行 Lua，PlayMode 与上述用户验收边界不变。

实现参考：[Unity 2022.3 ProfilerMarker.Auto](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerMarker.Auto.html)、[ProfilerMarker.Begin](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerMarker.Begin.html)、[Stopwatch.GetTimestamp](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch.gettimestamp)。

## 7. 2026-10-18 两份导出疑点的后续诊断

本节记录两份性能导出分析之后新增的观察点与小范围修复。前面第 6 节的外部编译结果属于当时的源码快照，**不覆盖本节新增代码**。本次按用户要求只做静态修改，由用户验收；未启动编译、Lua、PlayMode 测试或重新采样，也未操作用户 Unity 的运行/暂停状态。

### 7.1 河流局部细化

此处保留纯 WorldModel 的依赖边界：固定 ProfilerMarker 看线程调用树，数字复用原有 `ChunkGenerationTiming`，不额外引用通用 DEBUG 聚合器。现有主线程 `[ChunkGenSlow]` 输出会遍历全部阶段与计数，因此原有日志导出即可携带这些字段；没有为每格新增日志或作用域。

| 入口 | 含义 |
| --- | --- |
| `FlatWorld.Generation.River.LocalRefine` | 当前区块的河流局部细化总段 |
| `FlatWorld.Generation.River.Centerline` / `river.local_refine.centerline` | 扫描宏观节点、筛选河段并采样中心线 |
| `FlatWorld.Generation.River.Render` / `river.local_refine.render` | 中心线扩展水格与冲积带的总段 |
| `river.local_refine.channel_fill` / `river.local_refine.floodplain` | 在原中心点循环中分别累计水格填充、冲积带处理耗时，正常结束后批量写入本区块诊断 |
| `FlatWorld.Generation.River.TerminalLakes` / `river.local_refine.terminal_lakes` | 终点候选与终点湖处理；关闭终点湖时仍可观察到零候选 |
| `river.local_refine.height_compute` | 同一局部采样上下文中，高度缓存未命中的真实高度计算累计耗时，是多个子段中的交叉子集 |

`river.local_refine.*` 数字计数单位如下：

| 字段后缀 | 含义 |
| --- | --- |
| `invocations` / `region_nodes` / `output_padding_last` | 进入局部细化次数 / 各次扫描的宏观节点累计量 / 最近一次输出外圈宽度（格） |
| `segments` / `curve_samples` / `accepted_curve_samples` / `unique_centers` | 入选河段 / 计划曲线采样点 / 落入处理范围的曲线采样点 / 去重后的中心格 |
| `channel_candidates` / `channel_outside_window` / `channel_ocean_rejected` / `channel_merge_attempts` | 通过圆形范围筛选的候选 / 输出窗口外拒绝 / 海面以下拒绝 / 实际合并尝试；最后一项不等于新增唯一水格 |
| `floodplain_candidates` / `floodplain_merge_attempts` | 通过冲积带圆形范围筛选的候选 / 实际强度合并尝试；可重复覆盖同一格 |
| `water_cells` / `floodplain_cells` / `terminal_candidates` | 最终唯一河湖格 / 唯一冲积格 / 达到终点流量条件的候选，候选不等于最终成湖数 |
| `height_cache_hits` / `height_cache_misses` / `bend_cache_hits` / `bend_cache_misses` | 同一局部上下文的高度与河道弯曲场缓存命中、未命中次数 |

数字只在该请求已有 `ChunkGenerationTiming` 时采集；Editor/Development Build 原有请求诊断与 GM 通用统计开关独立，关闭 GM 统计不等于关闭区块日志。没有请求诊断时，新增循环统计不读取 Stopwatch。原有高度未命中计时逻辑保留。

生态宿主邻区查询会在同一外层请求诊断下多次进入细化；父 `river.local_refine`、本节子段和工作量统一累计，`output_padding_last` 只取最后一次。`unique_centers/water_cells/floodplain_cells` 是每次局部去重后的数量之和，**不是整个请求跨邻区去重的世界唯一格数**。`invocations` 在进入时计数，其他计数按相应阶段完成后写入；取消/异常可能留下未完成阶段。`render` 包含 `channel_fill/floodplain`，`height_compute` 又分布在它们和终点湖等处理中，**不可把这些值相加得到总耗时**。后台耗时也不等于主线程阻塞。

下一份导出可据此判断：centerline 高时看宏观节点与曲线样本；floodplain 高时看候选相对唯一冲积格的放大量及 padding；height_compute 高时看未命中数；height_compute 较低而 render 高时，继续看候选覆盖、字典合并与其他实际工作。这里只提供定位线索，不根据单个比例自动认定算法有错。

已做的小优化是终点湖同格高度只读取一次，资格判断与深度计算复用相同缓存值，省去重复归一化和字典读取；另修复诊断归属：同一外层请求的多次细化不再只保留最后一次明细。生成参数、遍历顺序与数值规则均未修改，没有声称已经修复先前 397～1301 ms 的主要耗时。

### 7.2 角色材质效果、覆雪与天然实体环境冻结

| 点位 | 含义与单位 |
| --- | --- |
| `FlatWorld.ActorRender.Frame` | 一次效果控制器帧处理，包含绑定刷新、模块推进与可能发生的 Renderer 提交 |
| `FlatWorld.ActorRender.UpdateModules` | 本次模块推进；work 为模块列表长度，包含覆雪 PrepareFrame 等子调用 |
| `FlatWorld.ActorRender.SubmitRenderers` / `RefreshBindings` | Renderer 提交遍历 / 绑定刷新；work 为进入该遍历的 Renderer 数 / 刷新后的 Renderer 数 |
| `FlatWorld.ActorRender.{IdleSkippedFrames,UnchangedSpriteRenderers,SubmittedRenderers}` | 整轮静态跳过次数 / 遍历中 Sprite 未变而跳过的 Renderer 次数 / 实际提交 MPB 的 Renderer 次数；不是唯一对象数 |
| `FlatWorld.ActorRender.Snow.SampleCoverage` | 实际 GetSnowCoverage 调用与覆雪值提交；work 为真实天气查询次数，移动或 WeatherMgr 不存在时不进入此点 |
| `FlatWorld.ActorRender.Snow.{CooldownSkips,ExcludedSamples,CoverageChanges}` | 未到采样时刻的跳过 / 身份、持有等资格不符 / 覆雪值实际变化次数 |
| `NaturalEntityRuntime.FreezeEnvironment` | 天然实体环境输入冻结总段；work 为本次自然物数，gauge 为最近定时批次规模 |
| `NaturalEntityRuntime.FreezeEnvironment.{Weather,Temperature,WriteInputs}` | 天气读取 / 温度读取 / 纯环境输入写入；仅原生 ProfilerMarker，不逐物件进入通用统计锁 |
| `NaturalEntityRuntime.Environment.{WeatherSamples,TemperatureSamples}` | 天气、温度读取尝试量；批次内累计后一次发布，不等于成功数 |
| `NaturalEntityRuntime.Environment.{MissingWeather,MissingTemperature,InputWrites}` | 读取失败量 / 实际输入写入数 |

`Frame` 包含 Modules/Submit/Bindings，Snow 采样属于 Modules；天然实体总段包含 Weather/Temperature/WriteInputs。父子 Total 均不能相加。先用总段、处理规模确认成本，再用子段与真实提交/查询数判断静态跳过是否生效、成本是否来自采样或状态提交。保留原覆雪采样频率和天然实体模拟规则，本次没有重新采样证明收益。

### 7.3 掉落物水态与重复环境检查

| 点位 | 含义与单位 |
| --- | --- |
| `DroppedItems.Water.CleanupTerrainWatches` | 观察块清理；work/gauge 为本批地形观察块数 |
| `DroppedItems.Water.PendingEnvironment` | 环境队列处理；work 为计划出队条数，gauge 为处理前队列长度，旧条目失效时不保证发生实际环境查询 |
| `DroppedItems.Water.WetBatch` | 已知湿物件处理；work/gauge 为处理开始时的湿物件快照规模，后续可因失效或环境变化跳过 |
| `DroppedItems.Water.{MovedItems,PendingWetChecksMerged}` | 累计漂流提交次数 / 已有湿物件排队检查合并条数，均不是唯一物件数 |
| `DroppedItems.Water.{CheckEnvironment,TerrainSample,EntryTransform,ResolveBuoyancy,SampleCurrent,ResolveDestination,CommitDrift}` | 环境检查、地形采样、入水转换、浮沉、流向采样、目的地解析、漂流提交的原生 Profiler 子段；CommitDrift 包含移动后位置的环境复查 |

已知湿物件从排队阶段合并到紧接着的湿物件批次，省去同轮、同位置的重复地形查询；漂流后的环境复查保留。原水态执行间隔、入水、浮沉与流动参数不变。先对比队列、湿物件规模和合并量，再看子段；Pending 的计划数不等于实际 CheckEnvironment 次数。

### 7.4 导出补抓附近尖峰与累计 DEBUG 快照

导出格式升级到 schema 3，原选中帧 `selectedFrameAnalysis/callTrees` 保留。`additionalFrameDetails` 额外保存附近超过 50 ms、按耗时排序最慢的最多 3 帧的帧耗时、完整聚合线程树与读取状态，排除已单独保存的选中帧。帧已从 Profiler 缓冲移出或读取失败时，按状态报告缺口，不能把缺失树理解为没有成本；这也不能补回早先两份旧导出未记录的 218/119 ms 调用树。第一份 EditorLoop 未展开的问题仍依赖实际录制详细度，导出器不会改变录制配置。

`performanceDebugAtExport` 是导出时只读获取的当前 Editor 进程累计 registry 快照，包括当时开关与累计字段；它不是远程 Player 的统计。它不属于选中帧，也不能精确对应其中某一条尖峰；不清空统计、不消费慢事件、不改变开关。统计默认关闭，未提前开启时不能事后补回数字；关闭状态也可能保留之前开启阶段的累计量，既不代表本帧零开销，也不代表一直处于采集状态。

下一轮可先从 F4/GM 的“设备数据 → 性能 DEBUG”打开“性能统计”，需要慢操作时再开“慢操作摘要”；开始一段短复现前点“清空性能统计”，复现后使用原 Profiler 导出入口。导出器会附上当时累计快照，Profiler Marker 则按 Unity 的采样规则进入调用树。暂停或恢复仍由用户操作，本次没有替用户改变状态。

本节新增 `terrain/river_diagnostic_aggregation` Lua 规则：独立 8×8 地形通过公开 BeginRequest/Generate/Capture，两次正式生成共享同一诊断，检查调用与工作量累计、最近输出规模不累计、各内部阶段存在；不以缓存耗时大小作为断言。它与既有 `terrain/cached_generation` 的实际地形确定性断言互补。本次只编写、静态检查，未编译或执行；掉落物与渲染的具体覆盖以各系统规则及本轮交付说明为准。
