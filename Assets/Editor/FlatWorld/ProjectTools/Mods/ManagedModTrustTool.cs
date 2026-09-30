using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>检查 C# MOD 的清单和程序集元数据，不执行 MOD 代码。</summary>
public static class ManagedModTrustTool
{
    #region MOD 包检查
    [MenuItem("FlatWorld/MOD/检查 C# MOD 包")]
    private static void ValidatePackage()
    {
        string root = EditorUtility.OpenFolderPanel("选择需要检查的 MOD 包", Path.Combine(Application.persistentDataPath, "Mods"), "");
        if (string.IsNullOrWhiteSpace(root)) return;
        try
        {
            var manifest = JsonConvert.DeserializeObject<ModManifest>(File.ReadAllText(Path.Combine(root, "manifest.json")));
            if (manifest?.Managed == null || string.IsNullOrWhiteSpace(manifest.Id)) throw new InvalidOperationException("清单没有有效 C# MOD 声明。");
            ModManagedAssemblyStore.ValidateDefinition(root, manifest.Managed);
            Debug.Log("C# MOD 包检查通过：“" + manifest.Id + "”。启用后重启游戏即可加载，无需单独授权。");
        }
        catch (Exception error) { Debug.LogException(error); }
    }
    #endregion
}
