using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
public static class AutoCompileWatcher
{
    // 开关状态的 EditorPrefs 键
    private const string PrefKeyEnabled = "AutoCompileWatcher.Enabled";
    // 防抖延迟，避免频繁触发编译
    private const double MinDelaySeconds = 0.6d;

    // 最近一次资源变更时间戳
    private static double _lastChangeTime;
    // 是否已有编译请求在等待
    private static bool _pending;

    static AutoCompileWatcher()
    {
        // 轮询 Editor 更新，用于触发防抖后的编译请求
        EditorApplication.update += OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
        if (!IsEnabled())
        {
            return;
        }

        if (!_pending)
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (EditorApplication.timeSinceStartup - _lastChangeTime < MinDelaySeconds)
        {
            return;
        }

        _pending = false;
        CompilationPipeline.RequestScriptCompilation();
    }

    // 标记变更并开始防抖计时
    private static void MarkPending()
    {
        _pending = true;
        _lastChangeTime = EditorApplication.timeSinceStartup;
    }

    // 读取开关状态
    private static bool IsEnabled()
    {
        return EditorPrefs.GetBool(PrefKeyEnabled, true);
    }

    [MenuItem("Tools/自动编译监听/启用", true)]
    private static bool ValidateEnable()
    {
        return !IsEnabled();
    }

    [MenuItem("Tools/自动编译监听/启用")]
    private static void Enable()
    {
        EditorPrefs.SetBool(PrefKeyEnabled, true);
    }

    [MenuItem("Tools/自动编译监听/禁用", true)]
    private static bool ValidateDisable()
    {
        return IsEnabled();
    }

    [MenuItem("Tools/自动编译监听/禁用")]
    private static void Disable()
    {
        EditorPrefs.SetBool(PrefKeyEnabled, false);
    }

    private class AutoCompilePostprocessor : AssetPostprocessor
    {
        // 监听与脚本编译相关的资源变更
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!IsEnabled())
            {
                return;
            }

            if (HasCompileRelevantChange(importedAssets)
                || HasCompileRelevantChange(deletedAssets)
                || HasCompileRelevantChange(movedAssets)
                || HasCompileRelevantChange(movedFromAssetPaths))
            {
                MarkPending();
            }
        }

        // 仅响应脚本/asmdef/asmref 的变更
        private static bool HasCompileRelevantChange(string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                string extension = Path.GetExtension(paths[i]);
                if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".asmref", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
