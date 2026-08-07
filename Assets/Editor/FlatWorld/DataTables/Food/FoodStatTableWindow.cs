using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 食物数值总表窗口
/// </summary>
public class FoodStatTableWindow : EditorWindow
{
    #region 常量
    private const string ConfigPath = "Assets/Editor/FlatWorld/DataTables/Food/FoodStatTableConfig.asset";
    #endregion

    #region 字段
    private FoodStatTableConfig _config; // 表格配置
    private Vector2 _scrollPosition; // 滚动位置
    private string _searchText = string.Empty; // 搜索文本
    private bool _showOnlyNutritionRows; // 仅显示有营养的行
    private bool _showOnlySpoilageRows; // 仅显示有腐败配置的行
    private int _selectedRowIndex = -1; // 选中行索引
    private int _highlightRowIndex = -1; // 高亮行索引
    private double _highlightUntilTime; // 高亮结束时间
    private const double HighlightDuration = 1.2d; // 高亮持续时间
    #endregion

    #region 菜单与初始化
    [MenuItem("FlatWorld/食物数值表")]
    public static void OpenWindow()
    {
        GetWindow<FoodStatTableWindow>("食物数值表");
    }

    private void OnEnable()
    {
        LoadOrCreateConfig();
    }
    #endregion

    #region GUI
    private void OnGUI()
    {
        if (_config == null)
        {
            EditorGUILayout.HelpBox("未能加载食物数值表配置", MessageType.Error);
            if (GUILayout.Button("重新加载"))
            {
                LoadOrCreateConfig();
            }

            return;
        }

        DrawToolbar();
        DrawFilterBar();
        EditorGUILayout.Space(6);
        DrawTable();
        EditorGUILayout.Space(6);
        DrawBottomBar();

        if (EditorApplication.timeSinceStartup < _highlightUntilTime)
        {
            Repaint();
        }
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            if (GUILayout.Button("扫描 Prefab", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                ScanAllPrefabs();
            }

            if (GUILayout.Button("应用全部", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                ApplyAllRowsToPrefabs();
            }

            if (GUILayout.Button("保存表格", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                _config.SaveNow();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"行数: {_config.Rows.Count}", EditorStyles.miniLabel, GUILayout.Width(80));
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawFilterBar()
    {
        EditorGUILayout.BeginHorizontal();
        {
            _searchText = EditorGUILayout.TextField("搜索", _searchText);
            _showOnlyNutritionRows = EditorGUILayout.ToggleLeft("仅营养", _showOnlyNutritionRows, GUILayout.Width(70));
            _showOnlySpoilageRows = EditorGUILayout.ToggleLeft("仅腐败", _showOnlySpoilageRows, GUILayout.Width(70));
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTable()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Prefab", GUILayout.Width(150));
                EditorGUILayout.LabelField("碳水/脂肪/蛋白/水/维生", GUILayout.Width(200));
                EditorGUILayout.LabelField("进度", GUILayout.Width(50));
                EditorGUILayout.LabelField("消耗速度", GUILayout.Width(70));
                EditorGUILayout.LabelField("腐败", GUILayout.Width(80));
                EditorGUILayout.LabelField("状态", GUILayout.Width(100));
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            {
                for (int i = 0; i < _config.Rows.Count; i++)
                {
                    FoodStatTableRow row = _config.Rows[i];
                    if (!ShouldShowRow(row))
                    {
                        continue;
                    }

                    DrawRow(i, row);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawRow(int index, FoodStatTableRow row)
    {
        bool isSelectedRow = index == _selectedRowIndex;
        bool isFlashRow = IsRowFlashHighlighted(index);
        Color oldColor = GUI.color;
        if (isSelectedRow)
        {
            GUI.color = Color.Lerp(oldColor, new Color(1f, 0.93f, 0.45f, 1f), 0.65f);
        }
        else if (isFlashRow)
        {
            float remainRate = (float)((_highlightUntilTime - EditorApplication.timeSinceStartup) / HighlightDuration);
            remainRate = Mathf.Clamp01(remainRate);
            float pulse = 0.7f + 0.3f * Mathf.PingPong((float)EditorApplication.timeSinceStartup * 4f, 1f);
            GUI.color = Color.Lerp(oldColor, new Color(1f, 0.9f, 0.35f, 1f), remainRate * pulse);
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        {
            bool isSelected = _selectedRowIndex == index;
            if (GUILayout.Toggle(isSelected, string.Empty, GUILayout.Width(18)) != isSelected)
            {
                _selectedRowIndex = isSelected ? -1 : index;
            }

            EditorGUILayout.ObjectField(row.Prefab, typeof(GameObject), false, GUILayout.Width(130));

            EditorGUI.BeginChangeCheck();
            float newCarbs = EditorGUILayout.FloatField(row.Carbohydrates, GUILayout.Width(30));
            float newFat = EditorGUILayout.FloatField(row.Fat, GUILayout.Width(30));
            float newProtein = EditorGUILayout.FloatField(row.Protein, GUILayout.Width(30));
            float newWater = EditorGUILayout.FloatField(row.Water, GUILayout.Width(30));
            float newVitamins = EditorGUILayout.FloatField(row.Vitamins, GUILayout.Width(30));
            float newProgress = EditorGUILayout.FloatField(row.Max_EatingProgress, GUILayout.Width(40));
            float newConsumeSpeed = EditorGUILayout.FloatField(row.nutritionConsumeSpeed, GUILayout.Width(50));
            
            EditorGUILayout.LabelField(row.EnableSpoilage ? "启用" : "禁用", GUILayout.Width(40));
            if (!row.EnableSpoilage)
            {
                EditorGUILayout.LabelField("-", GUILayout.Width(40));
            }
            else
            {
                EditorGUILayout.LabelField($"{(int)row.SpoilageIntervalSeconds}s", GUILayout.Width(40));
            }

            if (EditorGUI.EndChangeCheck())
            {
                row.Carbohydrates = Mathf.Max(0f, newCarbs);
                row.Fat = Mathf.Max(0f, newFat);
                row.Protein = Mathf.Max(0f, newProtein);
                row.Water = Mathf.Max(0f, newWater);
                row.Vitamins = Mathf.Max(0f, newVitamins);
                row.Max_EatingProgress = Mathf.Max(1f, newProgress);
                row.nutritionConsumeSpeed = Mathf.Max(0f, newConsumeSpeed);
                _config.SaveNow();
            }

            string status = BuildStatusText(row);
            EditorGUILayout.LabelField(status, GUILayout.Width(100));

            if (GUILayout.Button("应用", GUILayout.Width(50)))
            {
                ApplyRowToPrefab(row);
            }

            if (GUILayout.Button("定位", GUILayout.Width(50)))
            {
                if (row.Prefab != null)
                {
                    Selection.activeObject = row.Prefab;
                    EditorGUIUtility.PingObject(row.Prefab);
                }
            }

            if (GUILayout.Button("Inspector", GUILayout.Width(70)))
            {
                _selectedRowIndex = index;
                FlashRow(index);
                OpenPrefabInspector(row);
            }
        }
        EditorGUILayout.EndHorizontal();
        GUI.color = oldColor;
    }

    private void DrawBottomBar()
    {
        EditorGUILayout.BeginHorizontal();
        {
            if (GUILayout.Button("应用选中行"))
            {
                ApplySelectedRow();
            }

            if (GUILayout.Button("从 Prefab 重新扫描"))
            {
                ScanAllPrefabs();
            }

            if (GUILayout.Button("清空表格"))
            {
                if (EditorUtility.DisplayDialog("清空表格", "确定要清空当前表格吗？", "确定", "取消"))
                {
                    _config.Rows.Clear();
                    _config.SaveNow();
                }
            }
        }
        EditorGUILayout.EndHorizontal();
    }
    #endregion

    #region 数据加载
    private void LoadOrCreateConfig()
    {
        _config = AssetDatabase.LoadAssetAtPath<FoodStatTableConfig>(ConfigPath);
        if (_config != null)
        {
            _config.Rows ??= new List<FoodStatTableRow>();
            return;
        }

        _config = CreateInstance<FoodStatTableConfig>();
        EnsureFolderExists(Path.GetDirectoryName(ConfigPath));
        AssetDatabase.CreateAsset(_config, ConfigPath);
        AssetDatabase.SaveAssets();
        _config.Rows ??= new List<FoodStatTableRow>();
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        string folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(parent))
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolderExists(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }
    #endregion

    #region 扫描与应用
    private void ScanAllPrefabs()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int total = prefabGuids.Length;

        try
        {
            _config.Rows.Clear();

            for (int i = 0; i < total; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                EditorUtility.DisplayProgressBar("扫描食物 Prefab", $"处理中 {path} ({i + 1}/{total})", total == 0 ? 1f : (float)i / total);

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                if (!HasFoodComponent(prefab))
                {
                    continue;
                }

                FoodStatTableRow row = BuildRowFromPrefab(prefab, path);
                if (string.IsNullOrEmpty(row.PrefabName))
                {
                    continue;
                }

                _config.Rows.Add(row);
            }

            _config.Rows = _config.Rows
                .OrderBy(row => row.PrefabName)
                .ThenBy(row => row.PrefabPath)
                .ToList();

            _selectedRowIndex = -1;
            _config.SaveNow();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static bool HasFoodComponent(GameObject prefab)
    {
        if (prefab.GetComponentInChildren<Mod_Food>(true) != null)
        {
            return true;
        }

        return false;
    }

    private static FoodStatTableRow BuildRowFromPrefab(GameObject prefab, string path)
    {
        FoodStatTableRow row = new FoodStatTableRow
        {
            Prefab = prefab,
            PrefabPath = path,
            PrefabName = prefab.name,
            Carbohydrates = 0f,
            Fat = 0f,
            Protein = 0f,
            Water = 0f,
            Vitamins = 0f,
            Max_Carbohydrates = 0f,
            Max_Fat = 0f,
            Max_Protein = 0f,
            Max_Water = 0f,
            Max_Vitamins = 0f,
            Max_EatingProgress = 3f,
            nutritionConsumeSpeed = 0f,
            WaterConsumeSpeedRate = 0f,
            nutritionConsumeRate = 0f,
            EnableSpoilage = true,
            SpoilageIntervalSeconds = 1800f,
            SpoilageTargetItemID = "Meat_Rotten",
            HasNutrition = false,
            HasSpoilage = false
        };

        Mod_Food[] foods = prefab.GetComponentsInChildren<Mod_Food>(true);
        if (foods != null && foods.Length > 0)
        {
            Mod_Food food = foods[0];
            if (food != null)
            {
                // 确保FoodModData被初始化
                food.FoodModData ??= new ModData_FoodData();
                
                var foodData = food.FoodModData.EnsureFoodData();
                if (foodData?.nutrition != null)
                {
                    row.Carbohydrates = foodData.nutrition.Carbohydrates;
                    row.Fat = foodData.nutrition.Fat;
                    row.Protein = foodData.nutrition.Protein;
                    row.Water = foodData.nutrition.Water;
                    row.Vitamins = foodData.nutrition.Vitamins;
                    row.Max_Carbohydrates = foodData.nutrition.Max_Carbohydrates;
                    row.Max_Fat = foodData.nutrition.Max_Fat;
                    row.Max_Protein = foodData.nutrition.Max_Protein;
                    row.Max_Water = foodData.nutrition.Max_Water;
                    row.Max_Vitamins = foodData.nutrition.Max_Vitamins;
                    row.HasNutrition = true;
                }

                row.Max_EatingProgress = foodData?.Max_EatingProgress ?? 3f;
                row.nutritionConsumeSpeed = foodData?.nutritionConsumeSpeed?.Value ?? 0f;
                row.WaterConsumeSpeedRate = foodData?.WaterConsumeSpeedRate ?? 0f;
                row.nutritionConsumeRate = foodData?.nutritionConsumeRate ?? 0f;

                row.EnableSpoilage = food.FoodModData.EnableSpoilage;
                row.SpoilageIntervalSeconds = food.FoodModData.SpoilageIntervalSeconds;
                row.SpoilageTargetItemID = food.FoodModData.SpoilageTargetItemID ?? "Meat_Rotten";
                row.HasSpoilage = food.FoodModData.EnableSpoilage;
            }
        }

        return row;
    }

    private void ApplyAllRowsToPrefabs()
    {
        for (int i = 0; i < _config.Rows.Count; i++)
        {
            ApplyRowToPrefab(_config.Rows[i]);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void ApplySelectedRow()
    {
        if (_selectedRowIndex < 0 || _selectedRowIndex >= _config.Rows.Count)
        {
            return;
        }

        ApplyRowToPrefab(_config.Rows[_selectedRowIndex]);
    }

    private static void ApplyRowToPrefab(FoodStatTableRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.PrefabPath))
        {
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(row.PrefabPath);
        try
        {
            bool changed = false;

            Mod_Food[] foods = root.GetComponentsInChildren<Mod_Food>(true);
            foreach (Mod_Food food in foods)
            {
                if (food == null)
                {
                    continue;
                }

                // 确保FoodModData被初始化
                food.FoodModData ??= new ModData_FoodData();

                var foodData = food.FoodModData.EnsureFoodData();
                if (foodData?.nutrition != null)
                {
                    foodData.nutrition.Carbohydrates = Mathf.Max(0f, row.Carbohydrates);
                    foodData.nutrition.Fat = Mathf.Max(0f, row.Fat);
                    foodData.nutrition.Protein = Mathf.Max(0f, row.Protein);
                    foodData.nutrition.Water = Mathf.Max(0f, row.Water);
                    foodData.nutrition.Vitamins = Mathf.Max(0f, row.Vitamins);
                    foodData.nutrition.Max_Carbohydrates = Mathf.Max(0f, row.Max_Carbohydrates);
                    foodData.nutrition.Max_Fat = Mathf.Max(0f, row.Max_Fat);
                    foodData.nutrition.Max_Protein = Mathf.Max(0f, row.Max_Protein);
                    foodData.nutrition.Max_Water = Mathf.Max(0f, row.Max_Water);
                    foodData.nutrition.Max_Vitamins = Mathf.Max(0f, row.Max_Vitamins);
                }

                foodData.Max_EatingProgress = Mathf.Max(1f, row.Max_EatingProgress);
                if (foodData.nutritionConsumeSpeed != null)
                {
                    foodData.nutritionConsumeSpeed.BaseValue = Mathf.Max(0f, row.nutritionConsumeSpeed);
                }
                else
                {
                    foodData.nutritionConsumeSpeed = new GameValue_float(Mathf.Max(0f, row.nutritionConsumeSpeed));
                }
                foodData.WaterConsumeSpeedRate = Mathf.Max(0f, row.WaterConsumeSpeedRate);
                foodData.nutritionConsumeRate = Mathf.Max(0f, row.nutritionConsumeRate);

                food.FoodModData.EnableSpoilage = row.EnableSpoilage;
                food.FoodModData.SpoilageIntervalSeconds = Mathf.Max(0f, row.SpoilageIntervalSeconds);
                if (!string.IsNullOrEmpty(row.SpoilageTargetItemID))
                {
                    food.FoodModData.SpoilageTargetItemID = row.SpoilageTargetItemID;
                }

                EditorUtility.SetDirty(food);
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, row.PrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
    #endregion

    #region 辅助
    private bool ShouldShowRow(FoodStatTableRow row)
    {
        if (row == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_searchText))
        {
            string keyword = _searchText.Trim();
            if (!row.PrefabName.Contains(keyword, System.StringComparison.OrdinalIgnoreCase) &&
                !(row.PrefabPath?.Contains(keyword, System.StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return false;
            }
        }

        if (_showOnlyNutritionRows && !row.HasNutrition)
        {
            return false;
        }

        if (_showOnlySpoilageRows && !row.HasSpoilage)
        {
            return false;
        }

        return true;
    }

    private static string BuildStatusText(FoodStatTableRow row)
    {
        List<string> statusParts = new List<string>();
        if (row.HasNutrition)
        {
            statusParts.Add("营养");
        }

        if (row.HasSpoilage)
        {
            statusParts.Add("腐败");
        }

        return statusParts.Count > 0 ? string.Join("/", statusParts) : "无数值";
    }

    private bool IsRowFlashHighlighted(int index)
    {
        return index == _highlightRowIndex && EditorApplication.timeSinceStartup < _highlightUntilTime;
    }

    private void FlashRow(int index)
    {
        _highlightRowIndex = index;
        _highlightUntilTime = EditorApplication.timeSinceStartup + HighlightDuration;
    }

    private static void OpenPrefabInspector(FoodStatTableRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.PrefabPath))
        {
            return;
        }

        PrefabStage stage = PrefabStageUtility.OpenPrefab(row.PrefabPath);
        if (stage == null || stage.prefabContentsRoot == null)
        {
            return;
        }

        Object target = stage.prefabContentsRoot;
        Mod_Food food = stage.prefabContentsRoot.GetComponentInChildren<Mod_Food>(true);
        if (food != null)
        {
            target = food;
        }

        Selection.activeObject = target;
        EditorGUIUtility.PingObject(target);
    }
    #endregion
}
