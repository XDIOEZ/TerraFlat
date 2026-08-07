using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 代办事项列表配置 - 用于序列化保存
/// </summary>
public class TodoListConfig : ScriptableObject
{
    public List<TodoItem> items = new();

    private void OnEnable()
    {
        items ??= new List<TodoItem>();
    }

    private void OnValidate()
    {
        items ??= new List<TodoItem>();
    }

    private void MarkDirtyAndSave()
    {
        items ??= new List<TodoItem>();
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssetIfDirty(this);
        AssetDatabase.SaveAssets();
    }

    public void AddItem(TodoItem item)
    {
        items.Add(item);
        MarkDirtyAndSave();
    }

    public void RemoveItem(int index)
    {
        if (index < 0 || index >= items.Count)
            return;

        items.RemoveAt(index);
        MarkDirtyAndSave();
    }

    public void UpdateItem(int index, TodoItem item)
    {
        if (index < 0 || index >= items.Count)
            return;

        items[index] = item;
        MarkDirtyAndSave();
    }

    public void SaveNow()
    {
        MarkDirtyAndSave();
    }
}
