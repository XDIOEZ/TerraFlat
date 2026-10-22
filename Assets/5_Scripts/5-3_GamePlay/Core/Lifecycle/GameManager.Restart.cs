using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

public partial class GameManager
{
    #region 保存后重启进程

    private bool restartInProgress;

    public static bool SupportsProcessRestart
    {
        get
        {
#if !UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX)
            return true;
#else
            return false;
#endif
        }
    }

    public static bool TryRestartApplication(out string error)
    {
        if (!SupportsProcessRestart)
        {
            error = "当前平台请手动关闭并重新启动游戏。";
            return false;
        }
        try
        {
            StartRestartWaiter();
            Application.Quit();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = "重启失败：" + exception.Message;
            return false;
        }
    }

    public void SaveAndRestart(Item playerItem)
    {
        if (restartInProgress) return;
        StartCoroutine(SaveAndRestartCoroutine(playerItem));
    }

    private IEnumerator SaveAndRestartCoroutine(Item playerItem)
    {
        restartInProgress = true;
        while (manualSaveInProgress) yield return null;
        if (!SupportsProcessRestart)
        {
            UnityEngine.Debug.LogWarning("[Restart] 当前平台请手动保存并重新启动游戏。");
            restartInProgress = false;
            yield break;
        }
        yield return BackToHelloScene_Coroutine(playerItem, onComplete: () =>
        {
            if (!TryRestartApplication(out string error)) UnityEngine.Debug.LogError(error);
        }, requireSuccessfulSave: true);
        restartInProgress = false;
    }

    private static void StartRestartWaiter()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        using Process current = Process.GetCurrentProcess();
        string executable = current.MainModule.FileName;
        string path = executable.Replace("'", "''");
        string directory = Path.GetDirectoryName(executable).Replace("'", "''");
        string script = $"Wait-Process -Id {current.Id} -ErrorAction SilentlyContinue; Start-Process -FilePath '{path}' -WorkingDirectory '{directory}'";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using Process helper = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded)
        { UseShellExecute = false, CreateNoWindow = true });
        if (helper == null) throw new InvalidOperationException("无法启动重启助手，已保留当前游戏。");
#elif !UNITY_EDITOR && UNITY_STANDALONE_LINUX
        using Process current = Process.GetCurrentProcess();
        string executable = current.MainModule.FileName;
        string quote = "'" + executable.Replace("'", "'\\''") + "'";
        string script = $"while kill -0 {current.Id} 2>/dev/null; do sleep 1; done; exec {quote}";
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(script);
        using Process helper = Process.Start(info);
        if (helper == null) throw new InvalidOperationException("无法启动重启助手，已保留当前游戏。");
#endif
    }

    #endregion
}
