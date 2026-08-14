using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class StructureEditorWindow : EditorWindow
{
    private enum CanvasTool
    {
        Select,
        ItemBrush,
        TileBrush,
        MarkerBrush,
        Eraser,
        Pan
    }

    private const string DefinitionFolder = "Assets/4_ScriptObjects/World/Structures/Definitions";
    private const string TemplateFolder = "Assets/4_ScriptObjects/World/Structures/Templates";
    private const string AuthoringFolder = "Assets/Editor/FlatWorld/Structures/AuthoringAssets";

    private string newStructureId = "new_structure";
    private string newDisplayName = "新遗迹";
    private Vector2 scroll;
    private List<StructureValidationIssue> issues = new();
    [SerializeField] private StructureDefinitionSO selectedDefinition;
    [SerializeField] private CanvasTool canvasTool;
    [SerializeField] private GameObject itemBrushPrefab;
    [SerializeField] private Tile_Block tileBrush;
    [SerializeField] private StructureMarkerType markerBrushType = StructureMarkerType.Entrance;
    [SerializeField] private string markerContentId = string.Empty;
    [SerializeField] private float markerChance = 1f;
    [SerializeField] private float canvasZoom = 42f;
    [SerializeField] private Vector2 canvasPan = new(40f, 40f);
    [SerializeField] private UnityEngine.Object canvasSelection;
    [SerializeField] private string paletteSearch = string.Empty;
    [SerializeField] private Vector2 selectionScroll;
    private Vector2 paletteScroll;
    private List<GameObject> itemPalette = new();
    private Dictionary<string, GameObject> itemPrefabLookup = new(StringComparer.Ordinal);
    private List<Tile_Block> tilePalette = new();
    private bool draggingSelection;
    private Vector2Int lastPaintedCell = new(int.MinValue, int.MinValue);
    private int framedRootId;
    private Vector2 framedCanvasSize;

    [MenuItem("FlatWorld/遗迹编辑器/打开编辑窗口", priority = 0)]
    public static void Open()
    {
        GetWindow<StructureEditorWindow>("遗迹编辑器");
    }

    [UnityEditor.Callbacks.OnOpenAsset(0)]
    private static bool OpenStructureDefinitionAsset(int instanceId, int line)
    {
        if (EditorUtility.InstanceIDToObject(instanceId) is not StructureDefinitionSO definition)
            return false;

        StructureEditorWindow window = GetWindow<StructureEditorWindow>("遗迹编辑器");
        window.selectedDefinition = definition;
        window.framedRootId = 0;
        window.Show();
        window.Focus();
        EditorApplication.delayCall += () =>
        {
            if (window == null)
                return;

            window.OpenSelectedDefinition();
            window.Repaint();
        };
        return true;
    }

    private void OnEnable()
    {
        minSize = new Vector2(900f, 600f);
        SceneView.duringSceneGui += DrawSceneOverlay;
        if (Selection.activeObject is StructureDefinitionSO definition)
            selectedDefinition = definition;
        if (selectedDefinition == null)
            selectedDefinition = StructureCatalogSO.LoadDefault()?.Definitions?
                .FirstOrDefault(item => item != null);
        RefreshPalettes();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawSceneOverlay;
    }

    private void OnSelectionChange()
    {
        if (Selection.activeObject is StructureDefinitionSO definition)
            selectedDefinition = definition;
        else if (Selection.activeObject is Tile_Block block)
        {
            tileBrush = block;
            canvasTool = CanvasTool.TileBrush;
        }
        else if (Selection.activeObject is GameObject prefab &&
                 EditorUtility.IsPersistent(prefab) &&
                 !string.IsNullOrWhiteSpace(StructureAuthoringPrefabUtility.GetItemId(prefab)))
        {
            itemBrushPrefab = prefab;
            canvasTool = CanvasTool.ItemBrush;
        }
        Repaint();
    }

    private void OnGUI()
    {
        StructureAuthoringRoot root = GetCurrentRoot();
        DrawTopBar(root);

        if (root == null)
        {
            DrawLandingPage();
            return;
        }

        selectedDefinition = root.Definition;
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawToolPanel(root);
            Rect canvasRect = GUILayoutUtility.GetRect(
                320f,
                10000f,
                300f,
                10000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            DrawCanvas(root, canvasRect);
            DrawSelectionPanel(root);
        }

        if (AssetPreview.IsLoadingAssetPreviews())
            Repaint();
    }

    private void DrawTopBar(StructureAuthoringRoot root)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("遗迹画布", EditorStyles.boldLabel, GUILayout.Width(70f));
            selectedDefinition = (StructureDefinitionSO)EditorGUILayout.ObjectField(
                selectedDefinition,
                typeof(StructureDefinitionSO),
                false,
                GUILayout.MinWidth(160f));
            if (GUILayout.Button("打开/创建画布", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                OpenSelectedDefinition();
            if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                CreateNewStructure();
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(root == null))
            {
                if (GUILayout.Button("验证", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                    issues = StructureTemplateValidator.Validate(root);
                if (GUILayout.Button("烘焙保存", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    if (StructureTemplateBaker.Bake(root, out issues))
                        SaveCurrentPrefabStage();
                }
            }
        }
    }

    private void DrawLandingPage()
    {
        GUILayout.Space(24f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MaxWidth(560f)))
        {
            EditorGUILayout.LabelField("打开现有遗迹", EditorStyles.boldLabel);
            selectedDefinition = (StructureDefinitionSO)EditorGUILayout.ObjectField(
                "遗迹 Definition",
                selectedDefinition,
                typeof(StructureDefinitionSO),
                false);
            EditorGUILayout.HelpBox(
                "选择 abandoned_camp 后点击“打开/创建画布”。首次打开会从已烘焙模板还原成可视化 Authoring Prefab。",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(selectedDefinition == null))
            {
                if (GUILayout.Button("打开/创建俯视画布", GUILayout.Height(30f)))
                    OpenSelectedDefinition();
            }
        }

        GUILayout.Space(12f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MaxWidth(560f)))
        {
            EditorGUILayout.LabelField("新建遗迹", EditorStyles.boldLabel);
            newStructureId = EditorGUILayout.TextField("遗迹ID", newStructureId);
            newDisplayName = EditorGUILayout.TextField("显示名称", newDisplayName);
            if (GUILayout.Button("新建空白画布"))
                CreateNewStructure();
        }
    }

    private void DrawToolPanel(StructureAuthoringRoot root)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(230f)))
        {
            EditorGUILayout.LabelField("工具", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                ToolButton(CanvasTool.Select, "选择 V");
                ToolButton(CanvasTool.Pan, "平移 H");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                ToolButton(CanvasTool.ItemBrush, "物件 B");
                ToolButton(CanvasTool.TileBrush, "地块 T");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                ToolButton(CanvasTool.MarkerBrush, "Marker M");
                ToolButton(CanvasTool.Eraser, "橡皮 E");
            }

            EditorGUILayout.Space();
            if (canvasTool == CanvasTool.ItemBrush)
            {
                EditorGUILayout.LabelField("物件画笔", EditorStyles.boldLabel);
                itemBrushPrefab = (GameObject)EditorGUILayout.ObjectField(
                    itemBrushPrefab,
                    typeof(GameObject),
                    false);
                string itemId = StructureAuthoringPrefabUtility.GetItemId(itemBrushPrefab);
                EditorGUILayout.LabelField(
                    string.IsNullOrEmpty(itemId) ? "拖入带Item组件的Prefab" : $"ID: {itemId}",
                    EditorStyles.miniLabel);
                DrawItemPalette();
            }
            else if (canvasTool == CanvasTool.TileBrush)
            {
                EditorGUILayout.LabelField("地块画笔", EditorStyles.boldLabel);
                tileBrush = (Tile_Block)EditorGUILayout.ObjectField(
                    tileBrush,
                    typeof(Tile_Block),
                    false);
                DrawTilePalette();
            }
            else if (canvasTool == CanvasTool.MarkerBrush)
            {
                EditorGUILayout.LabelField("Marker画笔", EditorStyles.boldLabel);
                markerBrushType =
                    (StructureMarkerType)EditorGUILayout.EnumPopup(markerBrushType);
                markerContentId = EditorGUILayout.TextField("内容ID", markerContentId);
                markerChance = EditorGUILayout.Slider("概率", markerChance, 0f, 1f);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("画布", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Vector2Int size = EditorGUILayout.Vector2IntField("尺寸", root.Size);
            Vector2 pivot = EditorGUILayout.Vector2Field("Pivot", root.Pivot);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(root, "修改遗迹画布");
                root.Size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
                root.Pivot = pivot;
                MarkAuthoringDirty(root);
                framedRootId = 0;
            }

            if (GUILayout.Button("居中显示"))
                framedRootId = 0;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "滚轮缩放｜中键拖动画布｜左键绘制/选择｜Delete删除选中",
                MessageType.None);

            DrawCompactIssues();
        }
    }

    private void ToolButton(CanvasTool tool, string label)
    {
        bool active = canvasTool == tool;
        if (GUILayout.Toggle(active, label, "Button") && !active)
            canvasTool = tool;
    }

    private void DrawItemPalette()
    {
        DrawPaletteHeader();
        List<GameObject> visible = itemPalette
            .Where(prefab =>
            {
                string id = StructureAuthoringPrefabUtility.GetItemId(prefab);
                return string.IsNullOrWhiteSpace(paletteSearch) ||
                       id.IndexOf(paletteSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       prefab.name.IndexOf(paletteSearch, StringComparison.OrdinalIgnoreCase) >= 0;
            })
            .Take(120)
            .ToList();

        paletteScroll = EditorGUILayout.BeginScrollView(
            paletteScroll,
            EditorStyles.helpBox,
            GUILayout.Height(230f));
        DrawGameObjectPaletteGrid(visible);
        EditorGUILayout.EndScrollView();
    }

    private void DrawTilePalette()
    {
        DrawPaletteHeader();
        List<Tile_Block> visible = tilePalette
            .Where(block =>
                string.IsNullOrWhiteSpace(paletteSearch) ||
                block.name.IndexOf(paletteSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (!string.IsNullOrWhiteSpace(block.displayName) &&
                 block.displayName.IndexOf(
                     paletteSearch,
                     StringComparison.OrdinalIgnoreCase) >= 0))
            .Take(120)
            .ToList();

        paletteScroll = EditorGUILayout.BeginScrollView(
            paletteScroll,
            EditorStyles.helpBox,
            GUILayout.Height(230f));
        DrawTilePaletteGrid(visible);
        EditorGUILayout.EndScrollView();
    }

    private void DrawPaletteHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            paletteSearch = EditorGUILayout.TextField(
                paletteSearch,
                EditorStyles.toolbarSearchField);
            if (GUILayout.Button("↻", EditorStyles.miniButton, GUILayout.Width(26f)))
                RefreshPalettes();
        }
    }

    private void DrawGameObjectPaletteGrid(IReadOnlyList<GameObject> assets)
    {
        const int columns = 3;
        for (int i = 0; i < assets.Count; i += columns)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int column = 0; column < columns; column++)
                {
                    int index = i + column;
                    if (index >= assets.Count)
                    {
                        GUILayout.Space(64f);
                        continue;
                    }

                    GameObject prefab = assets[index];
                    Texture preview = AssetPreview.GetAssetPreview(prefab) ??
                                      AssetPreview.GetMiniThumbnail(prefab);
                    string id = StructureAuthoringPrefabUtility.GetItemId(prefab);
                    GUIContent content = new(
                        preview,
                        $"{id}\n{AssetDatabase.GetAssetPath(prefab)}");
                    bool active = itemBrushPrefab == prefab;
                    Color oldColor = GUI.backgroundColor;
                    if (active)
                        GUI.backgroundColor = new Color(0.35f, 0.85f, 1f);
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(64f)))
                    {
                        if (GUILayout.Button(content, GUILayout.Width(58f), GUILayout.Height(48f)))
                            itemBrushPrefab = prefab;
                        GUILayout.Label(
                            id,
                            EditorStyles.centeredGreyMiniLabel,
                            GUILayout.Width(58f));
                    }
                    GUI.backgroundColor = oldColor;
                }
            }
        }
    }

    private void DrawTilePaletteGrid(IReadOnlyList<Tile_Block> assets)
    {
        const int columns = 3;
        for (int i = 0; i < assets.Count; i += columns)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int column = 0; column < columns; column++)
                {
                    int index = i + column;
                    if (index >= assets.Count)
                    {
                        GUILayout.Space(64f);
                        continue;
                    }

                    Tile_Block block = assets[index];
                    TileBase tile = block.GetTileBaseAsset();
                    Texture preview = AssetPreview.GetAssetPreview(tile) ??
                                      AssetPreview.GetMiniThumbnail(tile);
                    string label = string.IsNullOrWhiteSpace(block.displayName)
                        ? block.name
                        : block.displayName;
                    GUIContent content = new(
                        preview,
                        $"{label}\n{AssetDatabase.GetAssetPath(block)}");
                    bool active = tileBrush == block;
                    Color oldColor = GUI.backgroundColor;
                    if (active)
                        GUI.backgroundColor = new Color(0.35f, 0.85f, 1f);
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(64f)))
                    {
                        if (GUILayout.Button(content, GUILayout.Width(58f), GUILayout.Height(48f)))
                            tileBrush = block;
                        GUILayout.Label(
                            label,
                            EditorStyles.centeredGreyMiniLabel,
                            GUILayout.Width(58f));
                    }
                    GUI.backgroundColor = oldColor;
                }
            }
        }
    }

    private void RefreshPalettes()
    {
        itemPalette = AssetDatabase.FindAssets("t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(prefab =>
                prefab != null &&
                !string.IsNullOrWhiteSpace(
                    StructureAuthoringPrefabUtility.GetItemId(prefab)))
            .OrderBy(
                StructureAuthoringPrefabUtility.GetItemId,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        itemPrefabLookup = itemPalette
            .GroupBy(StructureAuthoringPrefabUtility.GetItemId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        tilePalette = AssetDatabase.FindAssets("t:Tile_Block")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Tile_Block>)
            .Where(block => block != null && block.GetTileBaseAsset() != null)
            .OrderBy(block => block.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void OpenSelectedDefinition()
    {
        if (selectedDefinition == null)
        {
            ShowNotification(new GUIContent("请先选择遗迹Definition"));
            return;
        }

        try
        {
            StructureAuthoringPrefabUtility.OpenOrCreate(selectedDefinition);
            framedRootId = 0;
            EditorApplication.delayCall += Repaint;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("打开遗迹失败", exception.Message, "确定");
        }
    }

    private static void SaveCurrentPrefabStage()
    {
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage?.prefabContentsRoot == null || string.IsNullOrEmpty(stage.assetPath))
            return;

        PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
        AssetDatabase.SaveAssets();
    }

    private void DrawCanvas(StructureAuthoringRoot root, Rect rect)
    {
        if (rect.width < 20f || rect.height < 20f)
            return;

        GUI.BeginGroup(rect);
        Rect localRect = new(0f, 0f, rect.width, rect.height);
        EditorGUI.DrawRect(localRect, new Color(0.075f, 0.085f, 0.10f));

        int rootId = root.GetInstanceID();
        bool rootChanged = framedRootId != rootId;
        bool canvasSizeChanged =
            !Mathf.Approximately(framedCanvasSize.x, localRect.width) ||
            !Mathf.Approximately(framedCanvasSize.y, localRect.height);
        if (rootChanged)
        {
            FrameCanvas(root, localRect);
            framedRootId = rootId;
            framedCanvasSize = localRect.size;
        }
        else if (canvasSizeChanged)
        {
            // Keep the user's zoom and pan when the editor window is resized.
            // Re-fitting here would overwrite mouse-wheel zoom on the next GUI pass.
            if (framedCanvasSize.x > 0f && framedCanvasSize.y > 0f)
                canvasPan += (localRect.size - framedCanvasSize) * 0.5f;
            framedCanvasSize = localRect.size;
        }

        DrawTiles(root);
        DrawGrid(root);
        DrawPlacedItems(root);
        DrawMarkers(root);
        DrawPivot(root);
        DrawCanvasBadge(localRect);
        HandleCanvasInput(root, localRect);
        GUI.EndGroup();
    }

    private void FrameCanvas(StructureAuthoringRoot root, Rect rect)
    {
        float widthZoom = (rect.width - 70f) / Mathf.Max(1, root.Size.x);
        float heightZoom = (rect.height - 70f) / Mathf.Max(1, root.Size.y);
        canvasZoom = Mathf.Clamp(Mathf.Min(widthZoom, heightZoom), 12f, 96f);
        canvasPan = new Vector2(
            (rect.width - root.Size.x * canvasZoom) * 0.5f,
            (rect.height - root.Size.y * canvasZoom) * 0.5f);
    }

    private void DrawTiles(StructureAuthoringRoot root)
    {
        if (root.Tilemap == null)
            return;

        for (int x = 0; x < root.Size.x; x++)
        {
            for (int y = 0; y < root.Size.y; y++)
            {
                TileBase tile = root.Tilemap.GetTile(new Vector3Int(x, y, 0));
                if (tile == null)
                    continue;

                Rect cellRect = GetCellRect(root, x, y);
                EditorGUI.DrawRect(cellRect, new Color(0.22f, 0.28f, 0.24f));
                Texture preview = AssetPreview.GetAssetPreview(tile) ??
                                  AssetPreview.GetMiniThumbnail(tile);
                if (preview != null)
                    GUI.DrawTexture(cellRect, preview, ScaleMode.ScaleToFit, true);
            }
        }
    }

    private void DrawGrid(StructureAuthoringRoot root)
    {
        float gridWidth = root.Size.x * canvasZoom;
        float gridHeight = root.Size.y * canvasZoom;
        for (int x = 0; x <= root.Size.x; x++)
        {
            float screenX = canvasPan.x + x * canvasZoom;
            Color color = x == 0 || x == root.Size.x
                ? new Color(0.2f, 0.85f, 1f, 0.95f)
                : new Color(1f, 1f, 1f, 0.13f);
            EditorGUI.DrawRect(
                new Rect(Mathf.Round(screenX), canvasPan.y, 1f, gridHeight),
                color);
        }

        for (int y = 0; y <= root.Size.y; y++)
        {
            float screenY = canvasPan.y + y * canvasZoom;
            Color color = y == 0 || y == root.Size.y
                ? new Color(0.2f, 0.85f, 1f, 0.95f)
                : new Color(1f, 1f, 1f, 0.13f);
            EditorGUI.DrawRect(
                new Rect(canvasPan.x, Mathf.Round(screenY), gridWidth, 1f),
                color);
        }
    }

    private void DrawPlacedItems(StructureAuthoringRoot root)
    {
        List<GameObject> objects = GetPlacedItemRoots(root);
        for (int i = 0; i < objects.Count; i++)
        {
            GameObject placed = objects[i];
            Vector2 local = root.transform.InverseTransformPoint(placed.transform.position);
            Vector2 center = LocalToCanvas(root, local);
            float size = Mathf.Clamp(canvasZoom * 0.82f, 18f, 82f);
            Rect itemRect = new(center.x - size * 0.5f, center.y - size * 0.5f, size, size);

            StructureItemAuthoring metadata =
                placed.GetComponentInChildren<StructureItemAuthoring>(true);
            GameObject source = metadata?.SourcePrefab ??
                                PrefabUtility.GetCorrespondingObjectFromOriginalSource(placed);
            Texture preview = source != null
                ? AssetPreview.GetAssetPreview(source) ?? AssetPreview.GetMiniThumbnail(source)
                : null;

            EditorGUI.DrawRect(
                itemRect,
                canvasSelection == placed
                    ? new Color(1f, 0.72f, 0.15f, 0.95f)
                    : new Color(0.18f, 0.34f, 0.48f, 0.9f));
            Rect inner = new(
                itemRect.x + 2f,
                itemRect.y + 2f,
                itemRect.width - 4f,
                itemRect.height - 4f);
            if (preview != null)
                GUI.DrawTexture(inner, preview, ScaleMode.ScaleToFit, true);

            if (canvasZoom >= 28f)
            {
                string itemId = metadata?.ItemPrefabId;
                if (string.IsNullOrEmpty(itemId))
                {
                    Item item = placed.GetComponentInChildren<Item>(true);
                    itemId = item?.itemData?.IDName ?? placed.name;
                }
                GUI.Label(
                    new Rect(itemRect.x - 30f, itemRect.yMax, itemRect.width + 60f, 18f),
                    itemId,
                    EditorStyles.centeredGreyMiniLabel);
            }
        }
    }

    private void DrawMarkers(StructureAuthoringRoot root)
    {
        StructureMarkerAuthoring[] markers =
            root.GetComponentsInChildren<StructureMarkerAuthoring>(true);
        for (int i = 0; i < markers.Length; i++)
        {
            StructureMarkerAuthoring marker = markers[i];
            Vector2 local = root.transform.InverseTransformPoint(marker.transform.position);
            Vector2 center = LocalToCanvas(root, local);
            Rect markerRect = new(center.x - 9f, center.y - 9f, 18f, 18f);
            EditorGUI.DrawRect(
                markerRect,
                canvasSelection == marker.gameObject
                    ? Color.white
                    : GetMarkerColor(marker.Type));
            GUI.Label(markerRect, GetMarkerLetter(marker.Type), EditorStyles.whiteBoldLabel);
            if (canvasZoom >= 28f)
            {
                GUI.Label(
                    new Rect(markerRect.x - 40f, markerRect.yMax, 100f, 18f),
                    marker.MarkerId,
                    EditorStyles.centeredGreyMiniLabel);
            }
        }
    }

    private void DrawPivot(StructureAuthoringRoot root)
    {
        Vector2 center = LocalToCanvas(root, root.Pivot);
        Color color = Color.yellow;
        const float radius = 7f;
        const float diameter = radius * 2f;
        Rect outline = new(
            Mathf.Round(center.x - radius),
            Mathf.Round(center.y - radius),
            diameter,
            diameter);
        EditorGUI.DrawRect(new Rect(outline.x, outline.y, outline.width, 1f), color);
        EditorGUI.DrawRect(
            new Rect(outline.x, outline.yMax - 1f, outline.width, 1f),
            color);
        EditorGUI.DrawRect(new Rect(outline.x, outline.y, 1f, outline.height), color);
        EditorGUI.DrawRect(
            new Rect(outline.xMax - 1f, outline.y, 1f, outline.height),
            color);
        EditorGUI.DrawRect(
            new Rect(Mathf.Round(center.x - 10f), Mathf.Round(center.y), 20f, 1f),
            color);
        EditorGUI.DrawRect(
            new Rect(Mathf.Round(center.x), Mathf.Round(center.y - 10f), 1f, 20f),
            color);
    }

    private void DrawCanvasBadge(Rect rect)
    {
        string text = $"{canvasTool}  |  {Mathf.RoundToInt(canvasZoom)} px/cell";
        Rect badge = new(rect.x + 8f, rect.y + 8f, 180f, 22f);
        GUI.Box(badge, text, EditorStyles.helpBox);
    }

    private void HandleCanvasInput(StructureAuthoringRoot root, Rect rect)
    {
        Event current = Event.current;
        bool mouseInside = rect.Contains(current.mousePosition);

        if (mouseInside && current.type == EventType.KeyDown)
        {
            if (TrySwitchToolByKey(current.keyCode))
            {
                current.Use();
                Repaint();
                return;
            }

            if ((current.keyCode == KeyCode.Delete ||
                 current.keyCode == KeyCode.Backspace) &&
                DeleteCanvasSelection(root))
            {
                current.Use();
                return;
            }
        }

        if (!mouseInside)
            return;

        if (current.type == EventType.ScrollWheel)
        {
            Vector2 localBeforeZoom = CanvasToLocal(root, current.mousePosition);
            float multiplier = Mathf.Pow(1.12f, -current.delta.y);
            canvasZoom = Mathf.Clamp(canvasZoom * multiplier, 10f, 120f);
            Vector2 afterZoom = LocalToCanvas(root, localBeforeZoom);
            canvasPan += current.mousePosition - afterZoom;
            current.Use();
            Repaint();
            return;
        }

        if (current.type == EventType.MouseDrag &&
            (current.button == 2 ||
             (current.button == 0 && canvasTool == CanvasTool.Pan)))
        {
            canvasPan += current.delta;
            current.Use();
            Repaint();
            return;
        }

        if (current.type == EventType.MouseDown && current.button == 0)
        {
            lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
            switch (canvasTool)
            {
                case CanvasTool.Select:
                    canvasSelection = FindCanvasObjectAt(root, current.mousePosition);
                    Selection.activeObject = canvasSelection;
                    draggingSelection = canvasSelection is GameObject;
                    if (draggingSelection)
                        Undo.RecordObject(((GameObject)canvasSelection).transform, "移动遗迹元素");
                    break;
                case CanvasTool.ItemBrush:
                    PlaceItem(root, current.mousePosition);
                    break;
                case CanvasTool.TileBrush:
                    PaintTile(root, current.mousePosition, erase: false);
                    break;
                case CanvasTool.MarkerBrush:
                    PlaceMarker(root, current.mousePosition);
                    break;
                case CanvasTool.Eraser:
                    EraseAt(root, current.mousePosition);
                    break;
            }
            current.Use();
            Repaint();
            return;
        }

        if (current.type == EventType.MouseDrag && current.button == 0)
        {
            if (canvasTool == CanvasTool.Select && draggingSelection)
                MoveCanvasSelection(root, current.mousePosition);
            else if (canvasTool == CanvasTool.TileBrush)
                PaintTile(root, current.mousePosition, erase: false);
            else if (canvasTool == CanvasTool.Eraser)
                EraseAt(root, current.mousePosition);
            current.Use();
            Repaint();
            return;
        }

        if (current.type == EventType.MouseUp && current.button == 0)
        {
            draggingSelection = false;
            lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
        }
    }

    private bool TrySwitchToolByKey(KeyCode key)
    {
        CanvasTool next = key switch
        {
            KeyCode.V => CanvasTool.Select,
            KeyCode.B => CanvasTool.ItemBrush,
            KeyCode.T => CanvasTool.TileBrush,
            KeyCode.M => CanvasTool.MarkerBrush,
            KeyCode.E => CanvasTool.Eraser,
            KeyCode.H => CanvasTool.Pan,
            _ => canvasTool
        };
        if (next == canvasTool)
            return false;
        canvasTool = next;
        return true;
    }

    private void PlaceItem(StructureAuthoringRoot root, Vector2 mousePosition)
    {
        string itemId = StructureAuthoringPrefabUtility.GetItemId(itemBrushPrefab);
        if (itemBrushPrefab == null || string.IsNullOrWhiteSpace(itemId))
        {
            ShowNotification(new GUIContent("请先拖入带Item组件的Prefab"));
            return;
        }

        Vector2 local = ClampAndSnap(root, CanvasToLocal(root, mousePosition));
        GameObject instance =
            PrefabUtility.InstantiatePrefab(itemBrushPrefab, root.transform) as GameObject;
        if (instance == null)
        {
            ShowNotification(new GUIContent("物件Prefab实例化失败"));
            return;
        }

        Undo.RegisterCreatedObjectUndo(instance, "绘制遗迹物件");
        instance.transform.localPosition = new Vector3(local.x, local.y, 0f);
        // ItemMgr treats Vector3.one as the normal runtime size. Prefab root
        // scales are import details and must not leak into structure stamps.
        instance.transform.localScale = Vector3.one;
        Item item = instance.GetComponent<Item>() ?? instance.GetComponentInChildren<Item>(true);
        GameObject metadataOwner = item != null ? item.gameObject : instance;
        StructureItemAuthoring metadata =
            metadataOwner.GetComponent<StructureItemAuthoring>() ??
            Undo.AddComponent<StructureItemAuthoring>(metadataOwner);
        metadata.ItemPrefabId = itemId;
        metadata.MemberId = CreateUniqueMemberId(root, itemId, metadata);
        metadata.SourcePrefab = itemBrushPrefab;
        metadata.OrientationMode = StructureOrientationMode.KeepWorldOrientation;
        metadata.Optional = false;
        metadata.SpawnChance = 1f;
        metadata.ContainerContents = new StructureContainerContents();
        EditorUtility.SetDirty(metadata);
        canvasSelection = instance;
        Selection.activeGameObject = instance;
        MarkAuthoringDirty(root);
    }

    private void PaintTile(
        StructureAuthoringRoot root,
        Vector2 mousePosition,
        bool erase)
    {
        if (root.Tilemap == null)
            return;
        if (!erase && (tileBrush == null || tileBrush.GetTileBaseAsset() == null))
        {
            ShowNotification(new GUIContent("请先选择有效Tile_Block"));
            return;
        }

        Vector2 local = CanvasToLocal(root, mousePosition);
        Vector2Int cell = new(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y));
        if (!IsCellInside(root, cell) || cell == lastPaintedCell)
            return;

        lastPaintedCell = cell;
        Undo.RecordObject(root.Tilemap, erase ? "擦除遗迹地块" : "绘制遗迹地块");
        root.Tilemap.SetTile(
            new Vector3Int(cell.x, cell.y, 0),
            erase ? null : tileBrush.GetTileBaseAsset());
        EditorUtility.SetDirty(root.Tilemap);
        MarkAuthoringDirty(root);
    }

    private void PlaceMarker(StructureAuthoringRoot root, Vector2 mousePosition)
    {
        Vector2 local = ClampAndSnap(root, CanvasToLocal(root, mousePosition));
        GameObject markerObject = new($"{markerBrushType}Marker");
        Undo.RegisterCreatedObjectUndo(markerObject, "绘制遗迹Marker");
        Undo.SetTransformParent(markerObject.transform, root.transform, "设置Marker父级");
        markerObject.transform.localPosition = new Vector3(local.x, local.y, 0f);
        StructureMarkerAuthoring marker =
            Undo.AddComponent<StructureMarkerAuthoring>(markerObject);
        marker.Type = markerBrushType;
        marker.MarkerId =
            $"{markerBrushType.ToString().ToLowerInvariant()}_{root.GetComponentsInChildren<StructureMarkerAuthoring>(true).Length}";
        marker.ContentId = markerContentId;
        marker.Chance = markerChance;
        canvasSelection = markerObject;
        Selection.activeGameObject = markerObject;
        MarkAuthoringDirty(root);
    }

    private void EraseAt(StructureAuthoringRoot root, Vector2 mousePosition)
    {
        UnityEngine.Object hit = FindCanvasObjectAt(root, mousePosition);
        if (hit is GameObject gameObject)
        {
            if (canvasSelection == gameObject)
                canvasSelection = null;
            Undo.DestroyObjectImmediate(gameObject);
            MarkAuthoringDirty(root);
            return;
        }

        PaintTile(root, mousePosition, erase: true);
    }

    private void MoveCanvasSelection(
        StructureAuthoringRoot root,
        Vector2 mousePosition)
    {
        if (canvasSelection is not GameObject gameObject)
            return;

        Vector2 local = ClampAndSnap(root, CanvasToLocal(root, mousePosition));
        gameObject.transform.localPosition = new Vector3(local.x, local.y, 0f);
        EditorUtility.SetDirty(gameObject.transform);
        MarkAuthoringDirty(root);
    }

    private bool DeleteCanvasSelection(StructureAuthoringRoot root)
    {
        if (canvasSelection is not GameObject gameObject ||
            gameObject == root.gameObject ||
            gameObject == root.Tilemap?.gameObject)
        {
            return false;
        }

        Undo.DestroyObjectImmediate(gameObject);
        canvasSelection = null;
        MarkAuthoringDirty(root);
        Repaint();
        return true;
    }

    private UnityEngine.Object FindCanvasObjectAt(
        StructureAuthoringRoot root,
        Vector2 mousePosition)
    {
        const float radius = 18f;
        StructureMarkerAuthoring[] markers =
            root.GetComponentsInChildren<StructureMarkerAuthoring>(true);
        for (int i = markers.Length - 1; i >= 0; i--)
        {
            Vector2 local = root.transform.InverseTransformPoint(markers[i].transform.position);
            if (Vector2.Distance(LocalToCanvas(root, local), mousePosition) <= radius)
                return markers[i].gameObject;
        }

        List<GameObject> items = GetPlacedItemRoots(root);
        for (int i = items.Count - 1; i >= 0; i--)
        {
            Vector2 local = root.transform.InverseTransformPoint(items[i].transform.position);
            if (Vector2.Distance(LocalToCanvas(root, local), mousePosition) <= radius)
                return items[i];
        }

        return null;
    }

    private static List<GameObject> GetPlacedItemRoots(StructureAuthoringRoot root)
    {
        HashSet<GameObject> output = new();
        Item[] items = root.GetComponentsInChildren<Item>(true);
        for (int i = 0; i < items.Length; i++)
        {
            if (!StructureTemplateValidator.IsTopLevelPlacedItem(root, items[i]))
                continue;
            output.Add(GetDirectChildRoot(root, items[i].transform).gameObject);
        }

        StructureItemAuthoring[] metadata =
            root.GetComponentsInChildren<StructureItemAuthoring>(true);
        for (int i = 0; i < metadata.Length; i++)
            output.Add(GetDirectChildRoot(root, metadata[i].transform).gameObject);

        return output.Where(item => item != null).ToList();
    }

    private static Transform GetDirectChildRoot(
        StructureAuthoringRoot root,
        Transform transform)
    {
        Transform current = transform;
        while (current.parent != null && current.parent != root.transform)
            current = current.parent;
        return current;
    }

    private void DrawSelectionPanel(StructureAuthoringRoot root)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(350f)))
        {
            EditorGUILayout.LabelField("属性", EditorStyles.boldLabel);
            EditorGUILayout.ObjectField("Definition", root.Definition, typeof(StructureDefinitionSO), false);
            EditorGUILayout.ObjectField("Template", root.Template, typeof(StructureTemplateSO), false);

            GameObject selectedObject = canvasSelection as GameObject;
            selectionScroll = EditorGUILayout.BeginScrollView(selectionScroll);
            if (selectedObject == null)
            {
                EditorGUILayout.HelpBox("选择工具点击画布元素后，可在这里修改坐标、旋转及生成配置。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(selectedObject.name, EditorStyles.boldLabel);
                Transform selectedTransform = selectedObject.transform;
                Vector3 oldPosition = selectedTransform.localPosition;
                float oldRotation = selectedTransform.localEulerAngles.z;
                Vector3 oldScale = selectedTransform.localScale;
                Vector2 newPosition = EditorGUILayout.Vector2Field(
                    "坐标",
                    new Vector2(oldPosition.x, oldPosition.y));
                float newRotation = EditorGUILayout.FloatField("旋转Z", oldRotation);
                Vector3 newScale = EditorGUILayout.Vector3Field("缩放", oldScale);
                if (newPosition != new Vector2(oldPosition.x, oldPosition.y) ||
                    !Mathf.Approximately(newRotation, oldRotation) ||
                    newScale != oldScale)
                {
                    Undo.RecordObject(selectedTransform, "修改遗迹元素");
                    selectedTransform.localPosition =
                        new Vector3(newPosition.x, newPosition.y, oldPosition.z);
                    selectedTransform.localRotation = Quaternion.Euler(0f, 0f, newRotation);
                    selectedTransform.localScale = newScale;
                    MarkAuthoringDirty(root);
                }

                StructureItemAuthoring itemMetadata =
                    selectedObject.GetComponentInChildren<StructureItemAuthoring>(true);
                if (itemMetadata != null)
                    DrawItemMetadata(itemMetadata, root);

                StructureMarkerAuthoring marker =
                    selectedObject.GetComponent<StructureMarkerAuthoring>();
                if (marker != null)
                    DrawMarkerMetadata(marker, root);
            }
            EditorGUILayout.EndScrollView();

            if (selectedObject != null && GUILayout.Button("删除选中", GUILayout.Height(28f)))
                DeleteCanvasSelection(root);
        }
    }

    private void DrawItemMetadata(
        StructureItemAuthoring metadata,
        StructureAuthoringRoot root)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("生成配置", EditorStyles.boldLabel);
        string itemId = EditorGUILayout.TextField("Item ID", metadata.ItemPrefabId);
        GameObject source = (GameObject)EditorGUILayout.ObjectField(
            "源Prefab",
            metadata.SourcePrefab,
            typeof(GameObject),
            false);
        if (source != metadata.SourcePrefab)
        {
            string sourceItemId = StructureAuthoringPrefabUtility.GetItemId(source);
            if (!string.IsNullOrWhiteSpace(sourceItemId))
                itemId = sourceItemId;
        }

        string memberId;
        using (new EditorGUILayout.HorizontalScope())
        {
            memberId = EditorGUILayout.TextField("成员 ID", metadata.MemberId);
            if (GUILayout.Button(
                    string.IsNullOrWhiteSpace(metadata.MemberId) ? "生成" : "更新",
                    EditorStyles.miniButton,
                    GUILayout.Width(44f)))
            {
                memberId = CreateUniqueMemberId(root, itemId, metadata);
            }
        }
        StructureOrientationMode orientationMode =
            (StructureOrientationMode)EditorGUILayout.EnumPopup(
                "朝向模式",
                metadata.OrientationMode);
        bool optional = EditorGUILayout.Toggle("可选生成", metadata.Optional);
        float chance = EditorGUILayout.Slider("生成概率", metadata.SpawnChance, 0f, 1f);
        int seedSalt = EditorGUILayout.IntField("Seed Salt", metadata.SeedSalt);
        bool generationChanged =
            itemId != metadata.ItemPrefabId ||
            memberId != metadata.MemberId ||
            source != metadata.SourcePrefab ||
            orientationMode != metadata.OrientationMode ||
            optional != metadata.Optional ||
            !Mathf.Approximately(chance, metadata.SpawnChance) ||
            seedSalt != metadata.SeedSalt;
        if (generationChanged)
        {
            Undo.RecordObject(metadata, "修改遗迹物件配置");
            metadata.ItemPrefabId = itemId;
            metadata.MemberId = memberId;
            metadata.SourcePrefab = source;
            metadata.OrientationMode = orientationMode;
            metadata.Optional = optional;
            metadata.SpawnChance = chance;
            metadata.SeedSalt = seedSalt;
            EditorUtility.SetDirty(metadata);
            MarkAuthoringDirty(root);
        }

        DrawContainerContents(
            metadata,
            root,
            source != null ? source : ResolveItemPrefab(itemId));
    }

    #region 容器内容可视化

    /// <summary>绘制与目标Prefab真实库存槽位一致的容器内容配置。</summary>
    private void DrawContainerContents(
        StructureItemAuthoring metadata,
        StructureAuthoringRoot root,
        GameObject sourcePrefab)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("容器内容", EditorStyles.boldLabel);

        Mod_Inventory inventoryModule = sourcePrefab?.GetComponentInChildren<Mod_Inventory>(true);
        if (inventoryModule == null)
        {
            EditorGUILayout.HelpBox("当前物件不是容器；只有带 Mod_Inventory 的Prefab可配置槽位。", MessageType.None);
            return;
        }

        List<int> inventoryIndices = GetValidInventoryIndices(inventoryModule);
        if (inventoryIndices.Count == 0)
        {
            EditorGUILayout.HelpBox("容器Prefab没有可用的 InventoryInstances 或槽位数据。", MessageType.Error);
            return;
        }

        StructureContainerContents contents = metadata.ContainerContents ?? new StructureContainerContents();
        bool overrideContents = EditorGUILayout.Toggle("覆盖容器内容", contents.OverrideContents);
        if (overrideContents != contents.OverrideContents)
        {
            Undo.RecordObject(metadata, "切换遗迹容器内容");
            contents = EnsureContainerContents(metadata);
            contents.OverrideContents = overrideContents;
            if (overrideContents)
            {
                if (string.IsNullOrWhiteSpace(metadata.MemberId))
                    metadata.MemberId = CreateUniqueMemberId(root, metadata.ItemPrefabId, metadata);
                EnsureValidInventoryTarget(contents, inventoryModule, inventoryIndices);
            }
            EditorUtility.SetDirty(metadata);
            MarkAuthoringDirty(root);
        }

        if (!overrideContents)
        {
            EditorGUILayout.HelpBox("关闭时沿用容器Prefab自己的默认内容；开启后此处配置会完整覆盖目标库存。", MessageType.Info);
            return;
        }

        contents = EnsureContainerContents(metadata);
        int selectedOption = ResolveInventoryOption(contents, inventoryModule, inventoryIndices);
        string[] labels = inventoryIndices
            .Select(index => BuildInventoryLabel(inventoryModule.InventoryInstances[index], index))
            .ToArray();
        int nextOption = EditorGUILayout.Popup("目标库存", selectedOption, labels);
        if (nextOption != selectedOption)
        {
            Undo.RecordObject(metadata, "切换遗迹容器库存");
            int inventoryIndex = inventoryIndices[nextOption];
            Inventory inventory = inventoryModule.InventoryInstances[inventoryIndex];
            contents.TargetInventoryIndex = inventoryIndex;
            contents.TargetInventoryName = inventory?.Data?.Name;
            int slotCount = inventory?.Data?.itemSlots?.Count ?? 0;
            contents.Items ??= new List<StructureContainerItemEntry>();
            contents.Items.RemoveAll(entry =>
                entry == null || entry.SlotIndex < 0 || entry.SlotIndex >= slotCount);
            EditorUtility.SetDirty(metadata);
            MarkAuthoringDirty(root);
            selectedOption = nextOption;
        }

        int targetIndex = inventoryIndices[selectedOption];
        Inventory targetInventory = inventoryModule.InventoryInstances[targetIndex];
        int targetSlotCount = targetInventory?.Data?.itemSlots?.Count ?? 0;
        contents.Items ??= new List<StructureContainerItemEntry>();
        int configuredCount = contents.Items.Count(entry =>
            entry != null && entry.SlotIndex >= 0 && entry.SlotIndex < targetSlotCount);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label($"{targetSlotCount} 个槽位 / 已配置 {configuredCount}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(contents.Items.Count == 0))
            {
                if (GUILayout.Button("清空全部", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                {
                    Undo.RecordObject(metadata, "清空遗迹容器内容");
                    contents.Items.Clear();
                    EditorUtility.SetDirty(metadata);
                    MarkAuthoringDirty(root);
                }
            }
        }

        int invalidCount = contents.Items.Count(entry =>
            entry == null || entry.SlotIndex < 0 || entry.SlotIndex >= targetSlotCount);
        if (invalidCount > 0)
        {
            EditorGUILayout.HelpBox($"存在 {invalidCount} 条越界槽位配置，请切换库存或清理。", MessageType.Warning);
            if (GUILayout.Button("移除越界配置"))
            {
                Undo.RecordObject(metadata, "清理遗迹容器槽位");
                contents.Items.RemoveAll(entry =>
                    entry == null || entry.SlotIndex < 0 || entry.SlotIndex >= targetSlotCount);
                EditorUtility.SetDirty(metadata);
                MarkAuthoringDirty(root);
            }
        }

        for (int slotIndex = 0; slotIndex < targetSlotCount; slotIndex += 2)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawContainerSlot(metadata, root, targetInventory, slotIndex);
                if (slotIndex + 1 < targetSlotCount)
                    DrawContainerSlot(metadata, root, targetInventory, slotIndex + 1);
                else
                    GUILayout.Space(154f);
            }
        }
    }

    /// <summary>绘制单个可拖拽物品Prefab的库存槽卡片。</summary>
    private void DrawContainerSlot(
        StructureItemAuthoring metadata,
        StructureAuthoringRoot root,
        Inventory targetInventory,
        int slotIndex)
    {
        StructureContainerContents contents = metadata.ContainerContents;
        StructureContainerItemEntry currentEntry = contents.Items?
            .FirstOrDefault(entry => entry != null && entry.SlotIndex == slotIndex);
        GameObject currentPrefab = ResolveItemPrefab(currentEntry?.ItemPrefabId);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(154f)))
        {
            EditorGUILayout.LabelField($"槽位 {slotIndex + 1}", EditorStyles.boldLabel);
            GameObject selectedPrefab = (GameObject)EditorGUILayout.ObjectField(
                currentPrefab,
                typeof(GameObject),
                false,
                GUILayout.Height(42f));
            string selectedItemId = StructureAuthoringPrefabUtility.GetItemId(selectedPrefab);
            bool invalidSelection = selectedPrefab != null && string.IsNullOrWhiteSpace(selectedItemId);

            int amount = Mathf.Max(1, currentEntry?.Amount ?? 1);
            GameObject amountPrefab = selectedPrefab != null ? selectedPrefab : currentPrefab;
            int maxAmount = ResolveSlotMaxAmount(targetInventory, slotIndex, amountPrefab);
            using (new EditorGUI.DisabledScope(amountPrefab == null))
                amount = EditorGUILayout.IntSlider("数量", Mathf.Clamp(amount, 1, maxAmount), 1, maxAmount);

            string shownId = !string.IsNullOrWhiteSpace(selectedItemId)
                ? selectedItemId
                : currentEntry?.ItemPrefabId;
            EditorGUILayout.LabelField(
                string.IsNullOrWhiteSpace(shownId) ? "空" : shownId,
                EditorStyles.centeredGreyMiniLabel,
                GUILayout.Height(16f));

            bool clearRequested = currentEntry != null &&
                                  GUILayout.Button("清空槽位", EditorStyles.miniButton);
            bool prefabChanged = selectedPrefab != currentPrefab;
            bool amountChanged = currentEntry != null && amount != currentEntry.Amount;
            if (invalidSelection)
            {
                EditorGUILayout.HelpBox("请选择带 Item 的Prefab", MessageType.Error);
                return;
            }
            if (!clearRequested && !prefabChanged && !amountChanged)
                return;

            Undo.RecordObject(metadata, "修改遗迹容器槽位");
            contents.Items ??= new List<StructureContainerItemEntry>();
            contents.Items.RemoveAll(entry => entry == null || entry.SlotIndex == slotIndex);
            if (!clearRequested && selectedPrefab != null)
            {
                contents.Items.Add(new StructureContainerItemEntry
                {
                    SlotIndex = slotIndex,
                    ItemPrefabId = selectedItemId,
                    Amount = amount
                });
                contents.Items = contents.Items
                    .OrderBy(entry => entry.SlotIndex)
                    .ToList();
            }
            EditorUtility.SetDirty(metadata);
            MarkAuthoringDirty(root);
        }
    }

    private GameObject ResolveItemPrefab(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;
        if (itemPrefabLookup != null && itemPrefabLookup.TryGetValue(itemId, out GameObject prefab))
            return prefab;
        return StructureAuthoringPrefabUtility.FindItemPrefabById(itemId);
    }

    private static int ResolveSlotMaxAmount(
        Inventory inventory,
        int slotIndex,
        GameObject itemPrefab)
    {
        Item item = itemPrefab?.GetComponent<Item>() ?? itemPrefab?.GetComponentInChildren<Item>(true);
        float unitVolume = item?.itemData?.Stack?.Volume ?? 1f;
        if (unitVolume > 1f)
            return 1;

        ItemSlot slot = inventory?.Data?.itemSlots != null &&
                        slotIndex >= 0 && slotIndex < inventory.Data.itemSlots.Count
            ? inventory.Data.itemSlots[slotIndex]
            : null;
        float capacity = slot != null && slot.SlotMaxVolume > 0f ? slot.SlotMaxVolume : 100f;
        return Mathf.Max(1, Mathf.FloorToInt(capacity / Mathf.Max(0.0001f, unitVolume)));
    }

    private static List<int> GetValidInventoryIndices(Mod_Inventory inventoryModule)
    {
        List<int> output = new();
        if (inventoryModule?.InventoryInstances == null)
            return output;

        for (int i = 0; i < inventoryModule.InventoryInstances.Count; i++)
        {
            if (inventoryModule.InventoryInstances[i]?.Data?.itemSlots != null)
                output.Add(i);
        }
        return output;
    }

    private static int ResolveInventoryOption(
        StructureContainerContents contents,
        Mod_Inventory inventoryModule,
        IReadOnlyList<int> inventoryIndices)
    {
        for (int option = 0; option < inventoryIndices.Count; option++)
        {
            if (inventoryIndices[option] != contents.TargetInventoryIndex)
                continue;

            Inventory indexedInventory = inventoryModule.InventoryInstances[inventoryIndices[option]];
            if (string.IsNullOrWhiteSpace(contents.TargetInventoryName) ||
                string.Equals(indexedInventory?.Data?.Name, contents.TargetInventoryName, StringComparison.Ordinal))
            {
                return option;
            }
        }

        if (!string.IsNullOrWhiteSpace(contents.TargetInventoryName))
        {
            for (int option = 0; option < inventoryIndices.Count; option++)
            {
                Inventory inventory = inventoryModule.InventoryInstances[inventoryIndices[option]];
                if (string.Equals(inventory?.Data?.Name, contents.TargetInventoryName, StringComparison.Ordinal))
                    return option;
            }
        }
        return 0;
    }

    private static string BuildInventoryLabel(Inventory inventory, int index)
    {
        string name = inventory?.Data?.Name;
        int slotCount = inventory?.Data?.itemSlots?.Count ?? 0;
        return $"{index + 1}. {(string.IsNullOrWhiteSpace(name) ? "未命名库存" : name)} ({slotCount}格)";
    }

    private static StructureContainerContents EnsureContainerContents(StructureItemAuthoring metadata)
    {
        metadata.ContainerContents ??= new StructureContainerContents();
        metadata.ContainerContents.Items ??= new List<StructureContainerItemEntry>();
        return metadata.ContainerContents;
    }

    private static void EnsureValidInventoryTarget(
        StructureContainerContents contents,
        Mod_Inventory inventoryModule,
        IReadOnlyList<int> inventoryIndices)
    {
        int option = ResolveInventoryOption(contents, inventoryModule, inventoryIndices);
        int inventoryIndex = inventoryIndices[option];
        Inventory inventory = inventoryModule.InventoryInstances[inventoryIndex];
        contents.TargetInventoryIndex = inventoryIndex;
        contents.TargetInventoryName = inventory?.Data?.Name;
    }

    private static string CreateUniqueMemberId(
        StructureAuthoringRoot root,
        string itemId,
        StructureItemAuthoring current)
    {
        string baseId = SanitizeId(itemId);
        if (string.IsNullOrWhiteSpace(baseId))
            baseId = "item";

        HashSet<string> usedIds = root.GetComponentsInChildren<StructureItemAuthoring>(true)
            .Where(metadata => metadata != null && metadata != current && !string.IsNullOrWhiteSpace(metadata.MemberId))
            .Select(metadata => metadata.MemberId)
            .ToHashSet(StringComparer.Ordinal);
        for (int suffix = 1; ; suffix++)
        {
            string candidate = $"{baseId}_{suffix}";
            if (!usedIds.Contains(candidate))
                return candidate;
        }
    }

    #endregion

    private static void DrawMarkerMetadata(
        StructureMarkerAuthoring marker,
        StructureAuthoringRoot root)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Marker配置", EditorStyles.boldLabel);
        StructureMarkerType type =
            (StructureMarkerType)EditorGUILayout.EnumPopup("类型", marker.Type);
        string markerId = EditorGUILayout.TextField("Marker ID", marker.MarkerId);
        Vector2 size = EditorGUILayout.Vector2Field("范围", marker.Size);
        string contentId = EditorGUILayout.TextField("内容ID", marker.ContentId);
        float chance = EditorGUILayout.Slider("概率", marker.Chance, 0f, 1f);
        int seedSalt = EditorGUILayout.IntField("Seed Salt", marker.SeedSalt);
        StructureOrientationMode orientationMode =
            (StructureOrientationMode)EditorGUILayout.EnumPopup(
                "内容朝向",
                marker.OrientationMode);
        if (type == marker.Type &&
            markerId == marker.MarkerId &&
            size == marker.Size &&
            contentId == marker.ContentId &&
            orientationMode == marker.OrientationMode &&
            Mathf.Approximately(chance, marker.Chance) &&
            seedSalt == marker.SeedSalt)
        {
            return;
        }

        Undo.RecordObject(marker, "修改遗迹Marker");
        marker.Type = type;
        marker.MarkerId = markerId;
        marker.Size = size;
        marker.ContentId = contentId;
        marker.OrientationMode = orientationMode;
        marker.Chance = chance;
        marker.SeedSalt = seedSalt;
        EditorUtility.SetDirty(marker);
        MarkAuthoringDirty(root);
    }

    private void DrawCompactIssues()
    {
        if (issues == null || issues.Count == 0)
            return;

        EditorGUILayout.Space();
        int errors = issues.Count(issue => issue.Severity == StructureValidationSeverity.Error);
        int warnings = issues.Count - errors;
        EditorGUILayout.LabelField($"验证：{errors} 错误 / {warnings} 警告", EditorStyles.boldLabel);
        int count = Mathf.Min(issues.Count, 4);
        for (int i = 0; i < count; i++)
        {
            StructureValidationIssue issue = issues[i];
            EditorGUILayout.HelpBox(
                issue.Message,
                issue.Severity == StructureValidationSeverity.Error
                    ? MessageType.Error
                    : MessageType.Warning);
        }
    }

    private static void MarkAuthoringDirty(StructureAuthoringRoot root)
    {
        if (root == null)
            return;
        EditorUtility.SetDirty(root);
        if (root.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
    }

    private Vector2 LocalToCanvas(StructureAuthoringRoot root, Vector2 local)
    {
        return new Vector2(
            canvasPan.x + local.x * canvasZoom,
            canvasPan.y + (root.Size.y - local.y) * canvasZoom);
    }

    private Vector2 CanvasToLocal(StructureAuthoringRoot root, Vector2 canvas)
    {
        return new Vector2(
            (canvas.x - canvasPan.x) / canvasZoom,
            root.Size.y - (canvas.y - canvasPan.y) / canvasZoom);
    }

    private Rect GetCellRect(StructureAuthoringRoot root, int x, int y)
    {
        return new Rect(
            canvasPan.x + x * canvasZoom,
            canvasPan.y + (root.Size.y - y - 1) * canvasZoom,
            canvasZoom,
            canvasZoom);
    }

    private static Vector2 ClampAndSnap(StructureAuthoringRoot root, Vector2 local)
    {
        float x = Mathf.Round(local.x * 2f) * 0.5f;
        float y = Mathf.Round(local.y * 2f) * 0.5f;
        return new Vector2(
            Mathf.Clamp(x, 0f, root.Size.x),
            Mathf.Clamp(y, 0f, root.Size.y));
    }

    private static bool IsCellInside(StructureAuthoringRoot root, Vector2Int cell)
    {
        return cell.x >= 0 &&
               cell.y >= 0 &&
               cell.x < root.Size.x &&
               cell.y < root.Size.y;
    }

    private static string GetMarkerLetter(StructureMarkerType type)
    {
        return type switch
        {
            StructureMarkerType.Entrance => "E",
            StructureMarkerType.Loot => "L",
            StructureMarkerType.Enemy => "X",
            StructureMarkerType.Optional => "O",
            _ => "C"
        };
    }

    private void DrawIssues()
    {
        if (issues == null || issues.Count == 0)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("验证结果", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(220f));
        for (int i = 0; i < issues.Count; i++)
        {
            StructureValidationIssue issue = issues[i];
            MessageType type = issue.Severity == StructureValidationSeverity.Error
                ? MessageType.Error
                : MessageType.Warning;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(issue.Message, type);
                if (issue.Context != null && GUILayout.Button("定位", GUILayout.Width(48f)))
                {
                    Selection.activeObject = issue.Context;
                    EditorGUIUtility.PingObject(issue.Context);
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static void MarkerButton(
        StructureAuthoringRoot root,
        StructureMarkerType type,
        string label)
    {
        if (!GUILayout.Button(label))
            return;

        GameObject markerObject = new($"{type}Marker");
        Undo.RegisterCreatedObjectUndo(markerObject, $"创建{label}Marker");
        Undo.SetTransformParent(markerObject.transform, root.transform, "设置Marker父级");
        markerObject.transform.localPosition = new Vector3(root.Pivot.x, root.Pivot.y, 0f);
        StructureMarkerAuthoring marker = Undo.AddComponent<StructureMarkerAuthoring>(markerObject);
        marker.Type = type;
        marker.MarkerId = $"{type.ToString().ToLowerInvariant()}_{root.GetComponentsInChildren<StructureMarkerAuthoring>(true).Length}";
        Selection.activeGameObject = markerObject;
    }

    private void DrawSceneOverlay(SceneView sceneView)
    {
        StructureAuthoringRoot root = GetCurrentRoot();
        if (root == null)
            return;

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Vector3 origin = root.transform.position;
        Handles.color = new Color(0.2f, 0.9f, 1f, 0.55f);
        for (int x = 0; x <= root.Size.x; x++)
            Handles.DrawLine(origin + new Vector3(x, 0f), origin + new Vector3(x, root.Size.y));
        for (int y = 0; y <= root.Size.y; y++)
            Handles.DrawLine(origin + new Vector3(0f, y), origin + new Vector3(root.Size.x, y));

        Handles.color = Color.yellow;
        Handles.DrawWireDisc(origin + new Vector3(root.Pivot.x, root.Pivot.y), Vector3.forward, 0.2f);
        Handles.Label(origin + new Vector3(root.Pivot.x, root.Pivot.y + 0.25f), "Pivot");

        StructureMarkerAuthoring[] markers = root.GetComponentsInChildren<StructureMarkerAuthoring>(true);
        for (int i = 0; i < markers.Length; i++)
        {
            StructureMarkerAuthoring marker = markers[i];
            Handles.color = GetMarkerColor(marker.Type);
            Handles.DrawSolidDisc(marker.transform.position, Vector3.forward, 0.15f);
            Handles.Label(marker.transform.position + Vector3.up * 0.2f, marker.Type.ToString());
        }
    }

    private static Color GetMarkerColor(StructureMarkerType type)
    {
        return type switch
        {
            StructureMarkerType.Entrance => Color.green,
            StructureMarkerType.Loot => Color.yellow,
            StructureMarkerType.Enemy => Color.red,
            StructureMarkerType.Optional => Color.cyan,
            _ => Color.gray
        };
    }

    private void CreateNewStructure()
    {
        string id = SanitizeId(newStructureId);
        if (string.IsNullOrWhiteSpace(id))
        {
            EditorUtility.DisplayDialog("遗迹编辑器", "请输入有效遗迹ID", "确定");
            return;
        }

        EnsureFolder(DefinitionFolder);
        EnsureFolder(TemplateFolder);
        EnsureFolder(AuthoringFolder);

        StructureCatalogSO catalog = StructureProjectInstaller.EnsureDefaultCatalog();
        if (catalog == null)
        {
            EditorUtility.DisplayDialog("遗迹创建失败", "无法加载或创建默认StructureCatalog。", "确定");
            return;
        }

        catalog.Definitions ??= new List<StructureDefinitionSO>();
        StructureDefinitionSO registeredDefinition = catalog.Definitions.Find(
            item => item != null &&
                    string.Equals(item.StructureId, id, StringComparison.Ordinal));
        if (registeredDefinition != null)
        {
            Selection.activeObject = registeredDefinition;
            EditorGUIUtility.PingObject(registeredDefinition);
            EditorUtility.DisplayDialog("遗迹ID已存在", $"已定位到遗迹：{id}", "确定");
            return;
        }

        string definitionPath = $"{DefinitionFolder}/{id}.asset";
        string templatePath = $"{TemplateFolder}/{id}_template.asset";
        string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{AuthoringFolder}/{id}_Authoring.prefab");
        GameObject authoring = null;
        bool creationCompleted = false;
        bool definitionCreated = false;
        bool templateCreated = false;
        try
        {
            UnityEngine.Object definitionAsset = AssetDatabase.LoadMainAssetAtPath(definitionPath);
            StructureDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>(definitionPath);
            if (definitionAsset != null && definition == null)
                throw new InvalidOperationException($"路径已被其他资产占用：{definitionPath}");

            if (definition == null)
            {
                definition = CreateInstance<StructureDefinitionSO>();
                AssetDatabase.CreateAsset(definition, definitionPath);
                definitionCreated = true;
            }

            if (definition == null)
                throw new InvalidOperationException("无法创建StructureDefinitionSO实例。");

            definition.StructureId = id;
            if (definitionCreated || string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                definition.DisplayName =
                    string.IsNullOrWhiteSpace(newDisplayName) ? id : newDisplayName.Trim();
            }
            definition.Templates ??= new List<WeightedStructureTemplate>();
            if (!AssetDatabase.Contains(definition))
                throw new InvalidOperationException($"创建Definition资产失败：{definitionPath}");

            UnityEngine.Object templateAsset = AssetDatabase.LoadMainAssetAtPath(templatePath);
            StructureTemplateSO template =
                AssetDatabase.LoadAssetAtPath<StructureTemplateSO>(templatePath);
            if (templateAsset != null && template == null)
                throw new InvalidOperationException($"路径已被其他资产占用：{templatePath}");

            if (template == null)
            {
                template = CreateInstance<StructureTemplateSO>();
                AssetDatabase.CreateAsset(template, templatePath);
                templateCreated = true;
            }

            if (template == null)
                throw new InvalidOperationException("无法创建StructureTemplateSO实例。");

            template.TemplateId = $"{id}_template";
            if (!AssetDatabase.Contains(template))
                throw new InvalidOperationException($"创建Template资产失败：{templatePath}");

            if (!definition.Templates.Exists(entry => entry?.Template == template))
            {
                definition.Templates.Add(new WeightedStructureTemplate
                {
                    Template = template,
                    Weight = 1f
                });
            }
            EditorUtility.SetDirty(definition);
            EditorUtility.SetDirty(template);

            authoring = new GameObject(id);
            StructureAuthoringRoot root = authoring.AddComponent<StructureAuthoringRoot>();
            if (root == null)
            {
                throw new InvalidOperationException(
                    "无法添加StructureAuthoringRoot。请确认该组件不在Editor-only程序集中。");
            }

            root.Definition = definition;
            root.Template = template;
            root.Size = template.Size;
            root.Pivot = template.Pivot;
            if (authoring.AddComponent<Grid>() == null)
                throw new InvalidOperationException("无法添加Grid组件。");

            GameObject tilemapObject = new("Tilemap", typeof(Tilemap), typeof(TilemapRenderer));
            tilemapObject.transform.SetParent(authoring.transform, false);
            root.Tilemap = tilemapObject.GetComponent<Tilemap>();
            if (root.Tilemap == null)
                throw new InvalidOperationException("无法创建Tilemap组件。");

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(authoring, prefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"创建Authoring Prefab失败：{prefabPath}");

            if (!catalog.Definitions.Contains(definition))
            {
                Undo.RecordObject(catalog, "添加遗迹定义");
                catalog.Definitions.Add(definition);
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            creationCompleted = true;
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            AssetDatabase.OpenAsset(prefab);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "遗迹创建失败",
                $"{exception.Message}\n\n已清理本次新建内容，原有资产未删除。",
                "确定");
        }
        finally
        {
            if (authoring != null)
                DestroyImmediate(authoring);

            if (!creationCompleted)
            {
                DeleteCreatedAsset(prefabPath);
                if (templateCreated)
                    DeleteCreatedAsset(templatePath);
                if (definitionCreated)
                    DeleteCreatedAsset(definitionPath);
                AssetDatabase.SaveAssets();
            }
        }
    }

    private static void DeleteCreatedAsset(string path)
    {
        if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadMainAssetAtPath(path) != null)
            AssetDatabase.DeleteAsset(path);
    }

    private static StructureAuthoringRoot GetCurrentRoot()
    {
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        return stage?.prefabContentsRoot?.GetComponent<StructureAuthoringRoot>();
    }

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static string SanitizeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        char[] chars = value.Trim().ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
                chars[i] = '_';
        }
        return new string(chars);
    }
}
