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
    public int SchemaVersion = 1;
    public string RunId;
    public string Scene;
    public string LuaPath;
    public string SaveDirectory;
    public string ReportPath;
    public string ReportWriteError;
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
    public GameplayTestPosition PlayerStart;
    public GameplayTestPosition PlayerEnd;
    public List<GameplayTestStepRecord> Steps = new();
    public List<GameplayTestLogRecord> Logs = new();
    public List<GameplayTestExhibit> Exhibits = new();
    #endregion

    #region 文件输出
    public void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        string json = JsonConvert.SerializeObject(this, Formatting.Indented,
            new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
        string temporaryPath = ReportPath + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        if (File.Exists(ReportPath)) File.Replace(temporaryPath, ReportPath, null);
        else File.Move(temporaryPath, ReportPath);
    }
    #endregion
}

[Serializable]
public sealed class GameplayTestStepRecord
{
    #region 步骤数据
    public int Index;
    public string Stage;
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
