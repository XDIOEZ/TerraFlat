/*
* Tencent is pleased to support the open source community by making Puerts available.
* Copyright (C) 2020 Tencent.  All rights reserved.
* Puerts is licensed under the BSD 3-Clause License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
* This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
*/

#if UNITY_2020_1_OR_NEWER && !PUERTS_GENERAL
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Puerts.Editor.Generator
{
    /// <summary>
    /// Build preprocessor that checks if required IL2CPP files are generated before building
    /// </summary>
    public class GenerateChecker : IPreprocessBuildWithReport
    {
        #region IL2CPP 构建前生成检查

        public int callbackOrder => -100; // Run early in the build process

        public void OnPreprocessBuild(BuildReport report)
        {
            // 仅在目标使用 IL2CPP 优化时要求生成桥接文件。
            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(report.summary.platform);
            if (targetGroup == BuildTargetGroup.Unknown ||
                PlayerSettings.GetScriptingBackend(targetGroup) != ScriptingImplementation.IL2CPP)
            {
                return;
            }

            foreach (string define in PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup).Split(';'))
            {
                if (define.Trim() == "PUERTS_DISABLE_IL2CPP_OPTIMIZATION")
                {
                    return;
                }
            }

            string saveTo = PathHelper.GetIl2cppPluginPath();
            string requiredFile = Path.Combine(saveTo, "Puerts_il2cpp.cpp");

            if (!File.Exists(requiredFile))
            {
                string dialogMessage = "Required file 'Puerts_il2cpp.cpp' not found!\n\n" +
                    $"Expected location:\n{requiredFile}\n\n" +
                    "Please generate IL2CPP files first by selecting one of the following menu options:\n\n" +
                    "• Tools -> PuerTS -> Generate il2cpp -> Reflection Mode\n" +
                    "• Tools -> PuerTS -> Generate il2cpp -> Static Wrapper Mode\n" +
                    "• Tools -> PuerTS -> Generate il2cpp -> Minimal Bridge, Reflection Mode\n\n" +
                    "The build will be cancelled.";

                EditorUtility.DisplayDialog(
                    "Puerts IL2CPP - Missing Required File",
                    dialogMessage,
                    "OK"
                );

                string errorMessage = "[Puerts IL2CPP] Build cancelled. Required file 'Puerts_il2cpp.cpp' not found at: " + requiredFile +
                    ". Generate it first with Tools -> PuerTS -> Generate il2cpp -> Minimal Bridge, Reflection Mode.";
                Debug.LogError(errorMessage);
                throw new BuildFailedException(errorMessage);
            }
            else
            {
                Debug.Log($"[Puerts IL2CPP] Generate check passed. Found: {requiredFile}");
            }
        }

        #endregion
    }
}
#endif
