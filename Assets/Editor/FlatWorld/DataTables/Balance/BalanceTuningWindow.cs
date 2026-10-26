using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>统一的武器、防御、食物和任意 JSON 数值调节窗口；Play Mode 不需要退出即可反复体验。</summary>
internal sealed class BalanceTuningWindow : EditorWindow
{
    private const string StylePath = "Assets/Editor/FlatWorld/DataTables/Balance/BalanceTuning.uss";
    private const string AutoReloadPreference = "FlatWorld.BalanceTuning.AutoReload";

    private sealed class EntryGroup
    {
        public string Key;
        public string Name;
        public string Id;
        public string Source;
        public List<BalanceTuningRecord> Rows;
    }

    private BalanceTuningDatabase _database;
    private List<EntryGroup> _allGroups = new();
    private readonly List<EntryGroup> _visibleGroups = new();
    private ListView _groupList;
    private ScrollView _detailScroll;
    private Label _heading;
    private Label _subtitle;
    private Label _stats;
    private Label _status;
    private Label _runtimeStatus;
    private Button _saveSource;
    private TextField _search;
    private Toggle _autoReload;
    private string _category = "全部";
    private string _selectedGroupKey;
    private string _lastMessage = "点击左侧条目，使用 ↑ / ↓ 持续微调；数值不会收敛锁死。";
    private double _pendingReloadAt;
    private int _reloadRetries;
    private bool _wasPlaying;

    [MenuItem("FlatWorld/数值调节工作台 %#b")]
    public static void Open()
    {
        BalanceTuningWindow window = GetWindow<BalanceTuningWindow>("数值工作台");
        window.minSize = new Vector2(800f, 540f);
        window.Show();
    }

    private void OnEnable()
    {
        _database = BalanceTuningDatabase.LoadOrCreate();
        BalanceTuningSources.ImportLegacyIfNecessary(_database);
        if (!_database.CatalogScanned) BalanceTuningSources.ScanInto(_database);
        _wasPlaying = EditorApplication.isPlaying;
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    /// <summary>使用 UI Toolkit 创建可停靠、可搜索、宽屏/窄屏均可操作的自定义编辑器界面。</summary>
    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.Clear();
        StyleSheet stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
        if (stylesheet != null) root.styleSheets.Add(stylesheet);
        root.AddToClassList("balance-root");

        var header = new VisualElement();
        header.AddToClassList("topbar");
        var brand = new VisualElement();
        brand.AddToClassList("brand");
        brand.Add(MakeLabel("◈  FLATWORLD  /  DESIGN LAB", "eyebrow"));
        brand.Add(MakeLabel("数值调节工作台", "title"));
        brand.Add(MakeLabel("武器 · 护甲 · 食物 · 设备参数  /  一处调节，持续试验", "subtle"));
        header.Add(brand);
        var toolbar = new VisualElement();
        toolbar.AddToClassList("toolbar-actions");
        var scan = MakeButton("↻  重新扫描", "quiet-button", () =>
        {
            int count = BalanceTuningSources.ScanInto(_database);
            SetMessage($"已扫描 {count} 个参数；已有历史与手工调节值保持不变。");
            BuildGroups();
            RefreshList();
        });
        toolbar.Add(scan);
        _autoReload = new Toggle("改完自动同步游戏")
        {
            value = EditorPrefs.GetBool(AutoReloadPreference, true)
        };
        _autoReload.AddToClassList("auto-toggle");
        _autoReload.RegisterValueChangedCallback(e =>
            EditorPrefs.SetBool(AutoReloadPreference, e.newValue));
        toolbar.Add(_autoReload);
        header.Add(toolbar);
        root.Add(header);

        var body = new VisualElement();
        body.AddToClassList("workspace");
        root.Add(body);

        var sidebar = new VisualElement();
        sidebar.AddToClassList("sidebar");
        sidebar.Add(MakeLabel("参数目录", "section-title"));
        sidebar.Add(MakeLabel("⌕  搜索物品 / ID / 参数", "search-label"));
        _search = new TextField { value = string.Empty };
        _search.AddToClassList("search-input");
        _search.RegisterValueChangedCallback(_ => RefreshList());
        _search.tooltip = "搜索物品名称、ID、参数名或配置路径";
        sidebar.Add(_search);

        var categoryStrip = new VisualElement();
        categoryStrip.AddToClassList("category-strip");
        foreach (string name in new[] { "全部", "武器", "护甲", "食物", "战斗", "通用" })
        {
            string selected = name;
            Button button = MakeButton(name, "tab-button", () =>
            {
                _category = selected;
                foreach (VisualElement child in categoryStrip.Children())
                    child.EnableInClassList("tab-active", child.name == _category);
                RefreshList();
            });
            button.name = name;
            button.EnableInClassList("tab-active", name == _category);
            categoryStrip.Add(button);
        }
        sidebar.Add(categoryStrip);

        _stats = MakeLabel("", "directory-count");
        sidebar.Add(_stats);
        _groupList = new ListView
        {
            selectionType = SelectionType.Single,
            fixedItemHeight = 64,
            makeItem = MakeGroupListItem,
            bindItem = BindGroupListItem
        };
        _groupList.AddToClassList("group-list");
        _groupList.selectionChanged += selected =>
        {
            EntryGroup group = selected.OfType<EntryGroup>().FirstOrDefault();
            if (group == null) return;
            _selectedGroupKey = group.Key;
            ShowDetails(group);
        };
        sidebar.Add(_groupList);
        body.Add(sidebar);

        var content = new VisualElement();
        content.AddToClassList("content");
        var intro = new VisualElement();
        intro.AddToClassList("content-heading");
        var introText = new VisualElement();
        introText.AddToClassList("content-heading-text");
        _heading = MakeLabel("选择一个物品", "detail-title");
        _subtitle = MakeLabel("记录每一次调参，让下一次修改有迹可循。", "subtle");
        introText.Add(_heading);
        introText.Add(_subtitle);
        intro.Add(introText);
        _saveSource = MakeButton("✓  保存到源文件", "primary-button", SaveSelectedSource);
        intro.Add(_saveSource);
        content.Add(intro);

        var runtimeBar = new VisualElement();
        runtimeBar.AddToClassList("runtime-bar");
        _runtimeStatus = MakeLabel("", "runtime-label");
        runtimeBar.Add(_runtimeStatus);
        content.Add(runtimeBar);

        _detailScroll = new ScrollView(ScrollViewMode.Vertical);
        _detailScroll.AddToClassList("details-scroll");
        content.Add(_detailScroll);

        _status = MakeLabel("", "status-footer");
        content.Add(_status);
        body.Add(content);

        BuildGroups();
        RefreshList();
        UpdateSessionLabel();
        SetMessage(_lastMessage);
    }

    #region 目录

    private void BuildGroups()
    {
        _allGroups = _database.Records
            .GroupBy(x => x.GroupKey)
            .Select(group => new EntryGroup
            {
                Key = group.Key,
                Name = group.First().Name,
                Id = group.First().DefinitionId,
                Source = group.First().SourcePath,
                Rows = group.OrderBy(x => x.Category).ThenBy(x => x.Label).ToList()
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToList();
    }

    private void RefreshList()
    {
        if (_groupList == null) return;
        string search = _search?.value?.Trim() ?? string.Empty;
        _visibleGroups.Clear();
        foreach (EntryGroup group in _allGroups)
        {
            if (_category != "全部" && !group.Rows.Any(x => x.Category == _category)) continue;
            if (!string.IsNullOrEmpty(search) &&
                !(group.Name?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) &&
                !(group.Id?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) &&
                !(group.Source?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) &&
                !group.Rows.Any(x => x.Label?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                continue;
            _visibleGroups.Add(group);
        }
        _stats.text = $"{_visibleGroups.Count} 个条目   ·   {_visibleGroups.Sum(x => x.Rows.Count)} 个可调字段";
        _groupList.itemsSource = _visibleGroups;
        _groupList.Rebuild();
        EntryGroup active = _visibleGroups.FirstOrDefault(x => x.Key == _selectedGroupKey) ??
                            _visibleGroups.FirstOrDefault();
        if (active != null)
        {
            _selectedGroupKey = active.Key;
            _groupList.SetSelectionWithoutNotify(new[] { _visibleGroups.IndexOf(active) });
            ShowDetails(active);
        }
        else
        {
            _selectedGroupKey = null;
            _heading.text = "暂无匹配数值";
            _subtitle.text = "尝试调整筛选条件，或重新扫描配置资源。";
            _detailScroll.Clear();
            _saveSource.SetEnabled(false);
        }
    }

    private static VisualElement MakeGroupListItem()
    {
        var cell = new VisualElement();
        cell.AddToClassList("directory-row");
        cell.Add(MakeLabel("", "directory-name"));
        cell.Add(MakeLabel("", "directory-id"));
        cell.Add(MakeLabel("", "directory-meta"));
        return cell;
    }

    private void BindGroupListItem(VisualElement element, int index)
    {
        EntryGroup group = _visibleGroups[index];
        element.Q<Label>(className: "directory-name").text = group.Name;
        element.Q<Label>(className: "directory-id").text =
            string.IsNullOrEmpty(group.Id) ? "PREFAB  /  " + System.IO.Path.GetFileNameWithoutExtension(group.Source) : group.Id;
        element.Q<Label>(className: "directory-meta").text =
            $"{group.Rows.Count} 个参数  ·  {(group.Rows[0].SourceKind == BalanceTuningSourceKind.Prefab ? "预制体" : "JSON")}";
    }

    #endregion

    #region 明细编辑

    private void ShowDetails(EntryGroup group)
    {
        if (_detailScroll == null) return;
        _heading.text = group.Name;
        _subtitle.text = $"{(string.IsNullOrEmpty(group.Id) ? "Prefab 模板" : group.Id)}    •    {group.Source}";
        _saveSource.SetEnabled(group.Rows[0].SourceKind == BalanceTuningSourceKind.Prefab &&
                               !EditorApplication.isPlaying);
        _saveSource.text = group.Rows[0].SourceKind == BalanceTuningSourceKind.JsonDefinition
            ? "✓  JSON 自动写回" : "✓  保存到 Prefab";
        _detailScroll.Clear();

        var quickActions = new VisualElement();
        quickActions.AddToClassList("quick-actions");
        quickActions.Add(MakeButton("◉  定位源资源", "quiet-button", () =>
        {
            UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(group.Source);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }));
        quickActions.Add(MakeLabel($"本条目有 {group.Rows.Sum(x => x.History?.Count ?? 0)} 次调整记录", "muted-chip"));
        _detailScroll.Add(quickActions);

        IEnumerable<BalanceTuningRecord> shownRows = _category == "全部"
            ? group.Rows : group.Rows.Where(x => x.Category == _category);
        foreach (IGrouping<string, BalanceTuningRecord> category in shownRows.GroupBy(x => x.Category))
        {
            _detailScroll.Add(MakeLabel(category.Key + "参数", "group-heading"));
            foreach (BalanceTuningRecord row in category)
                _detailScroll.Add(MakeTuningCard(row));
        }
    }

    private VisualElement MakeTuningCard(BalanceTuningRecord row)
    {
        var card = new VisualElement();
        card.AddToClassList("tuning-card");
        var top = new VisualElement();
        top.AddToClassList("card-header");
        var identity = new VisualElement();
        identity.AddToClassList("card-identity");
        identity.Add(MakeLabel(row.Label, "card-title"));
        identity.Add(MakeLabel(row.FieldPath, "field-path"));
        top.Add(identity);
        top.Add(MakeLabel(row.History?.Count > 0 ? $"{row.History.Count} 次调整" : "待测试", "count-chip"));
        card.Add(top);

        if (row.ValueKind == BalanceTuningValueKind.Toggle)
        {
            var toggle = new Toggle("启用") { value = row.ToggleValue };
            toggle.RegisterValueChangedCallback(e =>
            {
                row.ToggleValue = e.newValue;
                ApplyRecord(row);
            });
            toggle.AddToClassList("typed-editor");
            card.Add(toggle);
        }
        else if (row.ValueKind == BalanceTuningValueKind.Text)
        {
            var input = new TextField("配置值") { value = row.TextValue ?? "", isDelayed = true };
            input.RegisterValueChangedCallback(e =>
            {
                row.TextValue = e.newValue;
                ApplyRecord(row);
            });
            input.AddToClassList("typed-editor");
            card.Add(input);
        }
        else
        {
            var controls = new VisualElement();
            controls.AddToClassList("tuning-controls");
            Button lower = MakeButton("−  偏高，降低", "down-button", () => Vote(row, -1));
            controls.Add(lower);
            var valueBox = new VisualElement();
            valueBox.AddToClassList("value-box");
            valueBox.Add(MakeLabel("当前数值", "value-caption"));
            var valueField = new FloatField { value = row.Value, isDelayed = true };
            valueField.AddToClassList("primary-value");
            valueField.RegisterValueChangedCallback(e =>
            {
                if (row.SetValue(e.newValue)) ApplyRecord(row);
            });
            valueBox.Add(valueField);
            controls.Add(valueBox);
            controls.Add(MakeButton("＋  偏低，提高", "up-button", () => Vote(row, 1)));
            card.Add(controls);

            var settings = new VisualElement();
            settings.AddToClassList("settings-row");
            var step = new FloatField("步长") { value = row.Step, isDelayed = true };
            step.AddToClassList("small-field");
            step.RegisterValueChangedCallback(e =>
            {
                row.Step = Mathf.Clamp(e.newValue, 0.0001f, 1000000f);
                _database.Save();
            });
            settings.Add(step);
            settings.Add(MakeLabel("同方向渐进加速  •  反方向减半", "algorithm-hint"));
            settings.Add(MakeButton("↶ 撤销", "quiet-button", () =>
            {
                if (!row.Undo()) return;
                row.IsPendingPrefabSave = row.SourceKind == BalanceTuningSourceKind.Prefab;
                ApplyRecord(row);
            }));
            card.Add(settings);

            var trend = new VisualElement();
            trend.AddToClassList("trend-chart");
            trend.generateVisualContent += context => DrawTrend(context, trend, row);
            card.Add(trend);

            var details = new Foldout { text = "查看历史与数值边界", value = false };
            details.AddToClassList("history-foldout");
            var bounds = new VisualElement();
            bounds.AddToClassList("bounds-row");
            var minimum = new FloatField("下限") { value = row.Min, isDelayed = true };
            var maximum = new FloatField("上限") { value = row.Max, isDelayed = true };
            minimum.RegisterValueChangedCallback(e =>
            {
                row.Min = Mathf.Min(row.Max, e.newValue);
                _database.Save();
            });
            maximum.RegisterValueChangedCallback(e =>
            {
                row.Max = Mathf.Max(row.Min, e.newValue);
                _database.Save();
            });
            bounds.Add(minimum);
            bounds.Add(maximum);
            details.Add(bounds);
            if (row.History != null)
            {
                foreach (BalanceTuningHistory history in row.History.AsEnumerable().Reverse().Take(12))
                    details.Add(MakeLabel(
                        $"{history.Timestamp}   {history.Before:0.###} → {history.After:0.###}   {history.Feedback}",
                        "history-entry"));
            }
            card.Add(details);
        }
        return card;
    }

    /// <summary>极简折线直接绘于 Editor VisualElement，不生成额外纹理或逐帧重绘。</summary>
    private static void DrawTrend(MeshGenerationContext context, VisualElement element, BalanceTuningRecord row)
    {
        float width = element.contentRect.width;
        float height = element.contentRect.height;
        if (width <= 8f || height <= 8f) return;
        List<float> series = row.History?.Select(h => h.Before).ToList() ?? new List<float>();
        series.Add(row.Value);
        if (series.Count > 20) series = series.Skip(series.Count - 20).ToList();
        if (series.Count < 2) return;
        float min = series.Min(), max = series.Max();
        float range = Mathf.Max(0.01f, max - min);
        Painter2D painter = context.painter2D;
        painter.strokeColor = new Color(0.33f, 0.69f, 0.98f, 0.9f);
        painter.lineWidth = 2f;
        painter.BeginPath();
        for (int i = 0; i < series.Count; i++)
        {
            float x = 5f + i * (width - 10f) / (series.Count - 1);
            float y = height - 7f - (series[i] - min) / range * (height - 14f);
            if (i == 0) painter.MoveTo(new Vector2(x, y));
            else painter.LineTo(new Vector2(x, y));
        }
        painter.Stroke();
    }

    private void Vote(BalanceTuningRecord row, int direction)
    {
        if (row.Vote(direction)) ApplyRecord(row);
        else SetMessage("已达到数值边界，试试修改下限/上限。");
    }

    /// <summary>每次点击统一保存历史、实时预览，JSON 同步权威配置，必要时安排原位重载。</summary>
    private void ApplyRecord(BalanceTuningRecord row)
    {
        _database.Save();
        int live = BalanceTuningLivePreview.ApplyNow(row);
        bool connectedOnline = EditorApplication.isPlaying && GameNetwork.IsOnline;
        if (row.SourceKind == BalanceTuningSourceKind.JsonDefinition)
        {
            if (!BalanceTuningSources.SaveJsonField(row, out string error))
            {
                SetMessage("写回 JSON 失败：" + error);
                return;
            }
            if (EditorApplication.isPlaying && _autoReload.value && !connectedOnline)
            {
                _pendingReloadAt = EditorApplication.timeSinceStartup + 1.25d;
                _reloadRetries = 0;
            }
            SetMessage(connectedOnline
                ? "已保存 JSON；联机期间不会单边修改运行实例，请结束联机会话后测试。"
                : $"已保存 JSON，当前原位更新 {live} 个对象" +
                  (EditorApplication.isPlaying && _autoReload.value
                      ? "；将尝试资源热重载，同步后续生成的物品。" : "。"));
        }
        else
        {
            SetMessage(connectedOnline
                ? "数值已暂存；联机期间禁止单边热调运行实例。"
                : $"数值已暂存，当前原位更新 {live} 个对象；退出 Play Mode 后可以写回 Prefab。");
        }
        EntryGroup group = _allGroups.FirstOrDefault(x => x.Key == _selectedGroupKey);
        if (group != null) ShowDetails(group);
    }

    /// <summary>编辑态显式写回 Prefab，播放中不触发 Prefab 导入及脚本域刷新。</summary>
    private void SaveSelectedSource()
    {
        if (EditorApplication.isPlaying)
        {
            SetMessage("当前在 Play Mode：为避免导入影响试玩，请退出后再保存 Prefab；调整历史已保留。");
            return;
        }
        EntryGroup group = _allGroups.FirstOrDefault(x => x.Key == _selectedGroupKey);
        if (group == null || group.Rows[0].SourceKind != BalanceTuningSourceKind.Prefab) return;
        if (!BalanceTuningSources.SavePrefabFields(group.Rows, out string error))
        {
            SetMessage("Prefab 保存失败：" + error);
            return;
        }
        _database.Save();
        SetMessage($"已保存 {group.Name} 的 {group.Rows.Count} 个配置字段到 Prefab。");
    }

    #endregion

    #region 状态

    private void OnEditorUpdate()
    {
        if (_wasPlaying != EditorApplication.isPlaying)
        {
            _wasPlaying = EditorApplication.isPlaying;
            UpdateSessionLabel();
            if (_selectedGroupKey != null)
            {
                EntryGroup group = _allGroups.FirstOrDefault(x => x.Key == _selectedGroupKey);
                if (group != null) ShowDetails(group);
            }
        }
        if (_pendingReloadAt <= 0 || EditorApplication.timeSinceStartup < _pendingReloadAt)
            return;
        _pendingReloadAt = 0;
        if (!EditorApplication.isPlaying) return;
        GameRes res = GameRes.ExistingInstance;
        if (res != null && res.RequestResourceReload())
        {
            SetMessage("资源原位刷新已开始：无需离开游戏，后续生成的物品也将使用新配置。");
        }
        else if (_reloadRetries++ < 5)
        {
            _pendingReloadAt = EditorApplication.timeSinceStartup + 2d;
        }
        else SetMessage("自动刷新暂不可用（例如正在联机或资源加载）。当前已加载对象仍可使用直接预览。");
    }

    private void UpdateSessionLabel()
    {
        if (_runtimeStatus == null) return;
        _runtimeStatus.text = EditorApplication.isPlaying
            ? "●  PLAY MODE   ·   更改数值时直接更新已加载实例，JSON 安全重载后同步新生成实例"
            : "●  EDIT MODE   ·   历史持续保存，Prefab 变更需手动写回";
        _runtimeStatus.EnableInClassList("runtime-playing", EditorApplication.isPlaying);
    }

    private void SetMessage(string message)
    {
        _lastMessage = message;
        if (_status != null) _status.text = message;
    }

    private static Label MakeLabel(string text, string style)
    {
        var label = new Label(text);
        if (!string.IsNullOrEmpty(style)) label.AddToClassList(style);
        return label;
    }

    private static Button MakeButton(string text, string style, Action action)
    {
        var button = new Button(action) { text = text };
        if (!string.IsNullOrEmpty(style)) button.AddToClassList(style);
        return button;
    }

    #endregion
}
