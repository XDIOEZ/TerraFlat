#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

/// <summary>
/// Project 文件夹收藏夹 - 快速跳转常用文件夹
/// </summary>
[InitializeOnLoad]
public class ProjectFavorites : EditorWindow
{
    private List<string> favorites = new List<string>();
    private List<string> favoriteNames = new List<string>();
    private Vector2 scrollPosition;
    private string newFavoritePath = "Assets";
    private string newFavoriteName = "";
    private bool showAddUI = false;
    private bool enableDebugLog = false;
    private bool showHeaderControls = true;
    private bool reorderMode = false;
    private bool showManageButtons = true;
    
    private Type projectBrowserType;
    private EditorWindow mainProjectWindow;
    
    private string ProjectIdentifier => Application.dataPath.GetHashCode().ToString();
    private string FavoritesPrefKey => "ProjectFavorites_Paths_" + ProjectIdentifier;
    private string NamesPrefKey => "ProjectFavorites_Names_" + ProjectIdentifier;
    private string DebugPrefKey => "ProjectFavorites_Debug_" + ProjectIdentifier;

    [MenuItem("Tools/Project Favorites")]
    private static void ShowWindow()
    {
        var window = GetWindow<ProjectFavorites>("项目收藏夹");
        window.minSize = new Vector2(250, 200);
    }

    private void OnEnable()
    {
        LoadFavorites();
        enableDebugLog = EditorPrefs.GetBool(DebugPrefKey, false);
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        // 尝试获取当前 Project 窗口
        if (mainProjectWindow == null)
        {
            try
            {
                projectBrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
                var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                mainProjectWindow = allWindows.FirstOrDefault(w => w.GetType().Name == "ProjectBrowser");
            }
            catch { }
        }
    }

    private void OnGUI()
    {
        GUILayout.Space(2);
        showHeaderControls = EditorGUILayout.Foldout(showHeaderControls, "显示操作区", true);
        if (showHeaderControls)
        {
            bool newDebugValue = EditorGUILayout.ToggleLeft("启用调试日志", enableDebugLog);
            if (newDebugValue != enableDebugLog)
            {
                enableDebugLog = newDebugValue;
                EditorPrefs.SetBool(DebugPrefKey, enableDebugLog);
            }
        }

        // 添加收藏 UI
        if (showHeaderControls && showAddUI)
        {
            DrawAddFavoriteUI();
            GUILayout.Space(10);
        }
        else if (showHeaderControls)
        {
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("添加当前选中", GUILayout.Height(28)))
            {
                string selectedPath = GetSelectedFolderPath();
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    newFavoritePath = selectedPath;
                    newFavoriteName = Path.GetFileName(selectedPath);
                    showAddUI = true;
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "请在Project窗口中选中一个文件夹", "确定");
                }
            }

            if (GUILayout.Button("手动添加", GUILayout.Height(28)))
            {
                newFavoritePath = "Assets";
                newFavoriteName = "新收藏";
                showAddUI = true;
            }

            if (GUILayout.Button(reorderMode ? "完成排序" : "调整排序", GUILayout.Height(28)))
            {
                reorderMode = !reorderMode;
            }

            if (GUILayout.Button(showManageButtons ? "隐藏管理" : "显示管理", GUILayout.Height(28)))
            {
                showManageButtons = !showManageButtons;
            }
            
            EditorGUILayout.EndHorizontal();
        }

        // 统计信息
        var style = new GUIStyle(EditorStyles.miniLabel);
        style.alignment = TextAnchor.MiddleRight;
        EditorGUILayout.LabelField($"共 {favorites.Count} 个收藏", style);
        
        GUILayout.Space(5);

        // 分隔线
        var separatorRect = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(separatorRect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        
        GUILayout.Space(5);

        // 显示收藏列表
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        if (favorites.Count == 0)
        {
            GUILayout.Space(20);
            EditorGUILayout.HelpBox("暂无收藏\n\n点击上方按钮添加常用文件夹", MessageType.Info);
        }
        else
        {
            for (int i = 0; i < favorites.Count; i++)
            {
                DrawFavoriteItem(i);
            }
        }

        EditorGUILayout.EndScrollView();
        
        GUILayout.Space(5);
    }

    private void DrawAddFavoriteUI()
    {
        EditorGUILayout.LabelField("添加收藏", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginVertical("box");
        
        GUILayout.Space(5);
        
        EditorGUILayout.LabelField("文件夹路径:", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        newFavoritePath = EditorGUILayout.TextField(newFavoritePath, GUILayout.MinHeight(22));
        
        if (GUILayout.Button("选择", GUILayout.Width(50), GUILayout.Height(22)))
        {
            string path = EditorUtility.OpenFolderPanel("选择文件夹", "Assets", "");
            if (!string.IsNullOrEmpty(path))
            {
                if (path.StartsWith(Application.dataPath))
                {
                    newFavoritePath = "Assets" + path.Substring(Application.dataPath.Length);
                }
            }
        }
        EditorGUILayout.EndHorizontal();
        
        GUILayout.Space(5);
        
        EditorGUILayout.LabelField("收藏名称:", EditorStyles.miniLabel);
        newFavoriteName = EditorGUILayout.TextField(newFavoriteName, GUILayout.MinHeight(22));

        GUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("确定", GUILayout.Height(26)))
        {
            if (!string.IsNullOrEmpty(newFavoritePath) && AssetDatabase.IsValidFolder(newFavoritePath))
            {
                if (string.IsNullOrEmpty(newFavoriteName))
                {
                    newFavoriteName = Path.GetFileName(newFavoritePath);
                }

                // 检查是否已存在
                if (!favorites.Contains(newFavoritePath))
                {
                    favorites.Add(newFavoritePath);
                    favoriteNames.Add(newFavoriteName);
                    SaveFavorites();
                    showAddUI = false;
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "该路径已在收藏中", "确定");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("错误", "文件夹路径无效", "确定");
            }
        }

        if (GUILayout.Button("取消", GUILayout.Height(26)))
        {
            showAddUI = false;
        }

        EditorGUILayout.EndHorizontal();
        
        GUILayout.Space(5);
        
        EditorGUILayout.EndVertical();
    }

    private void DrawFavoriteItem(int index)
    {
        string path = favorites[index];
        string name = favoriteNames[index];

        EditorGUILayout.BeginVertical("box");
        
        EditorGUILayout.BeginHorizontal();
        
        // 跳转按钮占据大部分空间
        if (GUILayout.Button(name, GUILayout.Height(18)))
        {
            JumpToFolder(path);
        }

        if (reorderMode)
        {
            GUI.enabled = index > 0;
            if (GUILayout.Button("上移", GUILayout.Width(25), GUILayout.Height(18)))
            {
                MoveFavorite(index, -1);
            }

            GUI.enabled = index < favorites.Count - 1;
            if (GUILayout.Button("下移", GUILayout.Width(25), GUILayout.Height(18)))
            {
                MoveFavorite(index, 1);
            }
            GUI.enabled = true;
        }

        if (showManageButtons)
        {
            // 重命名按钮
            if (GUILayout.Button("重命名", GUILayout.Width(25), GUILayout.Height(18)))
            {
                ShowRenameFavorite(index);
            }

            // 删除按钮
            if (GUILayout.Button("删除", GUILayout.Width(25), GUILayout.Height(18)))
            {
                if (EditorUtility.DisplayDialog("删除收藏", $"确认删除 \"{name}\" 吗", "删除", "取消"))
                {
                    favorites.RemoveAt(index);
                    favoriteNames.RemoveAt(index);
                    SaveFavorites();
                }
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void ShowRenameFavorite(int index)
    {
        string newName = EditorUtility.SaveFilePanel(
            "重命名收藏", 
            "", 
            favoriteNames[index], 
            "");
        
        if (!string.IsNullOrEmpty(newName))
        {
            // 获取文件名作为新的收藏名称
            newName = Path.GetFileNameWithoutExtension(newName);
            if (!string.IsNullOrEmpty(newName))
            {
                favoriteNames[index] = newName;
                SaveFavorites();
            }
        }
    }

    private void MoveFavorite(int index, int direction)
    {
        int newIndex = index + direction;
        if (newIndex < 0 || newIndex >= favorites.Count)
        {
            return;
        }

        string path = favorites[index];
        string name = favoriteNames[index];

        favorites.RemoveAt(index);
        favoriteNames.RemoveAt(index);

        favorites.Insert(newIndex, path);
        favoriteNames.Insert(newIndex, name);

        SaveFavorites();
    }

    private void JumpToFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            EditorUtility.DisplayDialog("错误", $"文件夹不存在: {path}", "确定");
            return;
        }

        // 获取当前 Project 窗口
        if (mainProjectWindow == null)
        {
            EditorApplication.ExecuteMenuItem("Window/General/Project");
            EditorApplication.delayCall += () => JumpToFolder(path);
            return;
        }

        var folderAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (folderAsset == null)
        {
            Debug.LogWarning($"无法加载文件夹: {path}");
            return;
        }

        // 延迟执行，避免 GUI 状态冲突
        EditorApplication.delayCall += () =>
        {
            if (folderAsset == null) return;

            try
            {
                mainProjectWindow.Focus();

                // 不修改 Selection，直接让 Project 窗口定位到文件夹
                var showFolderContents = mainProjectWindow.GetType().GetMethod(
                    "ShowFolderContents",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int), typeof(bool) },
                    null);

                bool jumped = false;
                if (showFolderContents != null && IsProjectBrowserTwoColumn(mainProjectWindow))
                {
                    try
                    {
                        showFolderContents.Invoke(mainProjectWindow, new object[] { folderAsset.GetInstanceID(), true });
                        jumped = true;
                    }
                    catch (TargetInvocationException tie)
                    {
                        Debug.LogWarning($"跳转文件夹失败: {tie.InnerException?.Message ?? tie.Message}");
                    }
                }

                if (!jumped)
                {
                    DebugLog("跳转文件夹失败: 未找到可用的 Project 窗口定位方法或非双栏模式");
                }

                // 仅用于高亮提示，不改变 Inspector 选中
                EditorGUIUtility.PingObject(folderAsset);
            }
            catch (Exception ex)
            {
                DebugLog($"跳转文件夹失败: {ex.Message}");
            }
        };
    }

    private bool IsProjectBrowserTwoColumn(EditorWindow window)
    {
        if (window == null) return false;

        try
        {
            var isTwoColumnProp = window.GetType().GetProperty(
                "isTwoColumn",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (isTwoColumnProp != null && isTwoColumnProp.PropertyType == typeof(bool))
            {
                return (bool)isTwoColumnProp.GetValue(window, null);
            }

            var isTwoColumnMethod = window.GetType().GetMethod(
                "IsTwoColumn",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (isTwoColumnMethod != null && isTwoColumnMethod.ReturnType == typeof(bool))
            {
                return (bool)isTwoColumnMethod.Invoke(window, null);
            }

            var viewModeField = window.GetType().GetField(
                "m_ViewMode",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (viewModeField != null)
            {
                var viewMode = viewModeField.GetValue(window);
                if (viewMode != null)
                {
                    return viewMode.ToString().IndexOf("Two", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"检测 Project 窗口模式失败: {ex.Message}");
        }

        return false;
    }

    private void DebugLog(string message)
    {
        if (enableDebugLog)
        {
            Debug.LogWarning(message);
        }
    }

    private string GetSelectedFolderPath()
    {
        // 获取 Project 窗口中当前选中的文件夹
        if (Selection.activeObject == null)
            return null;

        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        
        if (AssetDatabase.IsValidFolder(path))
        {
            return path;
        }

        // 如果选中的是文件，返回其所在文件夹
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            return directory.Replace('\\', '/');
        }

        return null;
    }

    private void SaveFavorites()
    {
        // 保存路径
        string pathsJson = JsonUtility.ToJson(new SerializableList { items = favorites });
        EditorPrefs.SetString(FavoritesPrefKey, pathsJson);

        // 保存名称
        string namesJson = JsonUtility.ToJson(new SerializableList { items = favoriteNames });
        EditorPrefs.SetString(NamesPrefKey, namesJson);
    }

    private void LoadFavorites()
    {
        favorites.Clear();
        favoriteNames.Clear();

        try
        {
            string pathsJson = EditorPrefs.GetString(FavoritesPrefKey, "");
            string namesJson = EditorPrefs.GetString(NamesPrefKey, "");

            if (!string.IsNullOrEmpty(pathsJson))
            {
                var pathsList = JsonUtility.FromJson<SerializableList>(pathsJson);
                if (pathsList != null && pathsList.items != null)
                {
                    favorites = new List<string>(pathsList.items);
                }
            }

            if (!string.IsNullOrEmpty(namesJson))
            {
                var namesList = JsonUtility.FromJson<SerializableList>(namesJson);
                if (namesList != null && namesList.items != null)
                {
                    favoriteNames = new List<string>(namesList.items);
                }
            }

            // 确保列表长度一致
            while (favoriteNames.Count < favorites.Count)
            {
                favoriteNames.Add(Path.GetFileName(favorites[favoriteNames.Count]));
            }

            while (favorites.Count < favoriteNames.Count)
            {
                favorites.Add("Assets");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"加载收藏失败: {e.Message}");
        }
    }

    [System.Serializable]
    private class SerializableList
    {
        public List<string> items = new List<string>();
    }
}

/// <summary>
/// 右键菜单 - 将当前选中文件夹添加到收藏
/// </summary>
public class ProjectFavoritesMenu
{
    [MenuItem("Assets/添加到收藏夹")]
    private static void AddCurrentFolderToFavorites()
    {
        string selectedPath = GetSelectedFolderPath();
        
        if (string.IsNullOrEmpty(selectedPath))
        {
            EditorUtility.DisplayDialog("提示", "请选中一个文件夹", "确定");
            return;
        }

        // 打开 Project Favorites 窗口
        var window = EditorWindow.GetWindow<ProjectFavorites>("项目收藏夹");
        window.Focus();

        // 通过反射设置新的收藏路径
        var newPathField = typeof(ProjectFavorites).GetField("newFavoritePath", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var newNameField = typeof(ProjectFavorites).GetField("newFavoriteName", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var showAddUIField = typeof(ProjectFavorites).GetField("showAddUI", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (newPathField != null) newPathField.SetValue(window, selectedPath);
        if (newNameField != null) newNameField.SetValue(window, Path.GetFileName(selectedPath));
        if (showAddUIField != null) showAddUIField.SetValue(window, true);
    }

    [MenuItem("Assets/添加到收藏夹", validate = true)]
    private static bool ValidateAddCurrentFolderToFavorites()
    {
        return GetSelectedFolderPath() != null;
    }

    private static string GetSelectedFolderPath()
    {
        if (Selection.activeObject == null)
            return null;

        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        
        if (AssetDatabase.IsValidFolder(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            return directory.Replace('\\', '/');
        }

        return null;
    }
}
#endif
