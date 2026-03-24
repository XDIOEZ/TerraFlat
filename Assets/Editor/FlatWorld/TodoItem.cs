using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 代办事项数据结构
/// </summary>
[System.Serializable]
public class TodoItem
{
    public string title;
    public string description;
    public bool isCompleted;
    public TodoPriority priority;
    public string dueDate;
    public string tags;

    public TodoItem()
    {
        title = "新任务";
        description = "";
        isCompleted = false;
        priority = TodoPriority.Normal;
        dueDate = "";
        tags = "";
    }

    public TodoItem(string title)
    {
        this.title = title;
        description = "";
        isCompleted = false;
        priority = TodoPriority.Normal;
        dueDate = "";
        tags = "";
    }

    public TodoItem(TodoItem other)
    {
        title = other.title;
        description = other.description;
        isCompleted = other.isCompleted;
        priority = other.priority;
        dueDate = other.dueDate;
        tags = other.tags;
    }
}

public enum TodoPriority
{
    Low,
    Normal,
    High,
    Critical
}
