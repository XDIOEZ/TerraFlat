using System;
using System.IO;
using System.Reflection;
using PuertsUnityMcp.Editor;
using UnityEditor;
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
        private const string ExportMenu = "FlatWorld/性能分析/导出选中帧与控制台 JSON";
        private static readonly Type ToolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
        private static bool exporting;

        private static string ReportsRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "67_ProfileData"));

        static ProfilerSnapshotToolbar()
        {
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
                        tooltip = "导出 Profiler 选中帧、附近帧耗时、当前 Console 日志、已加载世界物品现场 JSON 和整个桌面截图到 67_ProfileData；右键打开目录。"
                    };
                    button.style.minWidth = 88;
                    button.style.flexShrink = 0;
                    button.AddManipulator(new ContextualMenuManipulator(menu =>
                        menu.menu.AppendAction("打开性能报告目录", _ => OpenReportsFolder())));
                    zone.Insert(0, button);
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

        #endregion

        #region 快照导出与目录入口

        [MenuItem(ExportMenu)]
        public static void ExportSelectedFrame()
        {
            if (exporting) return;
            var profiler = EditorWindow.focusedWindow as ProfilerWindow;
            if (profiler == null)
            {
                var windows = Resources.FindObjectsOfTypeAll<ProfilerWindow>();
                if (windows.Length > 0) profiler = windows[0];
            }
            if (profiler == null)
            {
                EditorUtility.DisplayDialog("性能 JSON", "请先打开 Profiler，录制并选中要分析的帧。", "知道了");
                return;
            }
            exporting = true;
            try
            {
                long selected = profiler.selectedFrameIndex;
                if (selected < 0 || selected > int.MaxValue)
                    throw new InvalidOperationException("请先在 Profiler 曲线上点选要分析的帧。");
                string directory = UnityMcpProfilerSnapshot.ExportSelectedFrame((int)selected, ReportsRoot,
                    exportAdditionalFiles: WorldSceneSnapshotExporter.Export);
                EditorGUIUtility.systemCopyBuffer = directory;
                profiler.ShowNotification(new GUIContent("性能、日志、世界现场与整屏截图已导出，文件夹路径已复制"), 5d);
                Debug.Log("[性能 JSON] 本次最新报告：" + directory + "\n包含性能、日志、world-scene.json 与 screen.png；可以继续游戏，让 AI 先读 67_ProfileData 根目录的 00-READ-ME-FIRST.md，再分析此报告。");
            }
            catch (Exception exception)
            {
                var cause = exception.GetBaseException();
                EditorUtility.DisplayDialog("性能 JSON 导出失败", cause.Message, "知道了");
            }
            finally
            {
                exporting = false;
            }
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
