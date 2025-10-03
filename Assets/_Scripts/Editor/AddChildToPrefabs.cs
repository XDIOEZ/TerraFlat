using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class AddChildToPrefabs : EditorWindow
{
    private GameObject childPrefab;
    private List<GameObject> targetPrefabs = new List<GameObject>();

    [MenuItem("Tools/批量挂接Prefab (Undo版)")]
    public static void ShowWindow()
    {
        GetWindow<AddChildToPrefabs>("批量挂接Prefab");
    }

    void OnGUI()
    {
        GUILayout.Label("选择要挂接的Prefab", EditorStyles.boldLabel);
        childPrefab = (GameObject)EditorGUILayout.ObjectField("源Prefab", childPrefab, typeof(GameObject), false);

        GUILayout.Space(10);
        GUILayout.Label("目标Prefabs (支持拖拽)", EditorStyles.boldLabel);

        // 拖拽区域
        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "将目标Prefab拖拽到此处\n或点击下方按钮手动添加");

        // 拖拽处理
        switch (Event.current.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!dropArea.Contains(Event.current.mousePosition)) break;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (Event.current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object draggedObject in DragAndDrop.objectReferences)
                    {
                        if (draggedObject is GameObject prefab)
                        {
                            string path = AssetDatabase.GetAssetPath(prefab);
                            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                            {
                                if (!targetPrefabs.Contains(prefab))
                                    targetPrefabs.Add(prefab);
                            }
                        }
                    }
                    Event.current.Use();
                    GUI.changed = true;
                }
                break;
        }

        GUILayout.Space(10);

        // 显示已选择的 Prefabs
        if (targetPrefabs.Count > 0)
        {
            GUILayout.Label($"已选择 {targetPrefabs.Count} 个目标Prefab:");

            for (int i = 0; i < targetPrefabs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                targetPrefabs[i] = (GameObject)EditorGUILayout.ObjectField(targetPrefabs[i], typeof(GameObject), false);
                if (GUILayout.Button("×", GUILayout.Width(25)))
                {
                    targetPrefabs.RemoveAt(i);
                    GUI.changed = true;
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("清空列表"))
            {
                targetPrefabs.Clear();
                GUI.changed = true;
            }
        }

        GUILayout.Space(10);

        if (GUILayout.Button("手动添加目标Prefab"))
        {
            targetPrefabs.Add(null);
        }

        GUILayout.Space(20);

        if (GUILayout.Button("批量挂接Prefab", GUILayout.Height(40)))
        {
            ApplyChanges();
        }
    }

    void ApplyChanges()
    {
        if (childPrefab == null)
        {
            Debug.LogError("请先选择要挂接的源Prefab");
            return;
        }

        if (targetPrefabs.Count == 0)
        {
            Debug.LogError("请至少选择一个目标Prefab");
            return;
        }

        string sourcePrefabPath = AssetDatabase.GetAssetPath(childPrefab);
        if (string.IsNullOrEmpty(sourcePrefabPath) || !sourcePrefabPath.EndsWith(".prefab"))
        {
            Debug.LogError("源Prefab无效，请确保选择的是Prefab Asset");
            return;
        }

        int successCount = 0;
        for (int i = 0; i < targetPrefabs.Count; i++)
        {
            GameObject targetPrefab = targetPrefabs[i];
            if (targetPrefab == null) continue;

            string targetPath = AssetDatabase.GetAssetPath(targetPrefab);
            if (string.IsNullOrEmpty(targetPath) || !targetPath.EndsWith(".prefab"))
            {
                Debug.LogWarning($"{targetPrefab?.name} 不是有效的Prefab，已跳过");
                continue;
            }

            try
            {
                EditorUtility.DisplayProgressBar("批量挂接Prefab", $"处理 {targetPrefab.name}", (float)i / targetPrefabs.Count);

                // 加载 Prefab 内容
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(targetPath);
                if (prefabRoot != null)
                {
                    bool alreadyExists = prefabRoot.transform.Find(childPrefab.name) != null;

                    if (!alreadyExists)
                    {
                        // 注册 Undo
                        Undo.RegisterFullObjectHierarchyUndo(prefabRoot, "批量挂接Prefab");

                        GameObject childInstance = (GameObject)PrefabUtility.InstantiatePrefab(childPrefab, prefabRoot.transform);
                        if (childInstance != null)
                        {
                            childInstance.name = childPrefab.name;
                            successCount++;
                            Debug.Log($"✓ 已为 {targetPrefab.name} 挂接Prefab {childPrefab.name}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"⚠ {targetPrefab.name} 中已存在名为 {childPrefab.name} 的子对象，已跳过");
                    }

                    // 保存并卸载
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, targetPath);
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"✗ 处理 {targetPrefab?.name} 时出错: {e.Message}");
            }
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();
        Debug.Log($"批量挂接完成！成功处理 {successCount}/{targetPrefabs.Count} 个Prefab");
    }
}
