using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>显式授权指定 C# MOD 代码版本，查看指纹和取消授权均不加载 DLL。</summary>
public static class ManagedModTrustTool
{
    #region 用户授权
    [MenuItem("FlatWorld/MOD/授权 C# MOD 代码版本")]
    private static void Trust()
    {
        string root = EditorUtility.OpenFolderPanel("选择需要授权的 MOD 包", Path.Combine(Application.persistentDataPath, "Mods"), "");
        if (string.IsNullOrWhiteSpace(root)) return;
        try
        {
            var manifest = JsonConvert.DeserializeObject<ModManifest>(File.ReadAllText(Path.Combine(root, "manifest.json")));
            if (manifest?.Managed == null || string.IsNullOrWhiteSpace(manifest.Id)) throw new InvalidOperationException("清单没有有效 C# MOD 声明。");
            var code = ModManagedAssemblyStore.ReadPackage(root, manifest.Managed);
            if (!EditorUtility.DisplayDialog("信任此 C# MOD？",
                    manifest.Id + "\nSHA256: " + code.Fingerprint +
                    "\n\n托管代码可访问游戏进程和当前用户权限下的文件，不受 Lua 沙箱限制。只授权你信任的作者。代码变化后必须重新授权。", "授权此版本", "取消")) return;
            ModProfileStore.SetManagedCodeTrust(manifest.Id, code.Fingerprint, true);
            Debug.Log("已授权 C# MOD 代码版本（尚未执行）：“" + manifest.Id + "”。返回主菜单重新载入内容；换 DLL 后须重启游戏。");
        }
        catch (Exception error) { Debug.LogException(error); }
    }
    #endregion
}
