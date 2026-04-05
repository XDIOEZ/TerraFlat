using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Prefab 数值总表窗口
/// </summary>
public class PrefabStatTableWindow : EditorWindow
{
    #region 常量
    private const string ConfigPath = "Assets/Editor/FlatWorld/PrefabStatTableConfig.asset";
    #endregion

    #region 字段
    private PrefabStatTableConfig _config; // 表格配置
    private Vector2 _scrollPosition; // 滚动位置
    private string _searchText = string.Empty; // 搜索文本
    private bool _showOnlyHpRows; // 仅显示有血量的行
    private bool _showOnlyDefenseRows; // 仅显示有防御的行
    private bool _showOnlyDamageRows; // 仅显示有伤害的行
    private int _selectedRowIndex = -1; // 选中行索引
    private int _highlightRowIndex = -1; // 高亮行索引
    private double _highlightUntilTime; // 高亮结束时间
    private const double HighlightDuration = 1.2d; // 高亮持续时间
    #endregion

    #region 菜单与初始化
    [MenuItem("FlatWorld/Prefab数值表")]
    public static void OpenWindow()
    {
        GetWindow<PrefabStatTableWindow>("Prefab数值表");
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
            EditorGUILayout.HelpBox("未能加载 Prefab 数值表配置", MessageType.Error);
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
            _showOnlyHpRows = EditorGUILayout.ToggleLeft("仅血量", _showOnlyHpRows, GUILayout.Width(70));
            _showOnlyDefenseRows = EditorGUILayout.ToggleLeft("仅防御", _showOnlyDefenseRows, GUILayout.Width(70));
            _showOnlyDamageRows = EditorGUILayout.ToggleLeft("仅伤害", _showOnlyDamageRows, GUILayout.Width(70));
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTable()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Prefab", GUILayout.Width(180));
                EditorGUILayout.LabelField("路径", GUILayout.Width(220));
                EditorGUILayout.LabelField("MaxHp", GUILayout.Width(70));
                EditorGUILayout.LabelField("Hp", GUILayout.Width(70));
                EditorGUILayout.LabelField("Defense", GUILayout.Width(70));
                EditorGUILayout.LabelField("Damage", GUILayout.Width(70));
                EditorGUILayout.LabelField("状态", GUILayout.Width(120));
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            {
                for (int i = 0; i < _config.Rows.Count; i++)
                {
                    PrefabStatTableRow row = _config.Rows[i];
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

    private void DrawRow(int index, PrefabStatTableRow row)
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

            EditorGUILayout.ObjectField(row.Prefab, typeof(GameObject), false, GUILayout.Width(160));
            EditorGUILayout.SelectableLabel(row.PrefabPath ?? string.Empty, GUILayout.Width(220), GUILayout.Height(EditorGUIUtility.singleLineHeight));

            EditorGUI.BeginChangeCheck();
            float newHp = EditorGUILayout.FloatField(row.MaxHp, GUILayout.Width(60));
            float newCurrentHp = EditorGUILayout.FloatField(row.Hp, GUILayout.Width(60));
            float newDefense = EditorGUILayout.FloatField(row.Defense, GUILayout.Width(60));
            float newDamage = EditorGUILayout.FloatField(row.Damage, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
            {
                row.MaxHp = Mathf.Max(0f, newHp);
                row.Hp = Mathf.Clamp(newCurrentHp, 0f, row.MaxHp);
                row.Defense = Mathf.Max(0f, newDefense);
                row.Damage = Mathf.Max(0f, newDamage);
                _config.SaveNow();
            }

            string status = BuildStatusText(row);
            EditorGUILayout.LabelField(status, GUILayout.Width(120));

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
        _config = AssetDatabase.LoadAssetAtPath<PrefabStatTableConfig>(ConfigPath);
        if (_config != null)
        {
            _config.Rows ??= new List<PrefabStatTableRow>();
            return;
        }

        _config = CreateInstance<PrefabStatTableConfig>();
        EnsureFolderExists(Path.GetDirectoryName(ConfigPath));
        AssetDatabase.CreateAsset(_config, ConfigPath);
        AssetDatabase.SaveAssets();
        _config.Rows ??= new List<PrefabStatTableRow>();
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
                EditorUtility.DisplayProgressBar("扫描 Prefab 数值", $"处理中 {path} ({i + 1}/{total})", total == 0 ? 1f : (float)i / total);

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                if (!HasTargetDamageComponents(prefab))
                {
                    continue;
                }

                PrefabStatTableRow row = BuildRowFromPrefab(prefab, path);
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

    private static bool HasTargetDamageComponents(GameObject prefab)
    {
        if (prefab.GetComponentInChildren<DamageReceiver>(true) != null)
        {
            return true;
        }

        // 兼容当前项目主用的发送模块
        if (prefab.GetComponentInChildren<Mod_Damage>(true) != null)
        {
            return true;
        }

        // 兼容历史 DamageSender（若类型存在）
        System.Type senderType = System.Type.GetType("DamageSender");
        if (senderType == null)
        {
            senderType = System.AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch
                    {
                        return System.Array.Empty<System.Type>();
                    }
                })
                .FirstOrDefault(type => type != null && type.Name == "DamageSender");
        }

        if (senderType != null && prefab.GetComponentInChildren(senderType, true) != null)
        {
            return true;
        }

        return false;
    }

    private static PrefabStatTableRow BuildRowFromPrefab(GameObject prefab, string path)
    {
        PrefabStatTableRow row = new PrefabStatTableRow
        {
            Prefab = prefab,
            PrefabPath = path,
            PrefabName = prefab.name,
            MaxHp = 0f,
            Hp = 0f,
            Defense = 0f,
            Damage = 0f,
            HasHp = false,
            HasDefense = false,
            HasDamage = false
        };

        DamageReceiver[] receivers = prefab.GetComponentsInChildren<DamageReceiver>(true);
        if (receivers != null && receivers.Length > 0)
        {
            row.HasHp = true;
            row.HasDefense = true;
            row.MaxHp = receivers[0].Data != null ? receivers[0].Data.MaxHp : 0f;
            row.Hp = receivers[0].Data != null ? receivers[0].Data.Hp : 0f;
            row.Defense = receivers[0].Data != null ? receivers[0].Data.Defense : 0f;
        }

        Mod_Damage[] damages = prefab.GetComponentsInChildren<Mod_Damage>(true);
        if (damages != null && damages.Length > 0)
        {
            row.HasDamage = true;
            row.Damage = damages[0].Damage != null ? damages[0].Damage.BaseValue : 0f;
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

    private static void ApplyRowToPrefab(PrefabStatTableRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.PrefabPath))
        {
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(row.PrefabPath);
        try
        {
            bool changed = false;

            DamageReceiver[] receivers = root.GetComponentsInChildren<DamageReceiver>(true);
            foreach (DamageReceiver receiver in receivers)
            {
                if (receiver == null || receiver.Data == null)
                {
                    continue;
                }

                receiver.Data.MaxHp = Mathf.Max(0f, row.MaxHp);
                receiver.Data.Hp = Mathf.Clamp(row.Hp, 0f, receiver.Data.MaxHp);
                receiver.Data.Defense = Mathf.Max(0f, row.Defense);
                EditorUtility.SetDirty(receiver);
                changed = true;
            }

            Mod_Damage[] damages = root.GetComponentsInChildren<Mod_Damage>(true);
            foreach (Mod_Damage damage in damages)
            {
                if (damage == null || damage.Damage == null)
                {
                    continue;
                }

                damage.Damage.BaseValue = Mathf.Max(0f, row.Damage);
                EditorUtility.SetDirty(damage);
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
    private bool ShouldShowRow(PrefabStatTableRow row)
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

        if (_showOnlyHpRows && !row.HasHp)
        {
            return false;
        }

        if (_showOnlyDefenseRows && !row.HasDefense)
        {
            return false;
        }

        if (_showOnlyDamageRows && !row.HasDamage)
        {
            return false;
        }

        return true;
    }

    private static string BuildStatusText(PrefabStatTableRow row)
    {
        List<string> statusParts = new List<string>();
        if (row.HasHp)
        {
            statusParts.Add("血量");
        }

        if (row.HasDamage)
        {
            statusParts.Add("伤害");
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

    private static void OpenPrefabInspector(PrefabStatTableRow row)
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
        DamageReceiver receiver = stage.prefabContentsRoot.GetComponentInChildren<DamageReceiver>(true);
        if (receiver != null)
        {
            target = receiver;
        }
        else
        {
            Mod_Damage damage = stage.prefabContentsRoot.GetComponentInChildren<Mod_Damage>(true);
            if (damage != null)
            {
                target = damage;
            }
        }

        Selection.activeObject = target;
        EditorGUIUtility.PingObject(target);
    }
    #endregion
}