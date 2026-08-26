using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>重新导入已暴露的 Prefab，并验证当前编辑器进程实际加载的 xLua 原生入口。</summary>
[InitializeOnLoad]
internal static class CodexPrefabAndXluaProbeRunner
{
    private const string RequestPath = "Library/CodexProbePrefabAndXlua.request";
    private const string ResultPath = "Library/CodexPrefabAndXluaProbe.result.json";

    /// <summary>脚本重载后安排一次只针对已知问题资产的探测。</summary>
    static CodexPrefabAndXluaProbeRunner()
    {
        if (File.Exists(RequestPath))
            EditorApplication.delayCall += Run;
    }

    /// <summary>强制刷新 Prefab 导入缓存并调用 xLua 版本入口。</summary>
    private static void Run()
    {
        File.Delete(RequestPath);
        try
        {
            string[] paths =
            {
                "Assets/2_Prefabs/Gameplay/Modules/Combat/Mod_ColdWeapon.prefab",
                "Assets/2_Prefabs/Gameplay/Modules/Combat/Module_DamageReciver.prefab",
                "Assets/2_Prefabs/Gameplay/Modules/Variants/DamageSender Axe.prefab",
                "Assets/2_Prefabs/Gameplay/Modules/Variants/DamageSender Pickaxe.prefab"
            };
            var prefabs = new List<object>(paths.Length);
            foreach (string path in paths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                prefabs.Add(new
                {
                    path,
                    mainType = mainType?.FullName ?? string.Empty,
                    loadedType = asset?.GetType().FullName ?? string.Empty,
                    loaded = asset != null
                });
            }

            Type luaType = Type.GetType("XLua.LuaDLL.Lua, Xlua", true);
            MethodInfo versionMethod = luaType.GetMethod("xlua_get_lib_version", BindingFlags.Static | BindingFlags.Public);
            if (versionMethod == null)
                throw new MissingMethodException(luaType.FullName, "xlua_get_lib_version");
            int xLuaVersion = Convert.ToInt32(versionMethod.Invoke(null, null));
            WriteResult(new { success = true, xLuaVersion, prefabs });
        }
        catch (Exception exception)
        {
            WriteResult(new { success = false, exception = exception.ToString() });
        }
    }

    /// <summary>以 UTF-8 JSON 保存探测结果。</summary>
    private static void WriteResult(object value)
    {
        File.WriteAllText(ResultPath, JsonConvert.SerializeObject(value, Formatting.Indented));
    }
}
