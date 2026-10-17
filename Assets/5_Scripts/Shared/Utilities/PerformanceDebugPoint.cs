using System;
using System.Diagnostics;
using Unity.Profiling;

namespace FlatWorld.Diagnostics
{
    /// <summary>固定性能点同时提供 Profiler 区段与按需启用的纯数字统计。</summary>
    public sealed class PerformanceDebugPoint
    {
        #region 点位与无分配作用域

        private readonly ProfilerMarker marker;
        internal readonly bool Registered;
        internal long Calls, Work, Gauge, PeakGauge, TotalTicks, MaxTicks;
        internal bool HasGauge;
        public string Name { get; }

        public PerformanceDebugPoint(string markerName)
        {
            if (string.IsNullOrWhiteSpace(markerName)) throw new ArgumentException("性能点名称不能为空。", nameof(markerName));
            Name = markerName;
            marker = new ProfilerMarker(markerName);
            Registered = PerformanceDebugRegistry.Register(this);
        }

        public Scope Measure(long workUnits = 0, string context = null)
        {
            ProfilerMarker.AutoScope profiler = marker.Auto();
            int flags = Registered ? PerformanceDebugRegistry.ActiveFlags : 0;
            return flags == 0 ? new Scope(profiler) :
                new Scope(profiler, this, PerformanceDebugRegistry.Generation, flags,
                    Stopwatch.GetTimestamp(), workUnits, context);
        }

        /// <summary>实际处理量按批追加，不增加调用次数；关闭时只检查开关。</summary>
        public void AddWork(long count)
        {
            if (Registered && PerformanceDebugRegistry.StatisticsEnabled && count > 0)
                PerformanceDebugRegistry.AddWork(this, count, PerformanceDebugRegistry.Generation);
        }

        /// <summary>记录当前和峰值规模，不把重复观察当作新增工作。</summary>
        public void SetGauge(long value)
        {
            if (Registered && PerformanceDebugRegistry.StatisticsEnabled)
                PerformanceDebugRegistry.SetGauge(this, value, PerformanceDebugRegistry.Generation);
        }

        public struct Scope : IDisposable
        {
            private ProfilerMarker.AutoScope profiler;
            private PerformanceDebugPoint point;
            private readonly int generation, flags;
            private readonly long started, work;
            private readonly string context;
            private bool disposed;

            internal Scope(ProfilerMarker.AutoScope profiler)
            {
                this.profiler = profiler;
                point = null;
                generation = flags = 0;
                started = work = 0;
                context = null;
                disposed = false;
            }

            internal Scope(ProfilerMarker.AutoScope profiler, PerformanceDebugPoint point,
                int generation, int flags, long started, long work, string context)
            {
                this.profiler = profiler;
                this.point = point;
                this.generation = generation;
                this.flags = flags;
                this.started = started;
                this.work = work;
                this.context = context;
                disposed = false;
            }

            /// <summary>后台批次的计数跟随开始时的代际，清空后不会写入下一轮。</summary>
            public readonly void AddWork(long count)
            {
                if (!disposed && point != null && PerformanceDebugRegistry.StatisticsEnabled && count > 0)
                    PerformanceDebugRegistry.AddWork(point, count, generation);
            }

            public readonly void SetGauge(long value)
            {
                if (!disposed && point != null && PerformanceDebugRegistry.StatisticsEnabled)
                    PerformanceDebugRegistry.SetGauge(point, value, generation);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                long elapsed = point == null ? 0 : Stopwatch.GetTimestamp() - started;
                profiler.Dispose();
                if (point != null)
                {
                    PerformanceDebugRegistry.Record(point, generation, flags, elapsed, work, context);
                    point = null;
                }
            }
        }

        #endregion
    }
}
