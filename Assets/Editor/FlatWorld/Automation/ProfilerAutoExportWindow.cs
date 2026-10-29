using UnityEditor;
using UnityEngine;

namespace FlatWorld.Automation
{
    internal sealed class ProfilerAutoExportWindow : EditorWindow
    {
        #region 自动导出设置窗口

        private Vector2 scrollPosition;

        [MenuItem("FlatWorld/性能分析/自动导出设置")]
        internal static void Open()
        {
            var window = GetWindow<ProfilerAutoExportWindow>("性能 JSON 导出设置");
            window.minSize = new Vector2(460f, 560f);
            window.Show();
        }

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("性能导出采样（手动 / 自动共用）", EditorStyles.boldLabel);
            ProfilerSnapshotToolbar.SummaryNeighborFrameCount = EditorGUILayout.DelayedIntField(
                new GUIContent("摘要邻帧总数", "不含目标帧自身，默认 60：前后各 30 帧。设为 0 关闭邻帧摘要。"),
                ProfilerSnapshotToolbar.SummaryNeighborFrameCount);
            ProfilerSnapshotToolbar.DetailSelectionNeighborFrameCount = EditorGUILayout.DelayedIntField(
                new GUIContent("慢帧筛选邻帧总数", "不含目标帧自身，默认 300：前后各 150 帧。只用来挑选最慢帧，不导出整个范围的摘要或详情。"),
                ProfilerSnapshotToolbar.DetailSelectionNeighborFrameCount);
            ProfilerSnapshotToolbar.MaximumAdditionalDetailedFrames = EditorGUILayout.DelayedIntField(
                new GUIContent("额外详细帧数", "按耗时从高到低挑选，不限制 50ms；不重复目标帧。默认 3，连同目标帧最多 4 帧详情；设为 0 只导出目标帧详情。"),
                ProfilerSnapshotToolbar.MaximumAdditionalDetailedFrames);
            EditorGUILayout.HelpBox("数量均不含目标帧，前后平分，奇数时前面多取一帧；一侧不足不向另一侧补齐。" +
                "摘要和筛选范围独立，只有选中的最慢帧才额外导出完整调用树。", MessageType.Info);
            if (ProfilerSnapshotToolbar.SummaryNeighborFrameCount > 300 ||
                ProfilerSnapshotToolbar.DetailSelectionNeighborFrameCount > 1000 ||
                ProfilerSnapshotToolbar.MaximumAdditionalDetailedFrames > 10)
                EditorGUILayout.HelpBox("采样量较大：摘要超过 300 帧、筛选范围超过 1000 帧或额外详情超过 10 帧时，" +
                    "导出可能变慢，报告和内存占用也可能增加。当前设置仍会生效，实际只读取 Profiler 已保留的帧。", MessageType.Warning);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("低帧率自动取证", EditorStyles.boldLabel);
            ProfilerAutoExporter.Enabled = EditorGUILayout.Toggle("启用自动模式", ProfilerAutoExporter.Enabled);
            ProfilerAutoExporter.ThresholdFps = EditorGUILayout.DelayedFloatField(
                new GUIContent("触发阈值（FPS）", "严格低于该值的帧，其耗时计入低帧率累计时间，默认 30 FPS。"), ProfilerAutoExporter.ThresholdFps);
            ProfilerAutoExporter.DurationSeconds = EditorGUILayout.DelayedFloatField(
                new GUIContent("持续时间（秒）", "滚动统计最近这段时间，累计至少 80% 的时间低于目标 FPS 才触发；默认 5 秒，即累计至少 4 秒。"),
                ProfilerAutoExporter.DurationSeconds);
            ProfilerAutoExporter.WarmupSeconds = EditorGUILayout.DelayedFloatField(
                new GUIContent("启动/继续缓冲（秒）", "进入 PlayMode、启用或继续游戏后跳过的时间，避免加载或暂停造成误触发；设为 0 可立即监测。"),
                ProfilerAutoExporter.WarmupSeconds);
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox($"最近 {ProfilerAutoExporter.DurationSeconds:0.##} 秒内，低帧率累计至少 " +
                $"{ProfilerAutoExporter.DurationSeconds * ProfilerAutoExporter.RequiredLowFpsRatio:0.##} 秒（80%）才触发，无需连续。\n" +
                "完整观察窗口达标 → 暂停编辑器 → 导出窗口内最慢帧和暂停现场 → 自动继续游戏。\n" +
                "FPS 按 Profiler 的 CPU 帧耗时换算，不受 timeScale 影响。仅监测本机 Editor 的 PlayMode，" +
                "自动开启所需录制并恢复原开关；不会改变目标、深度分析或游戏时间缩放。", MessageType.Info);
            EditorGUILayout.LabelField("最近采样", ProfilerAutoExporter.CurrentFps > 0d
                ? $"{ProfilerAutoExporter.CurrentFps:0.##} FPS" : "暂无新帧");
            EditorGUILayout.LabelField("观察进度", $"{ProfilerAutoExporter.ObservedSeconds:0.##} / {ProfilerAutoExporter.DurationSeconds:0.##} 秒");
            EditorGUILayout.LabelField("低帧率累计", $"{ProfilerAutoExporter.LowFpsSeconds:0.##} / " +
                $"{ProfilerAutoExporter.DurationSeconds * ProfilerAutoExporter.RequiredLowFpsRatio:0.##} 秒（80%）");
            EditorGUILayout.HelpBox(ProfilerAutoExporter.Status, MessageType.None);
            if (!string.IsNullOrEmpty(ProfilerAutoExporter.LastReportPath))
            {
                EditorGUILayout.LabelField("最近自动导出");
                EditorGUILayout.SelectableLabel(ProfilerAutoExporter.LastReportPath, EditorStyles.wordWrappedLabel,
                    GUILayout.Height(40));
            }
            if (GUILayout.Button("打开性能报告目录")) ProfilerSnapshotToolbar.OpenReportsFolder();
            EditorGUILayout.EndScrollView();
        }

        #endregion
    }
}
