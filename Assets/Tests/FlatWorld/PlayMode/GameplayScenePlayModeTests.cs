using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace FlatWorld.Tests.PlayMode
{
    /// <summary>一轮 PlayMode 只创建一次正式世界，在其中执行整份 Lua 清单。</summary>
    public sealed class GameplayScenePlayModeTests
    {
        #region 真实场景集成测试
        [Serializable]
        private sealed class TestReport
        {
            public string status;
            public int errorCount;
            public int failedCaseCount;
            public List<TestStep> steps;
            public List<TestLog> logs;
        }

        [Serializable]
        private sealed class TestStep
        {
            public bool success;
            public string label;
            public string message;
        }

        [Serializable]
        private sealed class TestLog
        {
            public string type;
            public string message;
            public string stackTrace;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator ExecuteEntireGameplaySuiteInOneWorld()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(
                "Assets/3_Scenes/Development/GameplayTestScene.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Fail("GameplayTestScene 仅在 Unity Editor 内执行，不属于正式 Build。");
#endif
            GameplayTestScene runner = null;
            float deadline = Time.realtimeSinceStartup + 720f;
            while (runner == null && Time.realtimeSinceStartup < deadline)
            {
                runner = UnityEngine.Object.FindObjectOfType<GameplayTestScene>();
                yield return null;
            }
            Assert.That(runner, Is.Not.Null, "没有找到 GameplayTestRunner，场景未正确加载。");
            while (runner.Status == "running" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(runner.Status, Is.Not.EqualTo("running"), "玩法测试场景超时，未写入最终报告。");
            Assert.That(File.Exists(runner.ReportPath), Is.True,
                "玩法测试场景没有生成 JSON 报告：" + runner.ReportPath);
            TestReport report = JsonUtility.FromJson<TestReport>(File.ReadAllText(runner.ReportPath));
            Assert.That(report, Is.Not.Null, "测试报告 JSON 解析失败。");
            if (report.status != "passed")
            {
                string detail = string.Empty;
                if (report.steps != null)
                    foreach (TestStep step in report.steps)
                        if (!step.success) { detail = step.label + ": " + step.message; break; }
                if (string.IsNullOrEmpty(detail) && report.logs?.Count > 0)
                    detail = report.logs[0].type + ": " + report.logs[0].message +
                             "\n" + report.logs[0].stackTrace;
                Assert.Fail($"GameplayTestScene 返回 {report.status}，报告 {runner.ReportPath}\n{detail}");
            }
            Assert.That(report.errorCount, Is.Zero, "Unity 运行时存在 Error/Exception/Assert。");
            Assert.That(report.failedCaseCount, Is.Zero, "玩法用例执行失败。");
        }
        #endregion
    }
}
