using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameplayMCP
{
    /// <summary>测试中心从现有清单加载复选框，勾选多个案例后只启动一轮真实游戏。</summary>
    public sealed class GameplayTestDashboardWindow : EditorWindow
    {
        #region 窗口状态
        private const string SuitePreference = "FlatWorld.GameplayTests.Dashboard.Suite";
        private const string SelectionPreferencePrefix = "FlatWorld.GameplayTests.Dashboard.Selection.";
        private readonly List<string> suitePaths = new();
        private readonly HashSet<string> selectedCaseIds = new(StringComparer.OrdinalIgnoreCase);
        private GameplayTestSuite suite;
        private int selectedSuiteIndex;
        private Vector2 casesScroll;
        private Vector2 resultsScroll;
        private JObject pipeline;
        private JObject gameplay;
        private string loadedGameplayPath;
        private string notice;
        private bool showFailureDetails = true;
        private double nextPoll;

        private bool IsRunning => pipeline?.Value<string>("status") == "running";
        private bool CanEdit => !IsRunning && !EditorApplication.isPlayingOrWillChangePlaymode;
        private string CurrentSuitePath => suitePaths.Count > 0 && selectedSuiteIndex < suitePaths.Count
            ? suitePaths[selectedSuiteIndex] : null;
        private int SelectedCount => selectedCaseIds.Count;
        #endregion

        #region 启动与刷新
        [MenuItem("FlatWorld/测试/测试中心（多选案例）", priority = 1)]
        private static void OpenWindow()
        {
            var window = GetWindow<GameplayTestDashboardWindow>("FlatWorld 测试中心");
            window.minSize = new Vector2(580f, 510f);
            window.Show();
        }

        private void OnEnable()
        {
            ReloadSuiteList();
            RefreshStatus();
            EditorApplication.update += UpdateStatus;
        }

        private void OnDisable() => EditorApplication.update -= UpdateStatus;

        private void UpdateStatus()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.8d;
            RefreshStatus();
            Repaint();
        }

        private void ReloadSuiteList()
        {
            string previous = CurrentSuitePath ?? EditorPrefs.GetString(
                SuitePreference, GameplayTestSelection.QuickSuitePath);
            suitePaths.Clear();
            string root = Path.Combine(Application.streamingAssetsPath, "GameplayTests");
            if (Directory.Exists(root))
            {
                foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    suitePaths.Add("GameplayTests/" + relative);
                }
                suitePaths.Sort(StringComparer.OrdinalIgnoreCase);
            }
            int index = suitePaths.FindIndex(path => string.Equals(path, previous, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                index = suitePaths.FindIndex(path => path == GameplayTestSelection.QuickSuitePath);
            selectedSuiteIndex = Mathf.Max(0, index);
            LoadSuite();
        }

        private void LoadSuite()
        {
            suite = null;
            selectedCaseIds.Clear();
            if (CurrentSuitePath == null)
            {
                notice = "没有找到 GameplayTests 目录下的 JSON 测试清单。";
                return;
            }
            try
            {
                string path = GameplayTestSelection.GetAbsoluteSuitePath(CurrentSuitePath);
                suite = GameplayTestSuite.Parse(File.ReadAllText(path));
                string key = SelectionPreferencePrefix + CurrentSuitePath;
                bool hasSavedSelection = EditorPrefs.HasKey(key);
                HashSet<string> saved = hasSavedSelection
                    ? new HashSet<string>(EditorPrefs.GetString(key, "").Split(
                        new[] { '|' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase)
                    : null;
                foreach (GameplayTestCase testCase in suite.Cases)
                    if (testCase.Enabled && (saved == null || saved.Contains(testCase.Id)))
                        selectedCaseIds.Add(testCase.Id);
                EditorPrefs.SetString(SuitePreference, CurrentSuitePath);
                notice = null;
            }
            catch (Exception exception)
            {
                notice = "无法读取测试清单：" + exception.Message;
            }
        }

        private void SaveSelection()
        {
            if (CurrentSuitePath == null || suite == null) return;
            var selected = new List<string>();
            foreach (GameplayTestCase testCase in suite.Cases)
                if (selectedCaseIds.Contains(testCase.Id)) selected.Add(testCase.Id);
            EditorPrefs.SetString(SelectionPreferencePrefix + CurrentSuitePath, string.Join("|", selected));
        }

        private void RefreshStatus()
        {
            try
            {
                JObject status = GameplayTestPipelineCoordinator.Status(false);
                pipeline = status.Value<bool?>("ok") == true ? status : null;
                string path = pipeline?.Value<string>("gameplayReportPath");
                if (!string.Equals(loadedGameplayPath, path, StringComparison.Ordinal))
                {
                    loadedGameplayPath = path;
                    gameplay = null;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        gameplay = JObject.Parse(File.ReadAllText(path));
                }
            }
            catch (Exception exception)
            {
                notice = "读取测试报告失败：" + exception.Message;
            }
        }
        #endregion

        #region 主界面
        private void OnGUI()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("FlatWorld · 自动化测试中心", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("EditMode 全部通过 → PlayMode 一次启动 → 顺序执行所有勾选的 Lua 案例",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(9f);
            DrawSuitePicker();
            DrawCasePicker();
            DrawRunButton();
            DrawReport();
        }

        private void DrawSuitePicker()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("测试清单", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (suitePaths.Count > 0)
            {
                string[] labels = suitePaths.Select(path =>
                    Path.GetFileName(path) + "   ·   " + path).ToArray();
                using (new EditorGUI.DisabledScope(!CanEdit))
                {
                    int next = EditorGUILayout.Popup(selectedSuiteIndex, labels);
                    if (next != selectedSuiteIndex)
                    {
                        selectedSuiteIndex = next;
                        LoadSuite();
                    }
                }
            }
            else
                EditorGUILayout.LabelField("没有找到清单", EditorStyles.miniLabel);
            if (GUILayout.Button("刷新", GUILayout.Width(70f)))
                ReloadSuiteList();
            EditorGUILayout.EndHorizontal();
            if (suite != null)
                EditorGUILayout.LabelField(
                    suite.BuildShowroom ? "模式：完整物品陈列厅（初始化较慢）" : "模式：快速测试世界（跳过全物品陈列）",
                    EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawCasePicker()
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"测试案例 · 已选择 {SelectedCount} 项", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!CanEdit || suite == null))
            {
                if (GUILayout.Button("全选", GUILayout.Width(55f)))
                {
                    selectedCaseIds.Clear();
                    foreach (GameplayTestCase testCase in suite.Cases)
                        if (testCase.Enabled) selectedCaseIds.Add(testCase.Id);
                    SaveSelection();
                }
                if (GUILayout.Button("反选", GUILayout.Width(55f)))
                {
                    foreach (GameplayTestCase testCase in suite.Cases)
                        if (testCase.Enabled)
                        {
                            if (!selectedCaseIds.Add(testCase.Id))
                                selectedCaseIds.Remove(testCase.Id);
                        }
                    SaveSelection();
                }
                if (GUILayout.Button("清空", GUILayout.Width(55f)))
                {
                    selectedCaseIds.Clear();
                    SaveSelection();
                }
            }
            EditorGUILayout.EndHorizontal();

            casesScroll = EditorGUILayout.BeginScrollView(casesScroll,
                GUILayout.MinHeight(120f), GUILayout.MaxHeight(Mathf.Max(170f, position.height * 0.35f)));
            if (suite != null)
            {
                foreach (GameplayTestCase testCase in suite.Cases)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    using (new EditorGUI.DisabledScope(!CanEdit || !testCase.Enabled))
                    {
                        bool wasChecked = selectedCaseIds.Contains(testCase.Id);
                        bool isChecked = EditorGUILayout.ToggleLeft(
                            testCase.Name + (testCase.Enabled ? "" : "（清单已禁用）"), wasChecked);
                        if (isChecked != wasChecked)
                        {
                            if (isChecked) selectedCaseIds.Add(testCase.Id);
                            else selectedCaseIds.Remove(testCase.Id);
                            SaveSelection();
                        }
                    }
                    EditorGUILayout.LabelField(testCase.Id + "    ·    " + testCase.LuaPath,
                        EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRunButton()
        {
            if (!string.IsNullOrEmpty(notice))
                EditorGUILayout.HelpBox(notice, MessageType.Warning);
            if (EditorApplication.isPlayingOrWillChangePlaymode && !IsRunning)
                EditorGUILayout.HelpBox("Unity 当前已进入 PlayMode；请退出你自己的游戏后再启动测试。", MessageType.Info);
            EditorGUILayout.Space(5f);
            using (new EditorGUI.DisabledScope(!CanEdit || suite == null || SelectedCount == 0))
            {
                if (GUILayout.Button(IsRunning ? "测试正在执行" : $"▶ 开始测试已勾选的 {SelectedCount} 个案例",
                        GUILayout.Height(38f)))
                {
                    var chosen = suite.Cases.Where(testCase => selectedCaseIds.Contains(testCase.Id))
                        .Select(testCase => testCase.Id).ToArray();
                    JObject response = GameplayTestPipelineCoordinator.Start(CurrentSuitePath, chosen);
                    if (response.Value<bool?>("ok") == true)
                    {
                        pipeline = response;
                        gameplay = null;
                        loadedGameplayPath = null;
                        notice = null;
                    }
                    else
                        notice = response.Value<string>("message") ?? "测试启动失败。";
                }
            }
            if (SelectedCount == 0)
                EditorGUILayout.HelpBox("请至少勾选一个测试案例；EditMode 前置检查会自动全部执行。",
                    MessageType.Info);
        }

        private void DrawReport()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("执行状态与结果", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (pipeline == null)
            {
                EditorGUILayout.LabelField("还没有运行报告。", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return;
            }
            string status = pipeline.Value<string>("status") ?? "unknown";
            string phase = pipeline.Value<string>("phase") ?? "queued";
            EditorGUILayout.LabelField($"状态：{TranslateStatus(status)}    当前阶段：{TranslatePhase(phase)}",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("本轮编号：" + pipeline.Value<string>("runId"), EditorStyles.miniLabel);
            JObject edit = pipeline["editMode"] as JObject;
            JObject play = pipeline["playMode"] as JObject;
            EditorGUILayout.LabelField("EditMode：" + PhaseSummary(edit));
            EditorGUILayout.LabelField("PlayMode：" + PhaseSummary(play));
            if (!string.IsNullOrEmpty(pipeline.Value<string>("reason")))
                EditorGUILayout.HelpBox(pipeline.Value<string>("reason"),
                    status == "passed" ? MessageType.Info : MessageType.Warning);
            if (!string.IsNullOrEmpty(pipeline.Value<string>("artifactError")))
                EditorGUILayout.HelpBox("测试产物未完整保存：" + pipeline.Value<string>("artifactError"),
                    MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            string reportPath = pipeline.Value<string>("reportPath");
            if (GUILayout.Button("定位流水线报告") && !string.IsNullOrEmpty(reportPath) && File.Exists(reportPath))
                EditorUtility.RevealInFinder(reportPath);
            string gamePath = pipeline.Value<string>("gameplayReportPath");
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath)))
                if (GUILayout.Button("定位玩法报告"))
                    EditorUtility.RevealInFinder(gamePath);
            if (GUILayout.Button("复制报告路径") && !string.IsNullOrEmpty(reportPath))
                EditorGUIUtility.systemCopyBuffer = reportPath;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            string runDirectory = pipeline.Value<string>("runDirectory");
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(runDirectory) || !Directory.Exists(runDirectory)))
                if (GUILayout.Button("定位文件夹测试包"))
                    EditorUtility.RevealInFinder(runDirectory);
            string summaryPath = pipeline.Value<string>("summaryPath");
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(summaryPath) || !File.Exists(summaryPath)))
                if (GUILayout.Button("打开阅读报告"))
                    Application.OpenURL(new Uri(summaryPath).AbsoluteUri);
            EditorGUILayout.EndHorizontal();
            DrawFailures(edit);
            DrawFailures(play);
            DrawGameplayCases();
            EditorGUILayout.EndVertical();
        }

        private void DrawFailures(JObject phase)
        {
            if (phase?["cases"] is not JArray cases) return;
            foreach (JToken caseResult in cases)
            {
                string result = caseResult.Value<string>("status") ?? "";
                if (result.StartsWith("Passed", StringComparison.OrdinalIgnoreCase)) continue;
                EditorGUILayout.HelpBox(
                    caseResult.Value<string>("name") + " · " + result + "\n" +
                    caseResult.Value<string>("message"), MessageType.Error);
            }
        }

        private void DrawGameplayCases()
        {
            if (gameplay?["cases"] is not JArray cases) return;
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Lua 用例执行结果", EditorStyles.boldLabel);
            resultsScroll = EditorGUILayout.BeginScrollView(resultsScroll, GUILayout.MaxHeight(160f));
            foreach (JToken item in cases)
            {
                if (item.Value<string>("status") == "disabled") continue;
                string status = item.Value<string>("status");
                EditorGUILayout.LabelField(
                    (status == "passed" ? "✓ " : "✕ ") + item.Value<string>("name") +
                    "  [" + item.Value<string>("id") + "]  " + TranslateStatus(status));
                string screenshotPath = item.Value<string>("screenshotPath");
                if (!string.IsNullOrEmpty(screenshotPath) && File.Exists(screenshotPath))
                {
                    if (GUILayout.Button("查看此用例结束截图", GUILayout.Width(155f)))
                        Application.OpenURL(new Uri(screenshotPath).AbsoluteUri);
                }
                else if (!string.IsNullOrEmpty(item.Value<string>("screenshotError")))
                    EditorGUILayout.LabelField("截图：" + item.Value<string>("screenshotError"),
                        EditorStyles.wordWrappedMiniLabel);
                if (status == "failed" && item["steps"] is JArray steps)
                    foreach (JToken step in steps)
                        if (step.Value<bool?>("success") == false)
                        {
                            EditorGUILayout.HelpBox(step.Value<string>("message"), MessageType.Error);
                            break;
                        }
            }
            EditorGUILayout.EndScrollView();
            if (gameplay.Value<int?>("errorCount") > 0)
            {
                showFailureDetails = EditorGUILayout.Foldout(showFailureDetails,
                    "Unity 错误日志（" + gameplay.Value<int>("errorCount") + "）");
                if (showFailureDetails && gameplay["logs"] is JArray logs)
                {
                    int displayed = 0;
                    foreach (JToken entry in logs)
                    {
                        string type = entry.Value<string>("type");
                        if (type != "Error" && type != "Exception" && type != "Assert") continue;
                        EditorGUILayout.HelpBox(
                            entry.Value<string>("message") + "\n" + entry.Value<string>("stackTrace"),
                            MessageType.Error);
                        if (++displayed >= 3) break;
                    }
                }
            }
        }

        private static string PhaseSummary(JObject phase) =>
            phase == null ? "未开始" : TranslateStatus(phase.Value<string>("status")) +
            $"    通过 {phase.Value<int?>("passed") ?? 0} / 失败 {phase.Value<int?>("failed") ?? 0}";

        private static string TranslateStatus(string status) => status switch
        {
            "running" => "运行中", "queued" => "排队中", "pending" => "待执行",
            "passed" => "通过", "failed" => "失败", "interrupted" => "中断",
            "skipped" => "跳过", "disabled" => "未启用", _ => status ?? "未知"
        };

        private static string TranslatePhase(string phase) => phase switch
        {
            "queued" => "准备", "editmode" => "EditMode", "playmode_queued" => "等待 PlayMode",
            "playmode" => "PlayMode", "finished" => "已结束", _ => phase ?? "未知"
        };
        #endregion
    }
}
