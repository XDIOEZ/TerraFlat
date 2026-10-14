using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>快速规则用例在编辑态执行，不初始化游戏资源、不创建世界或改动正式存档。</summary>
public static class GameplayTestRulesSuiteRunner
{
    #region 隔离规则清单
    private const int MaximumCapturedLogs = 2000;

    public static IEnumerator Run(GameplayTestSuite suite, string suitePath, string runId, Action<GameplayTestReport> completed)
    {
        if (suite == null || suite.ExecutionMode != "rules") throw new ArgumentException("必须使用 rules 清单。");
        var timer = Stopwatch.StartNew();
        var report = new GameplayTestReport
        {
            RunId = runId, Scene = "EditModeRules", TestSuitePath = suitePath,
            StartedUtc = DateTime.UtcNow.ToString("O"), CurrentStage = "rules",
            RunDirectory = GameplayTestArtifacts.GetRunDirectory(runId)
        };
        report.ReportPath = Path.Combine(report.RunDirectory, "report.json");
        report.SummaryPath = Path.Combine(report.RunDirectory, "report.md");
        report.PackagePath = report.RunDirectory;
        Directory.CreateDirectory(report.RunDirectory);
        foreach (GameplayTestCase definition in suite.Cases)
            report.Cases.Add(new GameplayTestCaseReport
            {
                Id = definition.Id, Name = definition.Name, LuaPath = definition.LuaPath,
                Status = definition.Enabled ? "pending" : "disabled",
                ReportPath = GameplayTestArtifacts.CaseReportPath(report, definition.Id)
            });
        report.Write();
        bool stop = false;
        int caseIndex = 0;
        foreach (GameplayTestCase definition in suite.Cases)
        {
            GameplayTestCaseReport testCase = report.Cases[caseIndex++];
            if (!definition.Enabled)
            {
                GameplayTestReport.WriteJson(testCase.ReportPath, testCase);
                continue;
            }
            if (stop)
            {
                testCase.Status = "skipped"; testCase.Reason = "前一规则用例失败且清单禁止继续。";
                report.SkippedCaseCount++;
                GameplayTestReport.WriteJson(testCase.ReportPath, testCase);
                continue;
            }
            testCase.Status = "running";
            testCase.StartedUtc = DateTime.UtcNow.ToString("O");
            testCase.StartedElapsedSeconds = timer.Elapsed.TotalSeconds;
            report.CurrentStage = "rules." + definition.Id;
            report.Write();
            void Record(string command, string label, bool success, string message)
            {
                var step = new GameplayTestStepRecord
                {
                    Index = report.Steps.Count + 1, Stage = report.CurrentStage,
                    CaseId = definition.Id, Command = command, Label = label, Success = success,
                    Message = message, ElapsedSeconds = timer.Elapsed.TotalSeconds
                };
                report.Steps.Add(step); testCase.Steps.Add(step);
                if (!success) testCase.FailedStepCount++;
            }
            void CaptureLog(string message, string stack, LogType type)
            {
                if (type is not (LogType.Error or LogType.Exception or LogType.Assert or LogType.Warning)) return;
                bool error = type != LogType.Warning;
                if (error) { report.ErrorCount++; testCase.ErrorCount++; }
                else { report.WarningCount++; testCase.WarningCount++; }
                if (report.Logs.Count >= MaximumCapturedLogs)
                {
                    report.OmittedLogCount++; testCase.OmittedLogCount++;
                    return;
                }
                var entry = new GameplayTestLogRecord { Utc = DateTime.UtcNow.ToString("O"),
                    ElapsedSeconds = timer.Elapsed.TotalSeconds, Stage = report.CurrentStage, CaseId = definition.Id,
                    Type = type.ToString(), Message = message, StackTrace = stack };
                report.Logs.Add(entry); testCase.Logs.Add(entry);
            }
            using var runner = new GameplayTestLuaRunner(null, _ => false, Record);
            Application.logMessageReceived += CaptureLog;
            Exception failure = null;
            try
            {
                string source = null;
                try
                {
                    string path = Path.Combine(Application.streamingAssetsPath, definition.LuaPath.Replace('/', Path.DirectorySeparatorChar));
                    source = File.ReadAllText(path, Encoding.UTF8);
                    // RunRules 在任何生产调用前完成同一套预检，不重复创建和强制回收 Lua VM。
                }
                catch (Exception exception) { failure = exception; }
                if (failure == null)
                    yield return GameplayTestLuaRunner.Advance(runner.RunRules(source, "@" + definition.LuaPath), exception => failure = exception);
            }
            finally
            {
                Application.logMessageReceived -= CaptureLog;
                // 中断时也落盘当前真实采样，后续案例保留 pending 供统一收尾标记。
                report.DurationSeconds = timer.Elapsed.TotalSeconds;
                report.Write();
            }
            if (failure != null) Record("rules", "规则用例异常", false, failure.ToString());
            testCase.Status = failure == null && runner.Succeeded && testCase.FailedStepCount == 0 && testCase.ErrorCount == 0 ? "passed" : "failed";
            testCase.Reason = testCase.Status == "passed" ? null : failure?.Message ??
                testCase.Steps.Find(step => !step.Success)?.Message ?? "规则用例存在 Unity 错误。";
            testCase.FinishedUtc = DateTime.UtcNow.ToString("O");
            testCase.DurationSeconds = timer.Elapsed.TotalSeconds - testCase.StartedElapsedSeconds;
            if (testCase.Status == "passed") report.PassedCaseCount++;
            else { report.FailedCaseCount++; stop = !suite.ContinueAfterFailure; }
            GameplayTestReport.WriteJson(testCase.ReportPath, testCase);
            report.DurationSeconds = timer.Elapsed.TotalSeconds;
            report.Write();
            yield return null;
        }
        report.Status = report.FailedCaseCount == 0 && report.SkippedCaseCount == 0 && report.PassedCaseCount > 0 ? "passed" : "failed";
        report.CurrentStage = "finished";
        report.FinishedUtc = DateTime.UtcNow.ToString("O");
        report.DurationSeconds = timer.Elapsed.TotalSeconds;
        report.Write();
        GameplayTestArtifacts.WriteSummary(report);
        completed(report);
    }
    #endregion
}
