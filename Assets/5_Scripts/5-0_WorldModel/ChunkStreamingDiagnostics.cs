using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 世界会话级流送诊断：只记录阶段边界，不参与调度；每项保留最近 128 次耗时、最近 64 个结果。
    /// 耗时是 Stopwatch 单调墙钟，不是 CPU 利用率或 GPU 时间；父子阶段重叠，不能相加。
    /// </summary>
    public sealed class ChunkStreamingDiagnostics
    {
        #region 有界统计

        private const int SampleCapacity = 128;
        private const int RecentCapacity = 64;
        private const int MetricCapacity = 64;
        private readonly object gate = new object();
        private readonly Dictionary<string, Metric> metrics = new Dictionary<string, Metric>();
        private readonly Dictionary<string, long> counters = new Dictionary<string, long>();
        private readonly Queue<ChunkGenerationDiagnostic> recent = new Queue<ChunkGenerationDiagnostic>();
        private int pauseRevision;
        private bool suspended;

        /// <summary>主线程完成慢区块提交时通知 Unity 宿主输出一次阶段日志。</summary>
        public event Action<ChunkGenerationDiagnostic> SlowGenerationCompleted;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool Enabled => true;
#else
        public bool Enabled => false;
#endif
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public int PauseRevision { get { lock (gate) return pauseRevision; } }
        public bool Suspended { get { lock (gate) return suspended; } }

        private sealed class Metric
        {
            public readonly double[] Samples = new double[SampleCapacity];
            public long Count;
            public double TotalMs;
            public int Used;
            public int Next;
        }

        /// <summary>只包住同步工作，不得跨协程 yield 持有计时作用域。</summary>
        public readonly struct Measurement : IDisposable
        {
            private readonly ChunkStreamingDiagnostics owner;
            private readonly string name;
            private readonly long started;

            internal Measurement(ChunkStreamingDiagnostics owner, string name)
            {
                this.owner = owner;
                this.name = name;
                started = Stopwatch.GetTimestamp();
            }

            public void Dispose() => owner?.Record(name, ElapsedMs(started));
        }

        public Measurement Measure(string name) => Enabled ? new Measurement(this, name) : default;
        public static double ElapsedMs(long start, long end = 0) => start <= 0 ? 0d :
            Math.Max(0d, ((end > 0 ? end : Stopwatch.GetTimestamp()) - start) * 1000d / Stopwatch.Frequency);

        /// <summary>每个真实阶段结束时写一次；后台线程不得访问 Unity 对象。</summary>
        public void Record(string name, double milliseconds)
        {
            if (!Enabled) return;
            lock (gate)
            {
                if (!metrics.TryGetValue(name, out Metric metric))
                {
                    if (metrics.Count >= MetricCapacity) return;
                    metrics.Add(name, metric = new Metric());
                }
                metric.Count++;
                metric.TotalMs += milliseconds;
                metric.Samples[metric.Next] = milliseconds;
                metric.Next = (metric.Next + 1) % SampleCapacity;
                metric.Used = Math.Min(SampleCapacity, metric.Used + 1);
            }
        }

        public void Count(string name)
        {
            if (!Enabled) return;
            lock (gate)
            {
                if (!counters.TryGetValue(name, out long count) && counters.Count >= MetricCapacity) return;
                counters[name] = count + 1;
            }
        }

        /// <summary>Editor 暂停/恢复仅标记计时污染，不驱动或暂停正式生成。</summary>
        public void SetSuspended(bool value)
        {
            lock (gate)
            {
                if (suspended == value) return;
                suspended = value;
                pauseRevision++;
            }
        }

        public ChunkGenerationTiming BeginRequest(ChunkGenerationRequest request) =>
            Enabled ? new ChunkGenerationTiming(this, request) : null;

        public void Complete(ChunkGenerationTiming timing, string outcome)
        {
            if (!Enabled || timing == null) return;
            ChunkGenerationDiagnostic snapshot = timing.Finish(outcome);
            Count("commit." + outcome);
            lock (gate)
            {
                if (recent.Count == RecentCapacity) recent.Dequeue();
                recent.Enqueue(snapshot);
            }
            if (!snapshot.PauseAffected &&
                (snapshot.GenerationWorkMs >= 500d || snapshot.GenerationQueueMs >= 2000d))
                SlowGenerationCompleted?.Invoke(snapshot);
        }

        /// <summary>仅查询时复制/排序，正常帧不分配诊断 JSON 或日志字符串。</summary>
        public object Capture()
        {
            lock (gate)
            {
                var summaries = new List<object>(metrics.Count);
                foreach (KeyValuePair<string, Metric> pair in metrics)
                {
                    Metric m = pair.Value;
                    var samples = new double[m.Used];
                    Array.Copy(m.Samples, samples, m.Used);
                    Array.Sort(samples);
                    double sum = 0d;
                    for (int i = 0; i < samples.Length; i++) sum += samples[i];
                    summaries.Add(new
                    {
                        stage = pair.Key, totalCount = m.Count, totalMs = m.TotalMs,
                        recentCount = m.Used, recentAvgMs = sum / m.Used,
                        recentP95Ms = samples[Math.Max(0, (int)Math.Ceiling(m.Used * 0.95d) - 1)],
                        recentMaxMs = samples[m.Used - 1]
                    });
                }
                return new
                {
                    enabled = Enabled, sessionId = SessionId, pauseRevision, suspended,
                    metrics = summaries, counters = new Dictionary<string, long>(counters),
                    recentCompleted = recent.ToArray()
                };
            }
        }

        #endregion
    }

    /// <summary>单次请求的跨线程时间线；同地址重请求以 epoch/version 区分，不能混用上一次记录。</summary>
    public sealed class ChunkGenerationTiming
    {
        #region 请求时间线

        [ThreadStatic] private static ChunkGenerationTiming currentOnThread;
        /// <summary>Lazy 可能由任意等待线程执行，因此区域内部计时归实际执行者。</summary>
        internal static ChunkGenerationTiming CurrentOnThread
        {
            get => currentOnThread;
            set => currentOnThread = value;
        }

        private readonly object gate = new object();
        private readonly ChunkStreamingDiagnostics owner;
        private readonly ChunkGenerationRequest request;
        private readonly long requested = Stopwatch.GetTimestamp();
        private readonly int pauseRevision;
        private readonly bool startedSuspended;
        private long started, finished, enqueued, cancelled, commitStarted, handled;
        private string outcome;
        private readonly Dictionary<string, double> stageMilliseconds = new Dictionary<string, double>();
        private readonly Dictionary<string, long> stageCounts = new Dictionary<string, long>();

        /// <summary>纯生成阶段的计时作用域；后台线程只记单调时间，不访问 Unity。</summary>
        public readonly struct StageMeasurement : IDisposable
        {
            private readonly ChunkGenerationTiming timing;
            private readonly string stage;
            private readonly long started;

            internal StageMeasurement(ChunkGenerationTiming timing, string stage)
            {
                this.timing = timing;
                this.stage = stage;
                started = Stopwatch.GetTimestamp();
            }

            public void Dispose() => timing?.RecordStageDuration(stage, ChunkStreamingDiagnostics.ElapsedMs(started));
        }

        /// <summary>在阶段边界开始计时，结果随请求时间线一起交给主线程。</summary>
        public StageMeasurement MeasureStage(string stage) => new StageMeasurement(this, stage);

        /// <summary>把循环内累计的采样耗时合并为一次阶段记录。</summary>
        internal void RecordStageDuration(string stage, double milliseconds)
        {
            lock (gate) stageMilliseconds[stage] = milliseconds;
            owner.Record("generation.stage." + stage, milliseconds);
        }

        /// <summary>记录路由访问格数和实际噪声采样次数，便于区分算法规模与单次采样成本。</summary>
        internal void RecordStageCount(string stage, long count)
        {
            lock (gate) stageCounts[stage] = count;
        }

        internal ChunkGenerationTiming(ChunkStreamingDiagnostics owner, ChunkGenerationRequest request)
        {
            this.owner = owner;
            this.request = request;
            pauseRevision = owner.PauseRevision;
            startedSuspended = owner.Suspended;
            owner.Count("generation.requested");
        }

        public void StartGeneration()
        {
            lock (gate) started = Stopwatch.GetTimestamp();
            owner.Count("generation.started");
        }

        public void FinishGeneration()
        {
            lock (gate)
            {
                finished = Stopwatch.GetTimestamp();
                if (!IsInterrupted())
                {
                    owner.Record("generation.queue_wait", ChunkStreamingDiagnostics.ElapsedMs(requested, started));
                    owner.Record("generation.work", ChunkStreamingDiagnostics.ElapsedMs(started, finished));
                }
                if (cancelled > 0 && !IsInterrupted())
                    owner.Record("generation.cancel_exit", ChunkStreamingDiagnostics.ElapsedMs(cancelled, finished));
            }
            owner.Count("generation.finished");
        }

        public void EnqueueCompletion() { lock (gate) enqueued = Stopwatch.GetTimestamp(); }
        public void Cancel() { lock (gate) { if (cancelled == 0) cancelled = Stopwatch.GetTimestamp(); } }

        public void StartCommit()
        {
            lock (gate)
            {
                commitStarted = Stopwatch.GetTimestamp();
                if (enqueued > 0 && !IsInterrupted())
                    owner.Record("commit.queue_wait", ChunkStreamingDiagnostics.ElapsedMs(enqueued, commitStarted));
            }
        }

        internal ChunkGenerationDiagnostic Finish(string result)
        {
            lock (gate)
            {
                handled = Stopwatch.GetTimestamp();
                outcome = result;
                if (IsInterrupted()) owner.Count("timing.pause_interrupted_requests");
                return Capture();
            }
        }

        private bool IsInterrupted() => startedSuspended || owner.Suspended || owner.PauseRevision != pauseRevision;

        public ChunkGenerationDiagnostic Capture()
        {
            lock (gate)
            {
                long now = handled > 0 ? handled : Stopwatch.GetTimestamp();
                return new ChunkGenerationDiagnostic
                {
                    Dimension = request.Address.DimensionId,
                    Origin = new[] { request.Address.ChunkOrigin.X, request.Address.ChunkOrigin.Y },
                    Epoch = request.WorldEpoch, Version = request.RequestVersion,
                    Stage = outcome ?? (commitStarted > 0 ? "committing" : enqueued > 0 ? "awaiting_commit" :
                        finished > 0 ? "completion_dispatch" : started > 0 ? "generating" : "queued"),
                    CancelRequested = cancelled > 0, PauseAffected = IsInterrupted(),
                    AgeMs = ChunkStreamingDiagnostics.ElapsedMs(requested, now),
                    GenerationQueueMs = ChunkStreamingDiagnostics.ElapsedMs(requested,
                        started > 0 ? started : enqueued > 0 ? enqueued : now),
                    GenerationWorkMs = started > 0 ? ChunkStreamingDiagnostics.ElapsedMs(started, finished > 0 ? finished : now) : 0d,
                    CommitQueueMs = enqueued > 0 ? ChunkStreamingDiagnostics.ElapsedMs(enqueued, commitStarted > 0 ? commitStarted : now) : 0d,
                    CommitWorkMs = commitStarted > 0 ? ChunkStreamingDiagnostics.ElapsedMs(commitStarted, now) : 0d,
                    GenerationStagesMs = new Dictionary<string, double>(stageMilliseconds),
                    GenerationCounts = new Dictionary<string, long>(stageCounts)
                };
            }
        }

        #endregion
    }

    /// <summary>脱离活跃对象的只读诊断副本；仅观察，不进入世界存档或网络快照。</summary>
    public sealed class ChunkGenerationDiagnostic
    {
        public string Dimension { get; internal set; }
        public int[] Origin { get; internal set; }
        public long Epoch { get; internal set; }
        public long Version { get; internal set; }
        public string Stage { get; internal set; }
        public bool CancelRequested { get; internal set; }
        public bool PauseAffected { get; internal set; }
        public double AgeMs { get; internal set; }
        public double GenerationQueueMs { get; internal set; }
        public double GenerationWorkMs { get; internal set; }
        public double CommitQueueMs { get; internal set; }
        public double CommitWorkMs { get; internal set; }
        public Dictionary<string, double> GenerationStagesMs { get; internal set; }
        public Dictionary<string, long> GenerationCounts { get; internal set; }
    }
}
