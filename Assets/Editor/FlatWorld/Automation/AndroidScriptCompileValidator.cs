using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 仅验证 Android Player 脚本编译，不生成 APK/AAB。菜单入口先返回再排队执行底层编译，
    /// 避免外部自动化等待超时后重复发送命令；同一编辑器域内只允许一个任务排队或运行。
    /// </summary>
    public static class AndroidScriptCompileValidator
    {
        private const string OutputDirectory = "Temp/FlatWorldAndroidScriptCompile";
        private const string CompileStateSessionKey =
            "FlatWorld.Automation.AndroidScriptCompileValidator.QueuedOrRunning";
        private const string LastRequestUtcTicksSessionKey =
            "FlatWorld.Automation.AndroidScriptCompileValidator.LastRequestUtcTicks";
        private const double MinimumQueueDelaySeconds = 1d;
        private const double ProgressCleanupDelaySeconds = 3d;
        private const double DuplicateRequestGuardSeconds = 120d;

        private static bool compileQueuedOrRunning;
        private static double compileQueuedAt;
        private static bool progressCleanupQueued;
        private static double progressCleanupQueuedAt;

        #region 生命周期

        [InitializeOnLoadMethod]
        private static void RecoverAbandonedCompileState()
        {
            EditorApplication.update -= TryRunQueuedCompile;
            EditorApplication.update -= TryCleanupStaleCompileProgress;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            EditorApplication.quitting -= HandleEditorQuitting;
            EditorApplication.quitting += HandleEditorQuitting;

            compileQueuedOrRunning = false;
            SessionState.EraseBool(CompileStateSessionKey);
            QueueCompileProgressCleanup();
        }

        /// <summary>切换运行模式时取消尚未执行的校验，避免与程序集重载交错。</summary>
        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                CancelQueuedCompile("正在进入 Play Mode");
        }

        /// <summary>编辑器退出时解除回调并清理传统进度窗。</summary>
        private static void HandleEditorQuitting()
        {
            CancelQueuedCompile(null);
            EditorApplication.update -= TryCleanupStaleCompileProgress;
            EditorUtility.ClearProgressBar();
        }

        #endregion

        #region 菜单入口

        [MenuItem("FlatWorld/Validation/Compile Android Player Scripts")]
        public static void CompileAndroidPlayerScripts()
        {
            if (compileQueuedOrRunning ||
                SessionState.GetBool(CompileStateSessionKey, false) ||
                WasRequestedRecently())
            {
                Debug.LogWarning("[Android Script Compile] 已有近期任务排队、运行或完成，本次重复请求已忽略。");
                return;
            }

            compileQueuedOrRunning = true;
            SessionState.SetBool(CompileStateSessionKey, true);
            SessionState.SetString(LastRequestUtcTicksSessionKey, DateTime.UtcNow.Ticks.ToString());
            compileQueuedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= TryRunQueuedCompile;
            EditorApplication.update += TryRunQueuedCompile;
            Debug.Log("[Android Script Compile] 已加入队列，菜单调用已立即返回。");
        }

        #endregion

        #region 编译执行

        private static void TryRunQueuedCompile()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CancelQueuedCompile("编辑器已进入或正在进入 Play Mode");
                return;
            }

            // 至少让出一秒，确保 MCP 等外部调用方先收到菜单成功响应，不会按超时机制重发命令。
            if (EditorApplication.timeSinceStartup - compileQueuedAt < MinimumQueueDelaySeconds ||
                EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
                return;

            EditorApplication.update -= TryRunQueuedCompile;

            try
            {
                CompileAndroidPlayerScriptsNow();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Android Script Compile] 失败：{exception.Message}");
                Debug.LogException(exception);
            }
            finally
            {
                compileQueuedOrRunning = false;
                SessionState.EraseBool(CompileStateSessionKey);
                QueueCompileProgressCleanup();
            }
        }

        /// <summary>取消仍在排队的编译校验。</summary>
        private static void CancelQueuedCompile(string reason)
        {
            bool hadQueuedCompile = compileQueuedOrRunning ||
                                    SessionState.GetBool(CompileStateSessionKey, false);
            EditorApplication.update -= TryRunQueuedCompile;
            compileQueuedOrRunning = false;
            SessionState.EraseBool(CompileStateSessionKey);

            if (hadQueuedCompile && !string.IsNullOrEmpty(reason))
                Debug.Log($"[Android Script Compile] 已取消：{reason}。");
        }

        /// <summary>跨域重载保留短期请求记录，拦截外部连接恢复后重放的菜单命令。</summary>
        private static bool WasRequestedRecently()
        {
            string ticksText = SessionState.GetString(LastRequestUtcTicksSessionKey, string.Empty);
            if (!long.TryParse(ticksText, out long requestedAtTicks))
                return false;

            long elapsedTicks = DateTime.UtcNow.Ticks - requestedAtTicks;
            return elapsedTicks >= 0 &&
                   TimeSpan.FromTicks(elapsedTicks).TotalSeconds < DuplicateRequestGuardSeconds;
        }

        private static void CompileAndroidPlayerScriptsNow()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Android Build Support 未安装，无法执行目标脚本编译。");

            string absoluteOutput = Path.GetFullPath(OutputDirectory);
            Directory.CreateDirectory(absoluteOutput);
            ScriptCompilationSettings settings = new ScriptCompilationSettings
            {
                target = BuildTarget.Android,
                group = BuildTargetGroup.Android,
                options = ScriptCompilationOptions.None
            };

            ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(settings, absoluteOutput);
            int reportedAssemblyCount = result.assemblies?.Count ?? 0;
            int outputAssemblyCount = Directory.GetFiles(
                absoluteOutput,
                "*.dll",
                SearchOption.TopDirectoryOnly).Length;
            int assemblyCount = Math.Max(reportedAssemblyCount, outputAssemblyCount);
            if (assemblyCount == 0)
                throw new InvalidOperationException("Android Player 脚本编译没有产出任何程序集。");

            Debug.Log(
                $"[Android Script Compile] 通过：assemblies={assemblyCount}，" +
                $"reported={reportedAssemblyCount}。未生成或交付 APK/AAB。");
        }

        /// <summary>延迟清理 Unity 在目标平台脚本编译后偶发遗留的忙碌进度项。</summary>
        private static void QueueCompileProgressCleanup()
        {
            progressCleanupQueued = true;
            progressCleanupQueuedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= TryCleanupStaleCompileProgress;
            EditorApplication.update += TryCleanupStaleCompileProgress;
        }

        /// <summary>仅在编译已结束时移除脚本编译进度，不干扰其他资源任务。</summary>
        private static void TryCleanupStaleCompileProgress()
        {
            if (!progressCleanupQueued ||
                EditorApplication.timeSinceStartup - progressCleanupQueuedAt < ProgressCleanupDelaySeconds ||
                EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
                return;

            EditorApplication.update -= TryCleanupStaleCompileProgress;
            progressCleanupQueued = false;

            int removedCount = 0;
            foreach (Progress.Item item in Progress.EnumerateItems())
            {
                if (!item.running || !IsScriptCompileProgress(item.name, item.description))
                    continue;

                Progress.Remove(item.id, true);
                removedCount++;
            }

            EditorUtility.ClearProgressBar();
            if (removedCount > 0)
                Debug.Log($"[Android Script Compile] 已清理 {removedCount} 个残留的脚本编译进度项。");
        }

        /// <summary>识别 Unity 目标平台脚本编译产生的进度项。</summary>
        private static bool IsScriptCompileProgress(string name, string description)
        {
            return ContainsIgnoreCase(name, "Compiling scripts") ||
                   ContainsIgnoreCase(description, "Assembly Definition Files scripts");
        }

        /// <summary>执行不区分大小写的进度文本匹配。</summary>
        private static bool ContainsIgnoreCase(string value, string marker)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion
    }
}
