using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PuertsUnityMcp.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlatWorld.Automation
{
    [InitializeOnLoad]
    public static class ProfilerSnapshotToolbar
    {
        #region 编辑器顶栏按钮

        private const string ButtonName = "FlatWorldProfilerSnapshot";
        private const string ExportMenu = "FlatWorld/性能分析/导出选中或最新帧与控制台 JSON";
        private static readonly Type ToolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
        private static bool exporting;
        private static ToolbarToggle autoToggle;
        private static ToolbarButton autoSettings;

        public static string ReportsRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "ProfileData"));
        internal static bool IsExporting => exporting;

        static ProfilerSnapshotToolbar()
        {
            ProfilerAutoExporter.SettingsChanged += RefreshAutoControls;
            if (!Application.isBatchMode)
                EditorApplication.update += TryInstallButton;
        }

        // 顶栏重建时重新挂接，正常运行期间不保留逐帧扫描。
        private static void TryInstallButton()
        {
            if (ToolbarType == null)
            {
                EditorApplication.update -= TryInstallButton;
                return;
            }
            try
            {
                var toolbars = Resources.FindObjectsOfTypeAll(ToolbarType);
                if (toolbars.Length == 0) return;
                var rootField = ToolbarType.GetField("m_Root", BindingFlags.Instance | BindingFlags.NonPublic);
                if (rootField == null) throw new MissingFieldException("UnityEditor.Toolbar.m_Root");
                var root = rootField.GetValue(toolbars[0]) as VisualElement;
                var zone = root?.Q("ToolbarZoneRightAlign");
                if (zone == null) return;
                if (zone.Q(ButtonName) == null)
                {
                    var button = new ToolbarButton(ExportSelectedFrame)
                    {
                        name = ButtonName,
                        text = "性能 JSON",
                        tooltip = "Profiler 有选中帧时导出该帧及附近帧，否则导出最近帧及附近帧；同时保存 Console、世界现场和 Unity 主窗口截图到 Logs/ProfileData；成功后自动复制报告文件夹完整路径，可直接粘贴给 Agent；右键打开目录。"
                    };
                    button.style.minWidth = 88;
                    button.style.flexShrink = 0;
                    button.AddManipulator(new ContextualMenuManipulator(menu =>
                    {
                        menu.menu.AppendAction("自动导出设置", _ => ProfilerAutoExportWindow.Open());
                        menu.menu.AppendAction("低帧率自动导出", _ => ProfilerAutoExporter.Enabled = !ProfilerAutoExporter.Enabled,
                            _ => ProfilerAutoExporter.Enabled ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                        menu.menu.AppendAction("打开性能报告目录", _ => OpenReportsFolder());
                    }));
                    zone.Insert(0, button);
                    autoToggle = new ToolbarToggle { text = "自动", tooltip = "设定时间内，低于目标 FPS 的累计时间达到 80% 时，暂停并导出窗口内最慢帧的性能 JSON，完成后自动继续游戏。" };
                    autoToggle.RegisterValueChangedCallback(evt => ProfilerAutoExporter.Enabled = evt.newValue);
                    autoSettings = new ToolbarButton(ProfilerAutoExportWindow.Open) { tooltip = "设置自动导出的 FPS 阈值、持续时间和启动缓冲时间。" };
                    autoToggle.style.flexShrink = autoSettings.style.flexShrink = 0;
                    zone.Insert(1, autoToggle);
                    zone.Insert(2, autoSettings);
                    RefreshAutoControls();
                    root.RegisterCallback<DetachFromPanelEvent>(OnToolbarDetached);
                }
                EditorApplication.update -= TryInstallButton;
            }
            catch (Exception exception)
            {
                EditorApplication.update -= TryInstallButton;
                Debug.LogWarning("[性能 JSON] 顶栏按钮挂接失败，可使用菜单「" + ExportMenu + "」。" + exception.Message);
            }
        }

        private static void OnToolbarDetached(DetachFromPanelEvent evt)
        {
            if (evt.target != evt.currentTarget) return;
            EditorApplication.update -= TryInstallButton;
            EditorApplication.update += TryInstallButton;
        }

        private static void RefreshAutoControls()
        {
            autoToggle?.SetValueWithoutNotify(ProfilerAutoExporter.Enabled);
            if (autoSettings != null) autoSettings.text = $"<{ProfilerAutoExporter.ThresholdFps:0.##} FPS";
        }

        #endregion

        #region 快照导出与目录入口

        [MenuItem(ExportMenu)]
        public static void ExportSelectedFrame()
        {
            if (exporting || ProfilerAutoExporter.IsExportPending) return;
            try
            {
                // Profiler 未打开或没有有效选中帧时，直接使用缓冲区最新一帧。
                int first = ProfilerDriver.firstFrameIndex;
                int last = ProfilerDriver.lastFrameIndex;
                if (first < 0 || last < first)
                    throw new InvalidOperationException("Profiler 暂无可导出的帧，请先录制一些帧。");
                var notificationWindow = EditorWindow.focusedWindow;
                var profiler = notificationWindow as ProfilerWindow;
                if (profiler == null)
                {
                    var windows = Resources.FindObjectsOfTypeAll<ProfilerWindow>();
                    if (windows.Length > 0) profiler = windows[0];
                }
                long selected = profiler != null ? profiler.selectedFrameIndex : -1;
                int frameIndex = selected >= first && selected <= last ? (int)selected : last;
                string directory = ExportFrame(frameIndex);
                // 导出成功后复制报告目录，并在当前窗口提示，方便直接粘贴给 Agent。
                EditorGUIUtility.systemCopyBuffer = directory;
                (notificationWindow ?? profiler)?.ShowNotification(new GUIContent("性能报告已导出，文件夹完整路径已复制到剪贴板"), 5d);
                Debug.Log("[性能 JSON] 本次最新报告：" + directory + "\n文件夹完整路径已复制到剪贴板，可直接粘贴给 Agent。包含性能、日志和 world-scene.json；主窗口截图状态见 profiler-analysis.json。可以继续游戏，让 AI 先读 Logs/ProfileData 根目录的 00-READ-ME-FIRST.md，再分析此报告。");
            }
            catch (Exception exception)
            {
                var cause = exception.GetBaseException();
                EditorUtility.DisplayDialog("性能 JSON 导出失败", cause.Message, "知道了");
            }
        }

        // 菜单与 MCP 共用无弹窗导出入口，现场和采集说明一起发布到正式目录。
        public static string ExportFrame(int frameIndex, int contextFrameRadius = 30,
            Func<string, string, int, string[]> exportAdditionalFiles = null)
        {
            if (exporting) throw new InvalidOperationException("profiler_export_busy: 正在导出性能报告。");
            exporting = true;
            try
            {
                return UnityMcpProfilerSnapshot.ExportSelectedFrame(frameIndex, ReportsRoot, contextFrameRadius,
                    (directory, snapshotId, displayFrame) =>
                    {
                        var files = new List<string>(WorldSceneSnapshotExporter.Export(directory, snapshotId, displayFrame));
                        if (exportAdditionalFiles != null)
                            files.AddRange(exportAdditionalFiles(directory, snapshotId, displayFrame));
                        return files.ToArray();
                    });
            }
            finally { exporting = false; }
        }

        [MenuItem("FlatWorld/性能分析/打开性能报告目录")]
        public static void OpenReportsFolder()
        {
            Directory.CreateDirectory(ReportsRoot);
            EditorUtility.RevealInFinder(ReportsRoot);
        }

        #endregion
    }
}
