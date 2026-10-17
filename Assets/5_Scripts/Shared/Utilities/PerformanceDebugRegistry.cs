using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;

namespace FlatWorld.Diagnostics
{
    /// <summary>诊断只观察业务；后台线程仅写有界数字缓冲，由主线程按需输出文字。</summary>
    public static class PerformanceDebugRegistry
    {
        #region 会话与有界缓冲

        public const int MaximumPoints = 256;
        public const int MaximumSlowEvents = 128;
        public const double DefaultSlowThresholdMilliseconds = 16.67d;
        private const int StatisticsFlag = 1, SlowFlag = 2;
        private static readonly object gate = new();
        private static readonly List<PerformanceDebugPoint> points = new(MaximumPoints);
        private static readonly SlowEvent[] slowEvents = new SlowEvent[MaximumSlowEvents];
        private static int flags, generation = 1, slowCount, rejectedPoints;
        private static long droppedSlowEvents;
        private static double slowThreshold = DefaultSlowThresholdMilliseconds;

        internal static int ActiveFlags => Volatile.Read(ref flags);
        internal static int Generation => Volatile.Read(ref generation);
        public static bool StatisticsEnabled => (ActiveFlags & StatisticsFlag) != 0;
        public static bool SlowSummaryEnabled => (ActiveFlags & SlowFlag) != 0;
        public static bool HasPendingSlowEvents => Volatile.Read(ref slowCount) > 0;
        public static double SlowThresholdMilliseconds
        {
            get { lock (gate) return slowThreshold; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                lock (gate) slowThreshold = Math.Max(.1d, Math.Min(10000d, value));
            }
        }

        private struct SlowEvent
        {
            internal PerformanceDebugPoint Point;
            internal long Ticks, Work;
            internal string Context;
        }

        internal static bool Register(PerformanceDebugPoint point)
        {
            lock (gate)
            {
                if (points.Count >= MaximumPoints) { rejectedPoints++; return false; }
                points.Add(point);
                return true;
            }
        }

        public static void SetStatisticsEnabled(bool enabled) => SetFlag(StatisticsFlag, enabled);
        public static void SetSlowSummaryEnabled(bool enabled) => SetFlag(SlowFlag, enabled);

        private static void SetFlag(int flag, bool enabled)
        {
            lock (gate)
            {
                int next = enabled ? flags | flag : flags & ~flag;
                if (next == flags) return;
                generation++;
                Volatile.Write(ref flags, next);
                if (!enabled && flag == SlowFlag) ClearSlowEvents();
            }
        }

        /// <summary>清空统计保留注册和开关，已开始的旧作用域不能写回新一轮。</summary>
        public static void Clear()
        {
            lock (gate)
            {
                generation++;
                foreach (PerformanceDebugPoint point in points)
                {
                    point.Calls = point.Work = point.Gauge = point.PeakGauge = point.TotalTicks = point.MaxTicks = 0;
                    point.HasGauge = false;
                }
                ClearSlowEvents();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState() => ResetSession();

        /// <summary>关闭域重载时也重置会话，但静态只读点位仍保留原注册。</summary>
        public static void ResetSession()
        {
            lock (gate)
            {
                Volatile.Write(ref flags, 0);
                slowThreshold = DefaultSlowThresholdMilliseconds;
                Clear();
            }
        }

        #endregion

        #region 纯数字收集

        internal static void Record(PerformanceDebugPoint point, int session, int capturedFlags,
            long ticks, long work, string context)
        {
            lock (gate)
            {
                if (session != generation || flags == 0) return;
                ticks = Math.Max(0, ticks);
                if ((capturedFlags & flags & StatisticsFlag) != 0)
                {
                    point.Calls++;
                    point.Work += Math.Max(0, work);
                    point.TotalTicks += ticks;
                    point.MaxTicks = Math.Max(point.MaxTicks, ticks);
                }
                if ((capturedFlags & flags & SlowFlag) == 0 ||
                    ticks * 1000d / Stopwatch.Frequency < slowThreshold) return;
                if (slowCount == slowEvents.Length) { droppedSlowEvents++; return; }
                slowEvents[slowCount++] = new SlowEvent { Point = point, Ticks = ticks, Work = work, Context = context };
            }
        }

        internal static void AddWork(PerformanceDebugPoint point, long count, int session)
        {
            lock (gate) if (session == generation && (flags & StatisticsFlag) != 0) point.Work += count;
        }

        internal static void SetGauge(PerformanceDebugPoint point, long value, int session)
        {
            lock (gate)
            {
                if (session != generation || (flags & StatisticsFlag) == 0) return;
                point.Gauge = value;
                point.PeakGauge = point.HasGauge ? Math.Max(point.PeakGauge, value) : value;
                point.HasGauge = true;
            }
        }

        private static void ClearSlowEvents()
        {
            Array.Clear(slowEvents, 0, slowCount);
            slowCount = 0;
            droppedSlowEvents = 0;
        }

        public static void DiscardPendingSlowEvents() { lock (gate) ClearSlowEvents(); }

        #endregion

        #region 按需快照与主线程摘要

        public readonly struct Snapshot
        {
            public readonly string Name;
            public readonly long Calls, Work, Gauge, PeakGauge;
            public readonly bool HasGauge;
            public readonly double TotalMilliseconds, MaximumMilliseconds;
            internal Snapshot(PerformanceDebugPoint point)
            {
                Name = point.Name; Calls = point.Calls; Work = point.Work;
                Gauge = point.Gauge; PeakGauge = point.PeakGauge; HasGauge = point.HasGauge;
                TotalMilliseconds = point.TotalTicks * 1000d / Stopwatch.Frequency;
                MaximumMilliseconds = point.MaxTicks * 1000d / Stopwatch.Frequency;
            }
        }

        public static Snapshot[] CaptureSnapshot()
        {
            lock (gate)
            {
                var result = new Snapshot[points.Count];
                for (int i = 0; i < points.Count; i++) result[i] = new Snapshot(points[i]);
                return result;
            }
        }

        public static string BuildSummary()
        {
            Snapshot[] snapshot = CaptureSnapshot();
            Array.Sort(snapshot, (a, b) => b.TotalMilliseconds.CompareTo(a.TotalMilliseconds));
            var text = new StringBuilder("[性能 DEBUG] 区段为包含子调用的 Total，不可相加当作整帧。\n");
            text.Append("统计=").Append(StatisticsEnabled).Append("，慢摘要=").Append(SlowSummaryEnabled)
                .Append("，阈值=").Append(SlowThresholdMilliseconds.ToString("F2", CultureInfo.InvariantCulture))
                .Append("ms，注册=").Append(snapshot.Length).Append("，超额点位=").Append(rejectedPoints).AppendLine();
            foreach (Snapshot point in snapshot)
            {
                if (point.Calls == 0 && point.Work == 0 && !point.HasGauge) continue;
                text.Append(point.Name).Append(" | calls=").Append(point.Calls).Append(" work=").Append(point.Work)
                    .Append(" total=").Append(point.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture))
                    .Append("ms max=").Append(point.MaximumMilliseconds.ToString("F3", CultureInfo.InvariantCulture)).Append("ms");
                if (point.HasGauge) text.Append(" gauge=").Append(point.Gauge).Append(" peak=").Append(point.PeakGauge);
                text.AppendLine();
            }
            return text.ToString();
        }

        /// <summary>由主线程日志泵每次合并一批；热路径不会格式化或调用 Unity 日志。</summary>
        public static string DrainSlowSummary()
        {
            SlowEvent[] pending;
            long dropped;
            lock (gate)
            {
                if (slowCount == 0) return null;
                pending = new SlowEvent[slowCount];
                Array.Copy(slowEvents, pending, slowCount);
                dropped = droppedSlowEvents;
                ClearSlowEvents();
            }
            // 快照复制后离锁分组和排版，慢日志不能挡住后台写盘的数字收集。
            var groups = new Dictionary<PerformanceDebugPoint, SlowAggregate>(pending.Length);
            foreach (SlowEvent item in pending)
            {
                groups.TryGetValue(item.Point, out SlowAggregate group);
                group.Count++; group.Total += item.Ticks; group.Work += Math.Max(0, item.Work);
                if (item.Ticks >= group.Peak) { group.Peak = item.Ticks; group.Context = item.Context; }
                groups[item.Point] = group;
            }
            var text = new StringBuilder("[性能 DEBUG 慢操作] ");
            text.Append(pending.Length).Append(" 条合并，缓冲超额丢弃=").Append(dropped).AppendLine();
            foreach (var pair in groups)
            {
                SlowAggregate group = pair.Value;
                text.Append(pair.Key.Name).Append(" | slow=").Append(group.Count);
                if (group.Work > 0) text.Append(" inputWork=").Append(group.Work);
                text.Append(" total=").Append((group.Total * 1000d / Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture))
                    .Append("ms max=").Append((group.Peak * 1000d / Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture)).Append("ms");
                if (group.Context != null) text.Append(" context=").Append(group.Context);
                text.AppendLine();
            }
            return text.ToString();
        }

        private struct SlowAggregate
        {
            internal int Count;
            internal long Total, Peak, Work;
            internal string Context;
        }

        #endregion
    }
}
