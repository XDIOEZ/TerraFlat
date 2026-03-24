using UnityEditor;
using UnityEngine;

/// <summary>
/// 代办事项表编辑器窗口
/// </summary>
public class TodoListWindow : EditorWindow
{
    #region 字段定义
    
    private TodoListConfig _config;
    private Vector2 _scrollPosition;
    private int _selectedIndex = -1;
    private bool _showNewItemPanel;
    private string _newItemTitle = "";
    private TodoPriority _filterPriority = TodoPriority.Normal;
    private bool _enablePriorityFilter;
    private bool _showCompleted = true;
    private string _searchText = "";
    private TodoItem _editingItem;
    
    private const string CONFIG_PATH = "Assets/Editor/FlatWorld/TodoListConfig.asset";
    
    #endregion

    #region 菜单项与初始化
    
    [MenuItem("Tools/代办事项表 &T")]
    public static void ShowWindow()
    {
        GetWindow<TodoListWindow>("代办事项");
    }

    private void OnEnable()
    {
        LoadConfig();
    }

    private void OnDisable()
    {
        SaveConfigIfPossible();
    }

    private void OnLostFocus()
    {
        SaveConfigIfPossible();
    }

    private void LoadConfig()
    {
        _config = AssetDatabase.LoadAssetAtPath<TodoListConfig>(CONFIG_PATH);
        if (_config == null)
        {
            _config = CreateInstance<TodoListConfig>();
            AssetDatabase.CreateAsset(_config, CONFIG_PATH);
            AssetDatabase.SaveAssets();
        }

        _config.items ??= new System.Collections.Generic.List<TodoItem>();
    }

    private void SaveConfigIfPossible()
    {
        if (_config == null)
            return;

        _config.SaveNow();
    }
    
    #endregion

    #region GUI绘制

    private void OnGUI()
    {
        if (_config == null)
        {
            EditorGUILayout.HelpBox("未能加载待办事项配置", MessageType.Error);
            return;
        }

        DrawTopBar();
        EditorGUILayout.Space(10);
        DrawFilterBar();
        EditorGUILayout.Space(10);
        DrawItemsList();
    }

    private void DrawTopBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            if (GUILayout.Button("➕ 新建", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                _showNewItemPanel = !_showNewItemPanel;
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField($"总任务: {_config.items.Count}", EditorStyles.miniLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField($"已完成: {GetCompletedCount()}", EditorStyles.miniLabel, GUILayout.Width(100));
        }
        EditorGUILayout.EndHorizontal();

        if (_showNewItemPanel)
        {
            DrawNewItemPanel();
        }
    }

    private void DrawNewItemPanel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        {
            EditorGUILayout.LabelField("创建新任务", EditorStyles.boldLabel);
            
            _newItemTitle = EditorGUILayout.TextField("任务标题:", _newItemTitle);

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("创建", GUILayout.Width(60)))
                {
                    if (!string.IsNullOrEmpty(_newItemTitle))
                    {
                        _config.AddItem(new TodoItem(_newItemTitle));
                        _newItemTitle = "";
                        _showNewItemPanel = false;
                    }
                }

                if (GUILayout.Button("取消", GUILayout.Width(60)))
                {
                    _showNewItemPanel = false;
                    _newItemTitle = "";
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawFilterBar()
    {
        EditorGUILayout.BeginHorizontal();
        {
            _searchText = EditorGUILayout.TextField("🔍 搜索:", _searchText, GUILayout.Width(250));
            
            _enablePriorityFilter = EditorGUILayout.ToggleLeft("启用优先级", _enablePriorityFilter, GUILayout.Width(90));
            _filterPriority = (TodoPriority)EditorGUILayout.EnumPopup("优先级筛选:", _filterPriority);
            
            _showCompleted = EditorGUILayout.ToggleLeft("显示已完成", _showCompleted, GUILayout.Width(100));
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawItemsList()
    {
        EditorGUILayout.LabelField("待办事项", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        {
            for (int i = 0; i < _config.items.Count; i++)
            {
                if (ShouldShowItem(i))
                {
                    DrawTodoItem(i);
                }
            }

            if (_config.items.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无任务，快去创建一个吧！", MessageType.Info);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private bool ShouldShowItem(int index)
    {
        var item = _config.items[index];
        
        if (!_showCompleted && item.isCompleted)
            return false;

        if (!string.IsNullOrEmpty(_searchText) && !item.title.Contains(_searchText))
            return false;

        if (_enablePriorityFilter && item.priority != _filterPriority)
            return false;

        return true;
    }

    private void DrawTodoItem(int index)
    {
        var item = _config.items[index];
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        {
            EditorGUILayout.BeginHorizontal();
            {
                bool newCompleted = EditorGUILayout.Toggle(item.isCompleted, GUILayout.Width(20));
                if (newCompleted != item.isCompleted)
                {
                    item.isCompleted = newCompleted;
                    _config.UpdateItem(index, item);
                }

                EditorGUILayout.BeginVertical();
                {
                    var titleStyle = new GUIStyle(EditorStyles.label)
                    {
                        fontStyle = item.isCompleted ? FontStyle.BoldAndItalic : FontStyle.Bold,
                    };
                    
                    if (item.isCompleted)
                        GUI.color = Color.gray;

                    EditorGUILayout.LabelField($"[{GetPriorityEmoji(item.priority)}] {item.title}", titleStyle);
                    
                    GUI.color = Color.white;
                }
                EditorGUILayout.EndVertical();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("✎", GUILayout.Width(25)))
                {
                    if (_selectedIndex == index)
                    {
                        _selectedIndex = -1;
                        _editingItem = null;
                    }
                    else
                    {
                        _selectedIndex = index;
                        _editingItem = new TodoItem(item);
                    }
                }

                if (GUILayout.Button("✕", GUILayout.Width(25)))
                {
                    _config.RemoveItem(index);
                    if (_selectedIndex == index)
                    {
                        _selectedIndex = -1;
                        _editingItem = null;
                    }
                    return;
                }
            }
            EditorGUILayout.EndHorizontal();

            // 展开编辑面板
            if (_selectedIndex == index)
            {
                EditorGUILayout.Space(5);
                DrawItemEditor(index);
            }
        }
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.Space(3);
    }

    private void DrawItemEditor(int index)
    {
        if (_editingItem == null)
            _editingItem = new TodoItem(_config.items[index]);

        EditorGUILayout.LabelField("编辑详情", EditorStyles.miniLabel);
        EditorGUI.indentLevel++;
        {
            _editingItem.title = EditorGUILayout.TextField("标题:", _editingItem.title);
            _editingItem.description = EditorGUILayout.TextArea(_editingItem.description, GUILayout.Height(50));
            _editingItem.priority = (TodoPriority)EditorGUILayout.EnumPopup("优先级:", _editingItem.priority);
            _editingItem.dueDate = EditorGUILayout.TextField("截止日期:", _editingItem.dueDate);
            _editingItem.tags = EditorGUILayout.TextField("标签:", _editingItem.tags);

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("保存"))
                {
                    _config.UpdateItem(index, new TodoItem(_editingItem));
                    _selectedIndex = -1;
                    _editingItem = null;
                }

                if (GUILayout.Button("取消"))
                {
                    _selectedIndex = -1;
                    _editingItem = null;
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUI.indentLevel--;
    }

    #endregion

    #region 辅助方法

    private int GetCompletedCount()
    {
        int count = 0;
        foreach (var item in _config.items)
        {
            if (item.isCompleted)
                count++;
        }
        return count;
    }

    private string GetPriorityEmoji(TodoPriority priority)
    {
        return priority switch
        {
            TodoPriority.Low => "🟢",
            TodoPriority.Normal => "🟡",
            TodoPriority.High => "🔴",
            TodoPriority.Critical => "🔥",
            _ => "❓"
        };
    }

    #endregion
}
