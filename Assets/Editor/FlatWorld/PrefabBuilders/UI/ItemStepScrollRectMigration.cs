using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>显式迁移项目 UI 的滚动脚本，保留原组件 fileID、全部序列化字段和跨对象引用。</summary>
public static class ItemStepScrollRectMigration
{
    #region 正式资源迁移

    private const string TargetScriptPath = "Assets/5_Scripts/5-5_UI/Common/Controls/ItemStepScrollRect.cs";
    private const string OriginalGuid = "1aa08ab6e0800fa44ae55d278d1423e3";

    /// <summary>仅在显式菜单调用时处理正式 Prefab/场景，不在启动或资源导入时修改项目。</summary>
    [MenuItem("FlatWorld/UI/统一所有列表为逐项平滑滚动")]
    public static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 Play Mode 再迁移 UI Prefab。");
        string guid = AssetDatabase.AssetPathToGUID(TargetScriptPath);
        if (string.IsNullOrWhiteSpace(guid)) throw new InvalidOperationException("逐项滚动脚本尚未导入。");
        var pattern = new Regex(@"(m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*)" + OriginalGuid + @"(?=,)");
        int assets = 0, components = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string root in new[] { "Assets/2_Prefabs", "Assets/3_Scenes", "Assets/Resources" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(path);
                    if (extension != ".prefab" && extension != ".unity") continue;
                    string text = File.ReadAllText(path);
                    int count = pattern.Matches(text).Count;
                    if (count == 0) continue;
                    File.WriteAllText(path, pattern.Replace(text, match => match.Groups[1].Value + guid), new UTF8Encoding(false));
                    assets++; components += count;
                    AssetDatabase.ImportAsset(path.Replace('\\', '/'));
                }
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ItemStepScrollRect] 已迁移 {assets} 个资源、{components} 个滚动组件，原 fileID 和引用保持不变。");
    }

    #endregion
}
