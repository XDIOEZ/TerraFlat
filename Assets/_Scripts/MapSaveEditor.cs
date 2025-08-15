using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using MemoryPack;
using Sirenix.OdinInspector;

public class MapSaveEditor : MonoBehaviour
{
    [FolderPath]
    public string savePath = "Assets/Saves/Map"; // Ĭ�ϱ���·��
    public string fileName = ""; // Ĭ���ļ���
    //TODO ����ļ���Ϊ��(Ҳ�������û�������ļ���)���� ʹ��MapSave.MapName��Ϊ�ļ���
    [ShowInInspector]
    private string statusMessage = "";

    public MapSave mapSave; // Inspector�п���Ԥ��
    public TextAsset mapSaveAsset; // �ɹҽ� TextAsset

    [ShowInInspector]
    private List<TextAsset> mapFiles = new List<TextAsset>(); // �Զ�ɨ���TextAsset�б�


    [ShowInInspector]
    private int selectedIndex = 0; // ��ǰѡ����ļ�����

    [Button("ˢ�µ�ͼ�б�")]
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
            statusMessage = $"�ҵ� {mapFiles.Count} ����ͼ�ļ�";
        else
            statusMessage = "δ�ҵ���ͼ�ļ�";
    }



    [Button("��ѡ�е�ͼ")]
    public void OpenSelectedMap()
    {
        if (mapFiles.Count == 0) return;

        mapSaveAsset = mapFiles[selectedIndex];
        mapSave = MemoryPackSerializer.Deserialize<MapSave>(mapSaveAsset.bytes);

        statusMessage = $"�Ѵ򿪣�{mapSaveAsset.name}";
        Debug.Log(statusMessage);
    }


    // �ҽ�TextAsset������
    [Button("���ı��༭��")]
    public void TextEditor()
    {
        if (mapSaveAsset == null)
        {
            statusMessage = "���ȹҽ�һ�� TextAsset��";
            return;
        }

        mapSave = MemoryPackSerializer.Deserialize<MapSave>(mapSaveAsset.bytes);
        statusMessage = $"�Ѽ��عҽ� TextAsset��{mapSaveAsset.name}";
    }

    // ���渲�ǹҽӵ�TextAsset
    [Button("���渲�ǵ�ǰ�ҽӵ�ͼ")]
    public void TextEditorDone()
    {
        if (mapSaveAsset == null)
        {
            statusMessage = "���ȹҽ�һ�� TextAsset��";
            Debug.LogError(statusMessage);
            return;
        }

        string path = AssetDatabase.GetAssetPath(mapSaveAsset);
        byte[] data = MemoryPackSerializer.Serialize(mapSave);

        File.WriteAllBytes(path, data);
        AssetDatabase.Refresh();
        statusMessage = $"����ɹ���{path}";
        Debug.Log(statusMessage);
    }

    // ��� MapSave
    [Button("���MapSave")]
    public void ClearMapSave()
    {
        mapSave = new MapSave();
        statusMessage = "MapSave �����";
    }

    [Button("���浱ǰ��ͼ")]
    private void SaveCurrentMap()
    {
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        var currentMap = SaveLoadManager.GetCurrentMapStatic();
        if (currentMap == null)
        {
            statusMessage = "��ȡ MapSave ʧ�ܣ���ȷ�������ѳ�ʼ����";
            Debug.LogWarning(statusMessage);
            return;
        }

        // ������û�������ļ�������ʹ�� MapSave.MapName
        string finalFileName = string.IsNullOrEmpty(fileName) ? currentMap.MapName + ".bytes" : fileName;

        try
        {
            byte[] bytes = MemoryPackSerializer.Serialize(currentMap);
            string fullPath = Path.Combine(savePath, finalFileName);
            File.WriteAllBytes(fullPath, bytes);
            AssetDatabase.Refresh();
            statusMessage = $"����ɹ���{fullPath}";
            Debug.Log(statusMessage);
        }
        catch (System.Exception ex)
        {
            statusMessage = $"����ʧ�ܣ�{ex.Message}";
            Debug.LogError(statusMessage);
        }
    }

}
