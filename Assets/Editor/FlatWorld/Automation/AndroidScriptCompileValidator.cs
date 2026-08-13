using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 仅验证 Android Player 脚本编译，不生成 APK/AAB。直接调用 Unity 的底层 Player 脚本编译接口，
    /// 让 Android 条件编译和 Player 程序集在交付前经过目标平台编译，同时避免 BuildScriptsOnly 隐式依赖历史 APK。
    /// </summary>
    public static class AndroidScriptCompileValidator
    {
        private const string OutputDirectory = "Temp/FlatWorldAndroidScriptCompile";

        [MenuItem("FlatWorld/Validation/Compile Android Player Scripts")]
        public static void CompileAndroidPlayerScripts()
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
            int assemblyCount = result.assemblies?.Count ?? 0;
            if (assemblyCount == 0)
                throw new InvalidOperationException("Android Player 脚本编译没有产出任何程序集。");

            Debug.Log(
                $"[Android Script Compile] 通过：assemblies={assemblyCount}。未生成或交付 APK/AAB。");
        }
    }
}
