using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using MemoryPack;
using Sirenix.OdinInspector;

public class MapSaveEditor : MonoBehaviour
{
    [FolderPath]
    public string savePath = "Assets/Saves/Map"; // 默认保存路径
    public string fileName = ""; // 默认文件名
    //TODO 如果文件名为空(也就是玩家没有输入文件名)，则 使用MapSave.MapName作为文件名
    [ShowInInspector]
    private string statusMessage = "";

    public MapSave mapSave; // Inspector中可以预览
    public TextAsset mapSaveAsset; // 可挂接 TextAsset

    [ShowInInspector]
    private List<TextAsset> mapFiles = new List<TextAsset>(); // 自动扫描的TextAsset列表


    [ShowInInspector]
    private int selectedIndex = 0; // 当前选择的文件索引

    [Button("刷新地图列表")]
    public void RefreshMapFiles()
    {
        mapFiles.Clear();
        if (Directory.Exists(savePath))
        {
            string[] files = Directory.GetFiles(savePath, "*.bytes");
            foreach (var file in files)
            {
                string assetPath = file.Replace(Application.dataPath, "Assets").Replace("\\", "/");
                TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                if (textAsset != null)
                    mapFiles.Add(textAsset);
            }
        }

        if (mapFiles.Count > 0)
            statusMessage = $"找到 {mapFiles.Count} 个地图文件";
        else
            statusMessage = "未找到地图文件";
    }



    [Button("打开选中地图")]
    public void OpenSelectedMap()
    {
        if (mapFiles.Count == 0) return;

        mapSaveAsset = mapFiles[selectedIndex];
        mapSave = MemoryPackSerializer.Deserialize<MapSave>(mapSaveAsset.bytes);

        statusMessage = $"已打开：{mapSaveAsset.name}";
        Debug.Log(statusMessage);
    }


    // 挂接TextAsset并加载
    [Button("打开文本编辑器")]
    public void TextEditor()
    {
        if (mapSaveAsset == null)
        {
            statusMessage = "请先挂接一个 TextAsset！";
            return;
        }

        mapSave = MemoryPackSerializer.Deserialize<MapSave>(mapSaveAsset.bytes);
        statusMessage = $"已加载挂接 TextAsset：{mapSaveAsset.name}";
    }

    // 保存覆盖挂接的TextAsset
    [Button("保存覆盖当前挂接地图")]
    public void TextEditorDone()
    {
        if (mapSaveAsset == null)
        {
            statusMessage = "请先挂接一个 TextAsset！";
            Debug.LogError(statusMessage);
            return;
        }

        string path = AssetDatabase.GetAssetPath(mapSaveAsset);
        byte[] data = MemoryPackSerializer.Serialize(mapSave);

        File.WriteAllBytes(path, data);
        AssetDatabase.Refresh();
        statusMessage = $"保存成功：{path}";
        Debug.Log(statusMessage);
    }

    // 清空 MapSave
    [Button("清空MapSave")]
    public void ClearMapSave()
    {
        mapSave = new MapSave();
        statusMessage = "MapSave 已清空";
    }

    [Button("保存当前地图")]
    private void SaveCurrentMap()
    {
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        var currentMap = SaveLoadManager.GetCurrentMapStatic();
        if (currentMap == null)
        {
            statusMessage = "获取 MapSave 失败，请确保场景已初始化！";
            Debug.LogWarning(statusMessage);
            return;
        }

        // 如果玩家没有输入文件名，则使用 MapSave.MapName
        string finalFileName = string.IsNullOrEmpty(fileName) ? currentMap.MapName + ".bytes" : fileName;

        try
        {
            byte[] bytes = MemoryPackSerializer.Serialize(currentMap);
            string fullPath = Path.Combine(savePath, finalFileName);
            File.WriteAllBytes(fullPath, bytes);
            AssetDatabase.Refresh();
            statusMessage = $"保存成功：{fullPath}";
            Debug.Log(statusMessage);
        }
        catch (System.Exception ex)
        {
            statusMessage = $"保存失败：{ex.Message}";
            Debug.LogError(statusMessage);
        }
    }

}
