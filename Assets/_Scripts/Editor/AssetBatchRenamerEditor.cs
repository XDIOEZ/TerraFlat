# if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class AssetBatchRenamerEditor : EditorWindow
{
    private List<Object> objectsToRename = new List<Object>();
    private string namePrefix = "Item_";
    private string nameSuffix = "";
    private int startIndex = 1;
    private int digitCount = 3;

    private Stack<KeyValuePair<string, string>> renameHistory = new Stack<KeyValuePair<string, string>>();
    private bool isRenaming = false; // 标志是否正在进行重命名
    private bool cancelRenaming = false; // 标志是否取消重命名操作
    private CancellationTokenSource cancellationTokenSource; // 取消标志源

    [MenuItem("Tools/批量素材重命名工具")]
    public static void ShowWindow()
    {
        GetWindow<AssetBatchRenamerEditor>("批量素材重命名");
    }

    private void OnGUI()
    {
        GUILayout.Label("拖拽素材或文件夹到下方区域", EditorStyles.boldLabel);

        Rect dropArea = GUILayoutUtility.GetRect(0, 100, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "拖拽素材或文件夹到此");

        Event evt = Event.current;
        if (dropArea.Contains(evt.mousePosition))
        {
            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object draggedObject in DragAndDrop.objectReferences)
                    {
                        string path = AssetDatabase.GetAssetPath(draggedObject);
                        if (Directory.Exists(path))
                        {
                            string[] guids = AssetDatabase.FindAssets("", new[] { path });
                            foreach (string guid in guids)
                            {
                                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                                if (!Directory.Exists(assetPath))
                                {
                                    Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                                    if (asset != null && !objectsToRename.Contains(asset))
                                    {
                                        objectsToRename.Add(asset);
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (!objectsToRename.Contains(draggedObject))
                            {
                                objectsToRename.Add(draggedObject);
                            }
                        }
                    }
                    evt.Use();
                }
            }
        }

        GUILayout.Space(10);

        namePrefix = EditorGUILayout.TextField("名称前缀", namePrefix);
        nameSuffix = EditorGUILayout.TextField("名称后缀", nameSuffix);
        startIndex = EditorGUILayout.IntField("起始编号", startIndex);
        digitCount = EditorGUILayout.IntField("编号位数", digitCount);

        GUILayout.Space(10);

        if (GUILayout.Button("开始重命名"))
        {
            RenameAssetsWithProgress();
        }

        if (GUILayout.Button("撤销重命名"))
        {
            UndoRenameAssets();
        }

        if (GUILayout.Button("清空列表"))
        {
            objectsToRename.Clear();
        }

        if (GUILayout.Button("取消重命名"))
        {
            CancelRenaming();
        }

        GUILayout.Space(10);
        GUILayout.Label($"已添加素材数量：{objectsToRename.Count}");
    }

    private async void RenameAssetsWithProgress()
    {
        if (isRenaming)
        {
            Debug.LogWarning("重命名已经在进行中，无法重复开始！");
            return;
        }

        renameHistory.Clear();
        cancelRenaming = false; // 重置取消标志

        int total = objectsToRename.Count;
        if (total == 0)
        {
            Debug.LogWarning("没有可重命名的资源！");
            return;
        }

        cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;

        isRenaming = true; // 开始重命名

        try
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < total; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        break; // 如果被取消，跳出循环
                    }

                    Object obj = objectsToRename[i];
                    string oldPath = AssetDatabase.GetAssetPath(obj);

                    // 检查是否是 Sprite 类型
                    if (obj is Sprite sprite)
                    {
                        string texturePath = AssetDatabase.GetAssetPath(sprite.texture);
                        string textureDirectory = Path.GetDirectoryName(texturePath);
                        string extension = Path.GetExtension(texturePath);

                        string numberStr = (startIndex + i).ToString().PadLeft(digitCount, '0');
                        string newName = $"{namePrefix}{numberStr}{nameSuffix}";
                        string newTexturePath = Path.Combine(textureDirectory, newName + extension).Replace("\\", "/");

                        // 记录旧名字和新名字，以便撤销
                        renameHistory.Push(new KeyValuePair<string, string>(texturePath, newTexturePath));

                        // 重命名关联的 Texture2D 文件
                        string result = AssetDatabase.RenameAsset(texturePath, newName);
                        if (!string.IsNullOrEmpty(result))
                        {
                            Debug.LogError(result);
                        }
                    }
                    else
                    {
                        string directory = Path.GetDirectoryName(oldPath);
                        string extension = Path.GetExtension(oldPath);

                        string numberStr = (startIndex + i).ToString().PadLeft(digitCount, '0');
                        string newName = $"{namePrefix}{numberStr}{nameSuffix}";
                        string newPath = Path.Combine(directory, newName + extension).Replace("\\", "/");

                        // 记录旧名字和新名字，以便撤销
                        renameHistory.Push(new KeyValuePair<string, string>(oldPath, newPath));

                        string result = AssetDatabase.RenameAsset(oldPath, newName);
                        if (!string.IsNullOrEmpty(result))
                        {
                            Debug.LogError(result);
                        }
                    }
                }
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("✅ 重命名完成！");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("❌ 重命名过程中出错！");
            Debug.LogException(ex);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            isRenaming = false; // 重命名操作结束
        }
    }


    private void UndoRenameAssets()
    {
        if (renameHistory.Count == 0)
        {
            Debug.LogWarning("没有可以撤销的操作！");
            return;
        }

        try
        {
            // 按顺序撤销之前的重命名
            while (renameHistory.Count > 0)
            {
                var pair = renameHistory.Pop();
                string oldPath = pair.Key;
                string newPath = pair.Value;

                string oldName = Path.GetFileNameWithoutExtension(oldPath);
                string result = AssetDatabase.RenameAsset(newPath, oldName);

                if (string.IsNullOrEmpty(result))
                {
                    Debug.Log($"✅ 已恢复：{newPath} → {oldName}");
                }
                else
                {
                    Debug.LogError(result);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("❌ 撤销操作失败！");
            Debug.LogException(ex);
        }
    }

    private void CancelRenaming()
    {
        if (!isRenaming)
        {
            Debug.LogWarning("没有进行中的重命名操作！");
            return;
        }

        // 取消操作
        cancellationTokenSource?.Cancel();

        // 取消重命名时，恢复所有已修改的资源
        while (renameHistory.Count > 0)
        {
            var pair = renameHistory.Pop();
            string oldPath = pair.Key;
            string newPath = pair.Value;

            string oldName = Path.GetFileNameWithoutExtension(oldPath);
            AssetDatabase.RenameAsset(newPath, oldName); // 恢复旧名称
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.ClearProgressBar();
        isRenaming = false; // 重命名操作结束
        Debug.Log("❌ 重命名操作已取消！");
    }
}
#endif