using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace FlatWorld.Automation
{
    [InitializeOnLoad]
    internal static class ProfilerAutoExporter
    {
        #region 自动模式设置与生命周期

        internal const string ToggleMenu = "FlatWorld/性能分析/低帧率自动导出";
        internal const double RequiredLowFpsRatio = 0.8d;
        private static readonly string PreferenceKey = "FlatWorld.ProfilerAuto." + Application.dataPath + ".";
        private static readonly Func<bool> IsEditorConnection = BindEditorConnectionCheck();
        private static bool enabled;
        private static float thresholdFps, warmupSeconds, durationSeconds;
        private static bool monitoring, recordingBefore, expectedRecording;
        private static int lastObservedFrame = -1, targetId;
        private static double readyAt;
        private static Trigger pending;

        internal static event Action SettingsChanged;
        internal static string Status { get; private set; } = "自动模式已关闭";
        internal static double CurrentFps { get; private set; }
        internal static double ObservedSeconds { get; private set; }
        internal static double LowFpsSeconds { get; private set; }
        internal static string LastReportPath { get; private set; }
        internal static bool IsExportPending => pending != null;

        internal static bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value) return;
                StopMonitoring();
                enabled = value;
                EditorPrefs.SetBool(PreferenceKey + "Enabled", value);
                EditorApplication.update -= Update;
                if (value && !Application.isBatchMode) EditorApplication.update += Update;
                Status = value ? "等待运行中的游戏" : "自动模式已关闭";
                SettingsChanged?.Invoke();
            }
        }

        internal static float ThresholdFps
        {
            get => thresholdFps;
            set => SetSetting(ref thresholdFps, value, 30f, 1f, 1000f, "ThresholdFps");
        }

        internal static float WarmupSeconds
        {
            get => warmupSeconds;
            set => SetSetting(ref warmupSeconds, value, 5f, 0f, 120f, "WarmupSeconds");
        }

        internal static float DurationSeconds
        {
            get => durationSeconds;
            set => SetSetting(ref durationSeconds, value, 5f, 0.01f, 120f, "DurationSeconds");
        }

        static ProfilerAutoExporter()
        {
            thresholdFps = Sanitize(EditorPrefs.GetFloat(PreferenceKey + "ThresholdFps", 30f), 30f, 1f, 1000f);
            warmupSeconds = Sanitize(EditorPrefs.GetFloat(PreferenceKey + "WarmupSeconds", 5f), 5f, 0f, 120f);
            durationSeconds = Sanitize(EditorPrefs.GetFloat(PreferenceKey + "DurationSeconds", 5f), 5f, 0.01f, 120f);
            LastReportPath = SessionState.GetString(PreferenceKey + "LastReport", "");
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.pauseStateChanged += OnPauseChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            Enabled = EditorPrefs.GetBool(PreferenceKey + "Enabled", false);
        }

        private static float Sanitize(float value, float fallback, float minimum, float maximum)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);

        // Unity 2022 将该接口设为 internal，缓存委托避免逐帧反射与字符串分配。
        private static Func<bool> BindEditorConnectionCheck()
        {
            var method = typeof(ProfilerDriver).GetMethod("IsConnectionEditor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return method != null ? (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method) : () => false;
        }

        private static void SetSetting(ref float field, float value, float fallback, float minimum, float maximum, string name)
        {
            value = Sanitize(value, fallback, minimum, maximum);
            if (field == value) return;
            field = value;
            EditorPrefs.SetFloat(PreferenceKey + name, value);
            StopMonitoring();
            SettingsChanged?.Invoke();
        }

        [MenuItem(ToggleMenu)]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem(ToggleMenu, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(ToggleMenu, Enabled);
            return !Application.isBatchMode;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state) => StopMonitoring();

        private static void OnPauseChanged(PauseState state)
        {
            // 自己触发的暂停保留待导出帧；手动暂停或继续时清空旧帧游标。
            if (state == PauseState.Paused && pending != null) return;
            StopMonitoring();
        }

        private static void Shutdown()
        {
            EditorApplication.update -= Update;
            StopMonitoring();
        }

        private static void StopMonitoring()
        {
            EditorApplication.delayCall -= ExportPending;
            pending = null;
            if (monitoring && ProfilerDriver.enabled == expectedRecording)
                ProfilerDriver.enabled = recordingBefore;
            monitoring = false;
            lastObservedFrame = -1;
            CurrentFps = 0d;
            ResetWindow();
        }

        #endregion

        #region 滚动窗口内低帧率累计时间

        private static readonly Queue<SlowFrameSpan> SlowFrames = new Queue<SlowFrameSpan>();
        private static double windowStartedAtMs = double.NaN, previousFrameStartMs, previousFrameEndMs, slowFrameMilliseconds;

        private static void ResetWindow()
        {
            SlowFrames.Clear();
            windowStartedAtMs = double.NaN;
            previousFrameStartMs = previousFrameEndMs = slowFrameMilliseconds = 0d;
            ObservedSeconds = LowFpsSeconds = 0d;
        }

        private static bool ObserveFrame(double startMilliseconds, double milliseconds)
        {
            double endMilliseconds = startMilliseconds + milliseconds;
            if (double.IsNaN(startMilliseconds) || double.IsInfinity(startMilliseconds) ||
                double.IsNaN(endMilliseconds) || double.IsInfinity(endMilliseconds) || milliseconds <= 0d)
            {
                ResetWindow();
                return false;
            }
            if (!double.IsNaN(windowStartedAtMs) &&
                (startMilliseconds < previousFrameStartMs || endMilliseconds < previousFrameEndMs))
                ResetWindow();
            if (double.IsNaN(windowStartedAtMs))
            {
                windowStartedAtMs = startMilliseconds;
                previousFrameEndMs = startMilliseconds;
            }

            // 只累计真实 CPU 帧区间的重叠部分，窗口外、空档和重叠帧耗时都不会重复计入。
            double spanStart = Math.Max(startMilliseconds, previousFrameEndMs);
            previousFrameStartMs = startMilliseconds;
            previousFrameEndMs = endMilliseconds;
            if (CurrentFps < thresholdFps && endMilliseconds > spanStart)
            {
                SlowFrames.Enqueue(new SlowFrameSpan { startMs = spanStart, endMs = endMilliseconds });
                slowFrameMilliseconds += endMilliseconds - spanStart;
            }
            double windowMilliseconds = durationSeconds * 1000d;
            double cutoff = endMilliseconds - windowMilliseconds;
            while (SlowFrames.Count > 0 && SlowFrames.Peek().endMs <= cutoff)
            {
                SlowFrameSpan expired = SlowFrames.Dequeue();
                slowFrameMilliseconds -= expired.endMs - expired.startMs;
            }
            double clippedMilliseconds = SlowFrames.Count > 0 ? Math.Max(0d, cutoff - SlowFrames.Peek().startMs) : 0d;
            LowFpsSeconds = Math.Max(0d, slowFrameMilliseconds - clippedMilliseconds) / 1000d;
            ObservedSeconds = Math.Min(windowMilliseconds, endMilliseconds - windowStartedAtMs) / 1000d;
            return ObservedSeconds >= durationSeconds &&
                LowFpsSeconds + 0.000000001d >= durationSeconds * RequiredLowFpsRatio;
        }

        private struct SlowFrameSpan
        {
            internal double startMs, endMs;
        }

        #endregion

        #region 新帧检测与先暂停后导出

        private static bool EditorBusy => EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying;

        private static void Update()
        {
            if (!enabled || pending != null) return;
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorBusy)
                {
                    StopMonitoring();
                    Status = EditorApplication.isPaused ? "编辑器已暂停；继续后重新开始监测" : "等待运行中的游戏";
                    return;
                }
                if (!IsEditorConnection() || ProfilerDriver.profileEditor)
                {
                    StopMonitoring();
                    Status = "请将 Profiler 目标设为本机 Editor 的 PlayMode（关闭 Profile Editor）";
                    return;
                }
                if (ProfilerSnapshotToolbar.IsExporting) return;
                if (monitoring && targetId != ProfilerDriver.connectedProfiler) StopMonitoring();
                if (!monitoring)
                {
                    recordingBefore = ProfilerDriver.enabled;
                    expectedRecording = true;
                    monitoring = true;
                    targetId = ProfilerDriver.connectedProfiler;
                    ProfilerDriver.enabled = true;
                    lastObservedFrame = ProfilerDriver.lastFrameIndex;
                    readyAt = EditorApplication.timeSinceStartup + warmupSeconds;
                    Status = "启动/继续缓冲中，暂不触发";
                    return;
                }
                if (!ProfilerDriver.enabled)
                {
                    Enabled = false;
                    Status = "Profiler 录制被手动关闭，自动模式已停止";
                    return;
                }

                int first = ProfilerDriver.firstFrameIndex;
                int last = ProfilerDriver.lastFrameIndex;
                if (last < lastObservedFrame)
                {
                    StopMonitoring();
                    Status = "Profiler 缓冲已重置，重新等待新帧";
                    return;
                }
                if (EditorApplication.timeSinceStartup < readyAt)
                {
                    lastObservedFrame = last;
                    return;
                }
                Status = "监测中：完整观察窗口内，低帧率累计达到 80% 才暂停并导出";
                if (first < 0 || last < first) return;
                if (lastObservedFrame >= 0 && first > lastObservedFrame + 1) ResetWindow();

                // 只读取尚未检查的帧耗时，不展开采样树，也不漏掉两次 Editor 更新之间的慢帧。
                for (int frame = Math.Max(first, lastObservedFrame + 1); frame <= last; frame++)
                {
                    double milliseconds, startMilliseconds;
                    using (var raw = ProfilerDriver.GetRawFrameDataView(frame, 0))
                    {
                        if (!raw.valid)
                        {
                            if (frame == last) break;
                            lastObservedFrame = frame;
                            ResetWindow();
                            continue;
                        }
                        milliseconds = raw.frameTimeMs;
                        startMilliseconds = raw.frameStartTimeMs;
                    }
                    lastObservedFrame = frame;
                    if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds <= 0d)
                    {
                        ResetWindow();
                        continue;
                    }
                    CurrentFps = 1000d / milliseconds;
                    if (!ObserveFrame(startMilliseconds, milliseconds)) continue;
                    pending = new Trigger
                    {
                        triggeredAtUtc = DateTime.UtcNow.ToString("o"),
                        selectedDisplayFrame = frame + 1,
                        thresholdFps = thresholdFps,
                        measuredFps = CurrentFps,
                        frameTimeMs = milliseconds,
                        warmupSeconds = warmupSeconds,
                        durationSeconds = durationSeconds,
                        observedSeconds = ObservedSeconds,
                        lowFpsSeconds = LowFpsSeconds,
                        lowFpsRatio = LowFpsSeconds / durationSeconds,
                        requiredLowFpsRatio = RequiredLowFpsRatio,
                        windowEndProfilerTimeMs = startMilliseconds + milliseconds,
                        windowStartProfilerTimeMs = startMilliseconds + milliseconds - durationSeconds * 1000d,
                        targetId = targetId
                    };
                    EditorApplication.isPaused = true;
                    expectedRecording = false;
                    ProfilerDriver.enabled = false;
                    Status = "已请求暂停，等待确认后导出触发帧";
                    EditorApplication.delayCall += ExportPending;
                    return;
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void ExportPending()
        {
            Trigger trigger = pending;
            if (trigger == null) return;
            try
            {
                // 必须在下一次编辑器回调确认暂停，不能把导出本身的卡顿误认为触发帧。
                if (!EditorApplication.isPlaying || !EditorApplication.isPaused || EditorBusy)
                    throw new InvalidOperationException("编辑器未保持 PlayMode 暂停状态，已取消自动导出。");
                if (ProfilerDriver.connectedProfiler != trigger.targetId || !IsEditorConnection() || ProfilerDriver.profileEditor)
                    throw new InvalidOperationException("Profiler 目标已改变，已取消自动导出。");
                int frame = trigger.selectedDisplayFrame - 1;
                if (frame < ProfilerDriver.firstFrameIndex || frame > ProfilerDriver.lastFrameIndex)
                    throw new InvalidOperationException("触发帧已不在 Profiler 缓冲中，未使用其他帧代替。");
                if (ProfilerDriver.enabled)
                    throw new InvalidOperationException("等待导出时录制被重新开启，已取消本次取证。");
                using (var raw = ProfilerDriver.GetRawFrameDataView(frame, 0))
                {
                    if (!raw.valid || raw.frameTimeMs != trigger.frameTimeMs ||
                        raw.frameStartTimeMs + raw.frameTimeMs != trigger.windowEndProfilerTimeMs)
                        throw new InvalidOperationException("触发帧已失效或被替换，未导出其他帧。");
                }
                trigger.editorPausedBeforeExport = true;
                string directory = ProfilerSnapshotToolbar.ExportFrame(frame, 30,
                    (pendingDirectory, snapshotId, displayFrame) =>
                    {
                        trigger.snapshotId = snapshotId;
                        trigger.selectedDisplayFrame = displayFrame;
                        File.WriteAllText(Path.Combine(pendingDirectory, "auto-trigger.json"),
                            JsonUtility.ToJson(trigger, true), new UTF8Encoding(false));
                        return new[] { "auto-trigger.json" };
                    });
                LastReportPath = directory;
                SessionState.SetString(PreferenceKey + "LastReport", directory);
                EditorGUIUtility.systemCopyBuffer = directory;
                Status = $"已导出：最近 {trigger.durationSeconds:0.##} 秒内低帧率累计 {trigger.lowFpsSeconds:0.##} 秒；编辑器保持暂停";
                Debug.Log($"[性能 JSON 自动] 最近 {trigger.durationSeconds:0.##} 秒内，低于 {trigger.thresholdFps:0.##} FPS " +
                    $"累计 {trigger.lowFpsSeconds:0.##} 秒（{trigger.lowFpsRatio:P0}），" +
                    $"已先暂停编辑器并导出第 {trigger.selectedDisplayFrame} 帧：\n{directory}\n路径已复制；检查完成后手动继续游戏。");
            }
            catch (Exception exception) { Fail(exception); }
            finally { StopMonitoring(); }
        }

        private static void Fail(Exception exception)
        {
            Enabled = false;
            Status = "自动导出已停止：" + exception.GetBaseException().Message;
            Debug.LogError("[性能 JSON 自动] " + Status + "；不会自动恢复游戏。");
        }

        [Serializable]
        private sealed class Trigger
        {
            public int schemaVersion = 2;
            public string reason = "low-fps-time-ratio";
            public string snapshotId, triggeredAtUtc;
            public int selectedDisplayFrame, targetId;
            public double thresholdFps, measuredFps, frameTimeMs, warmupSeconds;
            public double durationSeconds, observedSeconds, lowFpsSeconds, lowFpsRatio, requiredLowFpsRatio;
            public double windowStartProfilerTimeMs, windowEndProfilerTimeMs;
            public bool editorPausedBeforeExport;
            public string measurement = "FPS = 1000 / Profiler main-thread CPU frameTimeMs; low-FPS time is the CPU frame interval overlap with the trailing durationSeconds window, not a frame count ratio. Not GPU/presentation FPS.";
            public string sceneScope = "World/Console/screenshot describe the paused export-time scene, not a reconstruction of the triggering historical frame.";
        }

        #endregion
    }
}
