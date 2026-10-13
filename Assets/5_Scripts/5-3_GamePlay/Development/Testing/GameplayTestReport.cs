using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>测试报告只保存普通数据，避免序列化 Unity 对象时递归访问计算属性。</summary>
public sealed class GameplayTestReport
{
    #region 报告数据
    public int SchemaVersion = 4;
    public string RunId;
    public string Scene;
    public string TestSuitePath;
    public string SaveDirectory;
    public string ReportPath;
    public string ReportWriteError;
    public string RunDirectory;
    public string SummaryPath;
    public string PackagePath;
    public string ArtifactError;
    public string StartedUtc;
    public string FinishedUtc;
    public string Status = "running";
    public string CurrentStage;
    public double DurationSeconds;
    public int ErrorCount;
    public int WarningCount;
    public int OmittedLogCount;
    public int ExpectedItemCount;
    public int PlacedItemCount;
    public int PassedCaseCount;
    public int FailedCaseCount;
    public int SkippedCaseCount;
    public int InterruptedCaseCount;
    public GameplayTestPosition PlayerStart;
    public GameplayTestPosition PlayerEnd;
    public List<GameplayTestStepRecord> Steps = new();
    public List<GameplayTestLogRecord> Logs = new();
    public List<GameplayTestExhibit> Exhibits = new();
    public List<GameplayTestCaseReport> Cases = new();
    #endregion

    #region 文件输出
    public void Write() => WriteJson(ReportPath, this);

    internal static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string json = JsonConvert.SerializeObject(value, Formatting.Indented,
            new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        // 外部报告读取器短暂占用文件时重试，避免测试流程被 Windows 共享冲突误判失败。
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
                break;
            }
            catch (IOException) when (attempt < 9)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
    }
    #endregion
}

[Serializable]
public sealed class GameplayTestCaseReport
{
    #region 用例数据
    public string Id;
    public string Name;
    public string LuaPath;
    public string ReportPath;
    public string ReportWriteError;
    public string ScreenshotPath;
    public string ScreenshotError;
    public string Status = "pending";
    public string Reason;
    public string StartedUtc;
    public string FinishedUtc;
    public double StartedElapsedSeconds;
    public double DurationSeconds;
    public int ErrorCount;
    public int WarningCount;
    public int FailedStepCount;
    public int OmittedLogCount;
    public GameplayTestPosition PlayerStart;
    public GameplayTestPosition PlayerEnd;
    public List<GameplayTestStepRecord> Steps = new();
    public List<GameplayTestLogRecord> Logs = new();
    #endregion
}

[Serializable]
public sealed class GameplayTestStepRecord
{
    #region 步骤数据
    public int Index;
    public string Stage;
    public string CaseId;
    public string Command;
    public string Label;
    public bool Success;
    public string Message;
    public double ElapsedSeconds;
    public GameplayTestPosition PlayerPosition;
    #endregion
}

[Serializable]
public sealed class GameplayTestLogRecord
{
    #region 日志数据
    public string Utc;
    public double ElapsedSeconds;
    public string Stage;
    public string CaseId;
    public string Type;
    public string Message;
    public string StackTrace;
    #endregion
}

[Serializable]
public sealed class GameplayTestPosition
{
    #region 坐标快照
    public float X;
    public float Y;
    public float Z;

    public GameplayTestPosition(Vector3 position)
    {
        X = position.x;
        Y = position.y;
        Z = position.z;
    }
    #endregion
}
