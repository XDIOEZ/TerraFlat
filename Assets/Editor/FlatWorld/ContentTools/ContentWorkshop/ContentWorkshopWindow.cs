#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.Editor.ContentWorkshop
{
    /// <summary>
    /// FlatWorld 面向策划与 UGC 创作者的内容工坊。
    /// 通过物品图鉴、九宫格、热加工预设和物品模板生成现有 JSON 配置，避免手工维护 ID 与槽位编号。
    /// </summary>
    internal sealed class ContentWorkshopWindow : EditorWindow
    {
        #region 常量与模板

        private const string MenuPath = "FlatWorld/内容配置/内容工坊";
        private const float PaletteWidth = 310f;
        private const float InspectorWidth = 315f;
        private const float IngredientSlotSize = 92f;

        private static readonly WorkshopItemTemplate[] ItemTemplates =
        {
            new(
                WorkshopItemTemplateKind.Material,
                "普通材料",
                "可拾取、堆叠并用于配方的基础游戏道具。",
                "basic_items",
                "BasicItem_Base",
                "Bone"),
            new(
                WorkshopItemTemplateKind.Food,
                "食物与饮品",
                "继承基础物品外壳，并自动加入食物能力。",
                "basic_items",
                "BasicItem_Base",
                "Apple",
                "food"),
            new(
                WorkshopItemTemplateKind.Tool,
                "采集工具",
                "带耐久、伤害与挥动动画的斧类工具模板。",
                "tools",
                "Axe_Base",
                "Axe_Stone",
                "damage",
                "animation"),
            new(
                WorkshopItemTemplateKind.Weapon,
                "近战武器",
                "使用短刀外壳与伤害能力创建可手持武器。",
                "weapons",
                "Knife_Base",
                "Dagger_Stone",
                "damage"),
            new(
                WorkshopItemTemplateKind.Equipment,
                "装备",
                "使用现有装备外壳和装备存储模块。",
                "equipment",
                "Equipment_Base",
                "Chestplate_Wood",
                "Module_Equipment_Store"),
            new(
                WorkshopItemTemplateKind.Seed,
                "种子",
                "创建可拾取并使用现有播种行为的种子。",
                "seeds",
                "Seed_Base",
                "Seed_Apple",
                "Mod_Seed"),
            new(
                WorkshopItemTemplateKind.BuildingSummoner,
                "建筑召唤器",
                "复制帐篷召唤器行为；适合先制作同类建筑道具，再在高级配置中调整建筑关联。",
                "building_summoners",
                "BuildingSummoner_Tent_Base",
                "Tent_Summoner",
                "建筑模块",
                "生命值系统模块",
                "Module_Interaction")
        };

        #endregion

        #region 状态

        private ContentWorkshopRepository repository;
        private ContentWorkshopPage page = ContentWorkshopPage.Crafting;
        private WorkshopRecipeDraft recipeDraft;
        private WorkshopItemDraft itemDraft;
        private WorkshopItemTemplate selectedTemplate;
        private WorkshopItemEntry selectedPaletteItem;
        private WorkshopItemCategory paletteCategory = WorkshopItemCategory.All;
        private string paletteSearch = string.Empty;
        private int selectedIngredientIndex = -1;
        private int selectedExistingRecipeIndex;
        private int duplicateItemIndex;
        private bool eraseMode;
        private bool showAdvancedRecipe;
        private bool showAdvancedItem;
        private Vector2 paletteScroll;
        private Vector2 recipeEditorScroll;
        private Vector2 inspectorScroll;
        private Vector2 templateScroll;
        private Vector2 itemEditorScroll;
        private string statusMessage = "正在读取配置…";
        private MessageType statusType = MessageType.Info;

        private GUIStyle paletteButtonStyle;
        private GUIStyle slotStyle;
        private GUIStyle centeredTitleStyle;
        private GUIStyle templateButtonStyle;

        #endregion

        #region Unity 生命周期

        [MenuItem(MenuPath, priority = 1900)]
        public static void Open()
        {
            ContentWorkshopWindow window = GetWindow<ContentWorkshopWindow>();
            window.titleContent = new GUIContent("内容工坊");
            window.minSize = new Vector2(1050f, 690f);
            window.Show();
        }

        private void OnEnable()
        {
            selectedTemplate = ItemTemplates[0];
            itemDraft = new WorkshopItemDraft();
            itemDraft.ResetForTemplate(selectedTemplate);
            ReloadRepository();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawTopToolbar();

            if (repository == null)
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Error);
                if (GUILayout.Button("重新读取配置", GUILayout.Height(36f)))
                    ReloadRepository();
                return;
            }

            switch (page)
            {
                case ContentWorkshopPage.Crafting:
                    DrawRecipeWorkspace(false);
                    break;
                case ContentWorkshopPage.Heating:
                    DrawRecipeWorkspace(true);
                    break;
                case ContentWorkshopPage.Items:
                    DrawItemWorkspace();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            DrawStatusBar();
        }

        #endregion

        #region 顶部与状态

        private void DrawTopToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(28f));
            if (GUILayout.Button("九宫格合成", PageButtonStyle(ContentWorkshopPage.Crafting), GUILayout.Width(105f)))
                SwitchPage(ContentWorkshopPage.Crafting);
            if (GUILayout.Button("熔炼与烹饪", PageButtonStyle(ContentWorkshopPage.Heating), GUILayout.Width(115f)))
                SwitchPage(ContentWorkshopPage.Heating);
            if (GUILayout.Button("创建游戏道具", PageButtonStyle(ContentWorkshopPage.Items), GUILayout.Width(115f)))
                SwitchPage(ContentWorkshopPage.Items);

            GUILayout.FlexibleSpace();
            GUILayout.Label(repository?.LastValidationSummary ?? "配置未载入", EditorStyles.miniLabel);
            if (GUILayout.Button("重新加载", EditorStyles.toolbarButton, GUILayout.Width(78f)))
                ReloadRepository();
            EditorGUILayout.EndHorizontal();
        }

        private GUIStyle PageButtonStyle(ContentWorkshopPage target)
        {
            return page == target ? EditorStyles.toolbarButton : EditorStyles.toolbarButton;
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(30f));
            GUILayout.Label(statusType switch
            {
                MessageType.Error => "●",
                MessageType.Warning => "●",
                _ => "●"
            }, GUILayout.Width(16f));
            GUILayout.Label(statusMessage, EditorStyles.wordWrappedMiniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("保存前自动校验 · 备份位于 Library/FlatWorldContentWorkshop", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void SwitchPage(ContentWorkshopPage target)
        {
            if (page == target)
                return;
            page = target;
            selectedIngredientIndex = -1;
            eraseMode = false;
            if (target is ContentWorkshopPage.Crafting or ContentWorkshopPage.Heating)
                CreateNewRecipe(target == ContentWorkshopPage.Heating);
            Repaint();
        }

        private void ReloadRepository()
        {
            try
            {
                repository ??= new ContentWorkshopRepository();
                repository.Reload();
                selectedPaletteItem = repository.Items.FirstOrDefault(item => item.Definition?.Abstract != true);
                statusMessage = repository.LastValidationSummary;
                statusType = MessageType.Info;
                CreateNewRecipe(page == ContentWorkshopPage.Heating);
            }
            catch (Exception exception)
            {
                repository = null;
                statusMessage = $"内容配置读取失败：{exception.Message}";
                statusType = MessageType.Error;
                Debug.LogException(exception);
            }
        }

        #endregion

        #region 配方工作区

        private void DrawRecipeWorkspace(bool heating)
        {
            if (recipeDraft == null || recipeDraft.IsHeating != heating)
                CreateNewRecipe(heating);

            EditorGUILayout.BeginHorizontal();
            DrawItemPalette(PaletteWidth);
            DrawRecipeEditor(heating);
            DrawRecipeInspector(heating, InspectorWidth);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRecipeEditor(bool heating)
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawRecipeSelectionBar(heating);
            recipeEditorScroll = EditorGUILayout.BeginScrollView(recipeEditorScroll);

            if (heating)
                DrawHeatingPresetSelector();

            GUILayout.Space(10f);
            GUILayout.Label(
                heating ? "将原料放入加工托盘" : "像玩家一样摆出配方",
                centeredTitleStyle);
            GUILayout.Label(
                eraseMode
                    ? "橡皮擦已启用：点击格子清空"
                    : selectedPaletteItem == null
                        ? "先从左侧图鉴选择物品"
                        : $"已选择“{selectedPaletteItem.DisplayName}”：点击格子放入，右键清空",
                EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(8f);

            DrawIngredientCanvas();
            GUILayout.Space(16f);
            DrawOutputs();
            GUILayout.Space(12f);
            DrawRecipePrimaryActions();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRecipeSelectionBar(bool heating)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            IReadOnlyList<WorkshopRecipeRecord> records = GetPageRecipes(heating);
            string[] options = new[] { "— 选择已有配方继续编辑 —" }
                .Concat(records.Select(record => $"[{record.PackageId}] {record.DisplayName}"))
                .ToArray();
            int newIndex = EditorGUILayout.Popup(selectedExistingRecipeIndex, options);
            if (newIndex != selectedExistingRecipeIndex)
            {
                selectedExistingRecipeIndex = newIndex;
                if (newIndex > 0)
                    LoadRecipe(records[newIndex - 1]);
            }
            if (GUILayout.Button("新建", GUILayout.Width(68f)))
                CreateNewRecipe(heating);
            if (recipeDraft.IsExisting && GUILayout.Button("恢复磁盘版本", GUILayout.Width(105f)))
            {
                WorkshopRecipeRecord record = repository.FindRecipe(recipeDraft.OriginalId);
                if (record != null)
                    LoadRecipe(record);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHeatingPresetSelector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("选择处理方式", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            DrawHeatingPresetButton(WorkshopHeatingPreset.Cooking, "低温烹饪", "煎、煮、烤", 150f);
            DrawHeatingPresetButton(WorkshopHeatingPreset.Charcoal, "炭火加工", "烧制、制炭", 500f);
            DrawHeatingPresetButton(WorkshopHeatingPreset.Smelting, "高温熔炼", "矿石、金属", 1000f);
            DrawHeatingPresetButton(WorkshopHeatingPreset.Alloy, "合金制作", "多种金属", 1200f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawHeatingPresetButton(
            WorkshopHeatingPreset preset,
            string title,
            string subtitle,
            float temperature)
        {
            Color oldBackground = GUI.backgroundColor;
            if (recipeDraft.HeatingPreset == preset)
                GUI.backgroundColor = new Color(0.55f, 0.86f, 0.66f);
            if (GUILayout.Button(new GUIContent($"{title}\n{subtitle}\n约 {temperature:0}°C"),
                    templateButtonStyle,
                    GUILayout.MinWidth(108f),
                    GUILayout.Height(72f)))
            {
                recipeDraft.ApplyHeatingPreset(preset);
                SelectDefaultHeatingPackage(preset);
                selectedIngredientIndex = -1;
            }
            GUI.backgroundColor = oldBackground;
        }

        private void DrawIngredientCanvas()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(GUILayout.Width(IngredientSlotSize * 3f + 14f));
            for (int row = 0; row < WorkshopRecipeDraft.CanvasHeight; row++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int column = 0; column < WorkshopRecipeDraft.CanvasWidth; column++)
                {
                    int index = row * WorkshopRecipeDraft.CanvasWidth + column;
                    DrawIngredientSlot(index);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            eraseMode = GUILayout.Toggle(eraseMode, "橡皮擦", "Button", GUILayout.Width(82f));
            if (GUILayout.Button("清空九宫格", GUILayout.Width(105f)))
            {
                foreach (WorkshopIngredientDraft ingredient in recipeDraft.Ingredients)
                    ingredient.Clear();
                selectedIngredientIndex = -1;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawIngredientSlot(int index)
        {
            WorkshopIngredientDraft ingredient = recipeDraft.Ingredients[index];
            WorkshopItemEntry item = repository.FindItem(ingredient.ItemId);
            Rect rect = GUILayoutUtility.GetRect(
                IngredientSlotSize,
                IngredientSlotSize,
                GUILayout.Width(IngredientSlotSize),
                GUILayout.Height(IngredientSlotSize));

            Color oldBackground = GUI.backgroundColor;
            if (selectedIngredientIndex == index)
                GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
            GUI.Box(rect, GUIContent.none, slotStyle);
            GUI.backgroundColor = oldBackground;

            if (ingredient.IsTag)
            {
                GUI.Label(new Rect(rect.x + 5f, rect.y + 10f, rect.width - 10f, 38f),
                    $"任意\n{ingredient.Tag}", EditorStyles.centeredGreyMiniLabel);
            }
            else if (item?.Icon != null)
            {
                GUI.DrawTexture(new Rect(rect.x + 18f, rect.y + 8f, 56f, 56f),
                    AssetPreview.GetAssetPreview(item.Icon) ?? item.Icon.texture,
                    ScaleMode.ScaleToFit,
                    true);
            }
            else if (item != null)
            {
                GUI.Label(new Rect(rect.x + 5f, rect.y + 9f, rect.width - 10f, 45f),
                    item.DisplayName, EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                GUI.Label(rect, "+", centeredTitleStyle);
            }

            if (!ingredient.IsEmpty)
            {
                GUI.Label(new Rect(rect.x + 4f, rect.yMax - 22f, rect.width - 8f, 18f),
                    ingredient.IsTag ? $"标签 ×{ingredient.Amount}" : $"{item?.DisplayName ?? ingredient.ItemId} ×{ingredient.Amount}",
                    EditorStyles.centeredGreyMiniLabel);
            }

            HandleIngredientSlotInput(rect, index);
        }

        private void HandleIngredientSlotInput(Rect rect, int index)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition) || current.type != EventType.MouseDown)
                return;

            WorkshopIngredientDraft ingredient = recipeDraft.Ingredients[index];
            if (current.button == 1 || eraseMode)
            {
                ingredient.Clear();
                selectedIngredientIndex = -1;
            }
            else if (current.button == 0)
            {
                selectedIngredientIndex = index;
                if (selectedPaletteItem != null)
                    ingredient.SetItem(selectedPaletteItem.Id);
            }
            current.Use();
            Repaint();
        }

        private void DrawOutputs()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("产物", EditorStyles.boldLabel);
            for (int index = 0; index < recipeDraft.Outputs.Count; index++)
            {
                WorkshopOutputDraft output = recipeDraft.Outputs[index];
                WorkshopItemEntry item = repository.FindItem(output.ItemId);
                EditorGUILayout.BeginHorizontal();
                GUIContent content = item == null
                    ? new GUIContent("从左侧选择物品后点击这里")
                    : new GUIContent(item.DisplayName, GetIconTexture(item.Icon), item.Id);
                if (GUILayout.Button(content, GUILayout.Height(45f)))
                {
                    if (selectedPaletteItem != null)
                        output.ItemId = selectedPaletteItem.Id;
                }
                GUILayout.Label("数量", GUILayout.Width(34f));
                output.Amount = Mathf.Max(1, EditorGUILayout.IntField(output.Amount, GUILayout.Width(58f)));
                GUI.enabled = recipeDraft.Outputs.Count > 1;
                if (GUILayout.Button("移除", GUILayout.Width(52f)))
                {
                    recipeDraft.Outputs.RemoveAt(index);
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("添加副产物", GUILayout.Width(110f)))
                recipeDraft.Outputs.Add(new WorkshopOutputDraft());
            EditorGUILayout.EndVertical();
        }

        private void DrawRecipePrimaryActions()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("保存到配置", GUILayout.Width(150f), GUILayout.Height(38f)))
                SaveRecipe();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRecipeInspector(bool heating, float width)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width));
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            GUILayout.Label(heating ? "加工规则" : "配方规则", EditorStyles.boldLabel);

            recipeDraft.DisplayName = EditorGUILayout.TextField("玩家看到的名称", recipeDraft.DisplayName);
            DrawRecipePackagePopup(heating);

            if (!heating)
            {
                recipeDraft.Ordered = EditorGUILayout.Popup(
                    "摆放规则",
                    recipeDraft.Ordered ? 0 : 1,
                    new[] { "形状必须一致", "材料齐全即可" }) == 0;
                GUI.enabled = recipeDraft.Ordered;
                recipeDraft.AllowMirror = EditorGUILayout.Toggle("允许左右镜像", recipeDraft.AllowMirror);
                recipeDraft.AutoTrim = EditorGUILayout.Toggle("自动裁掉外围空格", recipeDraft.AutoTrim);
                GUI.enabled = true;
            }
            else
            {
                recipeDraft.Ordered = EditorGUILayout.Toggle("原料位置必须一致", recipeDraft.Ordered);
                recipeDraft.Temperature = EditorGUILayout.Slider(
                    "最低加工温度",
                    recipeDraft.Temperature,
                    20f,
                    2000f);
                recipeDraft.MaxTemperature = EditorGUILayout.Slider(
                    "烧焦/过热温度",
                    Mathf.Max(recipeDraft.Temperature, recipeDraft.MaxTemperature),
                    recipeDraft.Temperature,
                    2400f);
            }

            GUILayout.Space(8f);
            DrawSelectedIngredientInspector();

            GUILayout.Space(8f);
            showAdvancedRecipe = EditorGUILayout.Foldout(showAdvancedRecipe, "开发者高级设置", true);
            if (showAdvancedRecipe)
            {
                EditorGUI.indentLevel++;
                recipeDraft.Id = EditorGUILayout.TextField("稳定 ID", recipeDraft.Id);
                EditorGUILayout.SelectableLabel(
                    recipeDraft.IsExisting ? $"原始 ID：{recipeDraft.OriginalId}" : "新建配方",
                    EditorStyles.miniLabel,
                    GUILayout.Height(18f));
                EditorGUILayout.HelpBox(
                    "稳定 ID 用于任务、MOD 和存档引用。发布后不要随显示名称一起修改。",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(8f);
            EditorGUILayout.HelpBox(BuildRecipeSummary(), MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedIngredientInspector()
        {
            GUILayout.Label("当前材料槽", EditorStyles.boldLabel);
            if (selectedIngredientIndex < 0 || selectedIngredientIndex >= recipeDraft.Ingredients.Length)
            {
                EditorGUILayout.HelpBox("点击九宫格中的一个材料槽，可以调整数量或改成标签材料。", MessageType.None);
                return;
            }

            WorkshopIngredientDraft ingredient = recipeDraft.Ingredients[selectedIngredientIndex];
            if (ingredient.IsEmpty)
            {
                EditorGUILayout.HelpBox("当前格为空。从左侧选择物品，然后再次点击该格。", MessageType.None);
                return;
            }

            WorkshopItemEntry item = repository.FindItem(ingredient.ItemId);
            GUILayout.Label(ingredient.IsTag ? $"任意带有“{ingredient.Tag}”标签的物品" : item?.DisplayName ?? ingredient.ItemId,
                EditorStyles.wordWrappedLabel);
            ingredient.Amount = Mathf.Max(1, EditorGUILayout.IntField("消耗数量", ingredient.Amount));
            bool useTag = EditorGUILayout.Toggle("允许同标签替代", ingredient.IsTag);
            if (useTag)
            {
                string[] tags = GetTagOptions(item);
                int currentIndex = Mathf.Max(0, Array.FindIndex(tags, tag =>
                    string.Equals(tag, ingredient.Tag, StringComparison.OrdinalIgnoreCase)));
                int nextIndex = EditorGUILayout.Popup("物品标签", currentIndex, tags);
                ingredient.SetTag(tags[nextIndex]);
            }
            else if (ingredient.IsTag && selectedPaletteItem != null)
            {
                ingredient.SetItem(selectedPaletteItem.Id);
            }

            if (GUILayout.Button("清空当前材料槽"))
            {
                ingredient.Clear();
                selectedIngredientIndex = -1;
            }
        }

        private string[] GetTagOptions(WorkshopItemEntry item)
        {
            string[] preferred = item?.Definition?.Tags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            string currentTag = selectedIngredientIndex >= 0
                ? recipeDraft.Ingredients[selectedIngredientIndex].Tag
                : null;
            string[] all = new[] { currentTag }.Concat(preferred).Concat(repository.KnownTags)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return all.Length > 0 ? all : new[] { "Material" };
        }

        private void DrawRecipePackagePopup(bool heating)
        {
            WorkshopPackageOption[] packages = repository.RecipePackages
                .Where(package => package.Enabled && IsPackageForPage(package.Id, heating))
                .ToArray();
            if (packages.Length == 0)
            {
                EditorGUILayout.HelpBox("Manifest 中没有适用于当前页面的启用分包。", MessageType.Error);
                return;
            }

            int index = Mathf.Max(0, Array.FindIndex(packages, package =>
                string.Equals(package.Id, recipeDraft.PackageId, StringComparison.OrdinalIgnoreCase)));
            int next = EditorGUILayout.Popup("保存分类", index, packages.Select(package => package.DisplayName).ToArray());
            recipeDraft.PackageId = packages[next].Id;
        }

        private string BuildRecipeSummary()
        {
            int ingredients = recipeDraft.Ingredients.Count(ingredient => !ingredient.IsEmpty);
            int tagIngredients = recipeDraft.Ingredients.Count(ingredient => ingredient.IsTag);
            int outputs = recipeDraft.Outputs.Count(output => !string.IsNullOrWhiteSpace(output.ItemId));
            return $"{ingredients} 个材料槽（{tagIngredients} 个标签材料） · {outputs} 个产物 · " +
                   (recipeDraft.IsHeating
                       ? $"温度 {recipeDraft.Temperature:0}–{recipeDraft.MaxTemperature:0}°C"
                       : recipeDraft.Ordered ? "固定形状" : "无序合成");
        }

        private void SaveRecipe()
        {
            try
            {
                repository.SaveRecipe(recipeDraft);
                WorkshopRecipeRecord saved = repository.FindRecipe(recipeDraft.Id);
                if (saved != null)
                    LoadRecipe(saved);
                statusMessage = $"配方“{recipeDraft.DisplayName}”已保存并通过完整目录校验。";
                statusType = MessageType.Info;
            }
            catch (Exception exception)
            {
                statusMessage = $"配方未保存：{exception.Message}";
                statusType = MessageType.Error;
                Debug.LogException(exception);
            }
        }

        private void CreateNewRecipe(bool heating)
        {
            if (repository == null)
                return;
            string packageId = repository.RecipePackages
                .FirstOrDefault(package => package.Enabled && IsPackageForPage(package.Id, heating))?.Id;
            recipeDraft = WorkshopRecipeDraft.CreateNew(heating, packageId);
            selectedExistingRecipeIndex = 0;
            selectedIngredientIndex = -1;
            showAdvancedRecipe = false;
        }

        private void LoadRecipe(WorkshopRecipeRecord record)
        {
            recipeDraft = WorkshopRecipeDraft.FromRecord(record);
            selectedIngredientIndex = -1;
            eraseMode = false;
            statusMessage = $"已载入配方：{record.DisplayName}";
            statusType = MessageType.Info;
        }

        private IReadOnlyList<WorkshopRecipeRecord> GetPageRecipes(bool heating)
        {
            return repository.Recipes
                .Where(record => string.Equals(record.Definition?.RecipeType,
                    heating ? "smelting" : "crafting",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static bool IsPackageForPage(string packageId, bool heating)
        {
            bool isHeatingPackage = packageId?.StartsWith("cooking/", StringComparison.OrdinalIgnoreCase) == true ||
                                    packageId?.StartsWith("smelting/", StringComparison.OrdinalIgnoreCase) == true;
            return heating == isHeatingPackage;
        }

        private void SelectDefaultHeatingPackage(WorkshopHeatingPreset preset)
        {
            string prefix = preset switch
            {
                WorkshopHeatingPreset.Cooking => "cooking/",
                WorkshopHeatingPreset.Alloy => "smelting/alloys",
                _ => "smelting/ores"
            };
            WorkshopPackageOption match = repository.RecipePackages.FirstOrDefault(package =>
                package.Enabled && package.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                recipeDraft.PackageId = match.Id;
        }

        #endregion

        #region 物品图鉴

        private void DrawItemPalette(float width)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width));
            GUILayout.Label("物品图鉴", EditorStyles.boldLabel);
            paletteSearch = EditorGUILayout.TextField(new GUIContent("搜索", "名称、说明、稳定 ID、标签"), paletteSearch);
            paletteCategory = (WorkshopItemCategory)EditorGUILayout.EnumPopup("分类", paletteCategory);

            IEnumerable<WorkshopItemEntry> visible = repository.Items
                .Where(item => item.Definition?.Abstract != true)
                .Where(item => item.Matches(paletteSearch, paletteCategory));
            WorkshopItemEntry[] entries = visible.ToArray();
            GUILayout.Label($"{entries.Length} 个可用物品", EditorStyles.miniLabel);

            paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll);
            const int columns = 3;
            for (int index = 0; index < entries.Length; index += columns)
            {
                EditorGUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    int itemIndex = index + column;
                    if (itemIndex >= entries.Length)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }
                    DrawPaletteItem(entries[itemIndex]);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (selectedPaletteItem != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label($"已选择：{selectedPaletteItem.DisplayName}", EditorStyles.boldLabel);
                GUILayout.Label(selectedPaletteItem.Description, EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label($"分类：{CategoryLabel(selectedPaletteItem.Category)}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawPaletteItem(WorkshopItemEntry entry)
        {
            Color oldBackground = GUI.backgroundColor;
            if (ReferenceEquals(selectedPaletteItem, entry))
                GUI.backgroundColor = new Color(0.55f, 0.86f, 0.66f);
            GUIContent content = new(entry.DisplayName, GetIconTexture(entry.Icon), $"{entry.Description}\n{entry.Id}");
            if (GUILayout.Button(content, paletteButtonStyle, GUILayout.Width(89f), GUILayout.Height(82f)))
            {
                selectedPaletteItem = entry;
                eraseMode = false;
            }
            GUI.backgroundColor = oldBackground;
        }

        #endregion

        #region 物品创建工作区

        private void DrawItemWorkspace()
        {
            EditorGUILayout.BeginHorizontal();
            DrawItemTemplates();
            DrawItemPreviewAndSource();
            DrawItemInspector();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawItemTemplates()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(285f));
            GUILayout.Label("从模板开始", EditorStyles.boldLabel);
            GUILayout.Label("模板自动选择外壳、模块 Prefab 和安全默认值。", EditorStyles.wordWrappedMiniLabel);
            templateScroll = EditorGUILayout.BeginScrollView(templateScroll);
            foreach (WorkshopItemTemplate template in ItemTemplates)
            {
                Color oldBackground = GUI.backgroundColor;
                if (ReferenceEquals(selectedTemplate, template))
                    GUI.backgroundColor = new Color(0.55f, 0.86f, 0.66f);
                if (GUILayout.Button(new GUIContent($"{template.DisplayName}\n{template.Description}"),
                        templateButtonStyle,
                        GUILayout.Height(70f)))
                {
                    selectedTemplate = template;
                    itemDraft.ResetForTemplate(template);
                }
                GUI.backgroundColor = oldBackground;
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(5f);
            GUILayout.Label("也可以复制现有道具的外观与基础数值", EditorStyles.wordWrappedMiniLabel);
            WorkshopItemEntry[] existing = repository.Items
                .Where(item => item.Definition?.Abstract != true)
                .ToArray();
            if (existing.Length > 0)
            {
                duplicateItemIndex = Mathf.Clamp(duplicateItemIndex, 0, existing.Length - 1);
                duplicateItemIndex = EditorGUILayout.Popup(
                    duplicateItemIndex,
                    existing.Select(item => item.DisplayName).ToArray());
                if (GUILayout.Button("复制为新道具"))
                    DuplicateItem(existing[duplicateItemIndex]);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawItemPreviewAndSource()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            itemEditorScroll = EditorGUILayout.BeginScrollView(itemEditorScroll);
            GUILayout.Label("所见即所得外观", centeredTitleStyle);
            GUILayout.Label("正式保存时会自动注册稳定 Sprite 地址", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(10f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect previewRect = GUILayoutUtility.GetRect(240f, 240f, GUILayout.Width(240f), GUILayout.Height(240f));
            GUI.Box(previewRect, GUIContent.none, slotStyle);
            if (itemDraft.Icon != null)
            {
                Texture preview = AssetPreview.GetAssetPreview(itemDraft.Icon) ?? itemDraft.Icon.texture;
                Color oldColor = GUI.color;
                Matrix4x4 oldMatrix = GUI.matrix;
                GUI.color = itemDraft.Tint;
                GUIUtility.RotateAroundPivot(itemDraft.RotationDegrees, previewRect.center);
                GUI.DrawTexture(new Rect(previewRect.x + 25f, previewRect.y + 25f, 190f, 190f),
                    preview,
                    ScaleMode.ScaleToFit,
                    true);
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
            }
            else
            {
                GUI.Label(previewRect, "选择一个 Sprite\n作为物品图标与外观", centeredTitleStyle);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            itemDraft.Icon = (Sprite)EditorGUILayout.ObjectField(
                itemDraft.Icon,
                typeof(Sprite),
                false,
                GUILayout.Width(290f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(selectedTemplate.DisplayName, EditorStyles.boldLabel);
            GUILayout.Label(selectedTemplate.Description, EditorStyles.wordWrappedLabel);
            GUILayout.Label(
                $"保存到：{selectedTemplate.PackageId} · 继承：{selectedTemplate.ParentId}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawItemInspector()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(355f));
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            GUILayout.Label("道具属性", EditorStyles.boldLabel);

            itemDraft.DisplayName = EditorGUILayout.TextField("玩家看到的名称", itemDraft.DisplayName);
            GUILayout.Label("玩家说明", EditorStyles.miniLabel);
            itemDraft.Description = EditorGUILayout.TextArea(itemDraft.Description, GUILayout.MinHeight(58f));

            GUILayout.Space(8f);
            GUILayout.Label("基础手感", EditorStyles.boldLabel);
            itemDraft.Amount = Mathf.Max(0.01f, EditorGUILayout.FloatField("初始数量", itemDraft.Amount));
            itemDraft.Volume = Mathf.Max(0f, EditorGUILayout.FloatField("单个体积", itemDraft.Volume));
            itemDraft.Durability = Mathf.Max(0f, EditorGUILayout.FloatField("耐久度", itemDraft.Durability));
            itemDraft.CanBePickedUp = EditorGUILayout.Toggle("可以拾取", itemDraft.CanBePickedUp);
            itemDraft.Tags = EditorGUILayout.TextField(new GUIContent("标签", "逗号分隔；配方可按标签接受替代物品"), itemDraft.Tags);

            GUILayout.Space(8f);
            GUILayout.Label("外观", EditorStyles.boldLabel);
            itemDraft.Tint = EditorGUILayout.ColorField("颜色", itemDraft.Tint);
            itemDraft.RotationDegrees = NormalizeRotation(
                EditorGUILayout.FloatField(
                    new GUIContent("旋转角度 (°)", "绕 Sprite 中心旋转的 Z 轴角度"),
                    itemDraft.RotationDegrees));
            itemDraft.FlipX = EditorGUILayout.Toggle("水平翻转", itemDraft.FlipX);
            itemDraft.FlipY = EditorGUILayout.Toggle("垂直翻转", itemDraft.FlipY);

            GUILayout.Space(8f);
            GUILayout.Label("能力卡片", EditorStyles.boldLabel);
            itemDraft.AddFoodAbility = EditorGUILayout.ToggleLeft("可以食用", itemDraft.AddFoodAbility);
            if (itemDraft.AddFoodAbility)
                DrawFoodInspector();
            itemDraft.AddFuelAbility = EditorGUILayout.ToggleLeft("可以作为燃料", itemDraft.AddFuelAbility);
            itemDraft.AddCombatAbility = EditorGUILayout.ToggleLeft("可以造成近战伤害", itemDraft.AddCombatAbility);
            itemDraft.AddEquipmentAbility = EditorGUILayout.ToggleLeft("可以装备", itemDraft.AddEquipmentAbility);
            if (itemDraft.AddCombatAbility)
                itemDraft.Damage = Mathf.Max(0f, EditorGUILayout.FloatField("基础伤害", itemDraft.Damage));

            GUILayout.Space(8f);
            showAdvancedItem = EditorGUILayout.Foldout(showAdvancedItem, "开发者高级设置", true);
            if (showAdvancedItem)
            {
                EditorGUI.indentLevel++;
                itemDraft.Id = EditorGUILayout.TextField("稳定 ID", itemDraft.Id);
                EditorGUILayout.SelectableLabel($"父模板：{selectedTemplate.ParentId}",
                    EditorStyles.miniLabel,
                    GUILayout.Height(18f));
                EditorGUILayout.SelectableLabel($"参考定义：{selectedTemplate.ReferenceItemId}",
                    EditorStyles.miniLabel,
                    GUILayout.Height(18f));
                EditorGUILayout.HelpBox(
                    "建筑召唤器模板默认复制帐篷行为；制作全新建筑类型时还需要配置目标建筑与占地。",
                    MessageType.Warning);
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(12f);
            if (GUILayout.Button("创建并写入物品配置", GUILayout.Height(40f)))
                CreateItem();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        #region 食物参数编辑

        /// <summary>绘制食物模块的营养、进食、腐败和饮用方式参数。</summary>
        private void DrawFoodInspector()
        {
            GUILayout.Space(6f);
            GUILayout.Label("食物参数", EditorStyles.boldLabel);
            itemDraft.FoodConsumeKind = Mathf.Clamp(
                EditorGUILayout.Popup("食用方式", itemDraft.FoodConsumeKind, new[] { "固体食物", "饮品" }),
                0,
                1);

            GUILayout.Label("营养值", EditorStyles.miniBoldLabel);
            itemDraft.FoodCarbohydrates = NonNegativeFloatField("碳水化合物", itemDraft.FoodCarbohydrates);
            itemDraft.FoodFat = NonNegativeFloatField("脂肪", itemDraft.FoodFat);
            itemDraft.FoodProtein = NonNegativeFloatField("蛋白质", itemDraft.FoodProtein);
            itemDraft.FoodWater = NonNegativeFloatField("水分", itemDraft.FoodWater);
            itemDraft.FoodVitamins = NonNegativeFloatField("维生素", itemDraft.FoodVitamins);

            GUILayout.Label("营养容量上限", EditorStyles.miniBoldLabel);
            itemDraft.FoodMaxCarbohydrates = NonNegativeFloatField(
                "碳水上限",
                itemDraft.FoodMaxCarbohydrates);
            itemDraft.FoodMaxFat = NonNegativeFloatField("脂肪上限", itemDraft.FoodMaxFat);
            itemDraft.FoodMaxProtein = NonNegativeFloatField("蛋白质上限", itemDraft.FoodMaxProtein);
            itemDraft.FoodMaxWater = NonNegativeFloatField("水分上限", itemDraft.FoodMaxWater);
            itemDraft.FoodMaxVitamins = NonNegativeFloatField("维生素上限", itemDraft.FoodMaxVitamins);

            GUILayout.Label("食用特性", EditorStyles.miniBoldLabel);
            itemDraft.FoodMaxEatingProgress = Mathf.Max(
                1f,
                EditorGUILayout.FloatField("完整进食次数", itemDraft.FoodMaxEatingProgress));
            itemDraft.FoodNutritionConsumeSpeed = NonNegativeFloatField(
                "营养自然消耗速度",
                itemDraft.FoodNutritionConsumeSpeed);
            itemDraft.FoodWaterConsumeSpeedRate = NonNegativeFloatField(
                "水分消耗倍率",
                itemDraft.FoodWaterConsumeSpeedRate);
            itemDraft.FoodNutritionConsumeRate = NonNegativeFloatField(
                "营养消耗倍率",
                itemDraft.FoodNutritionConsumeRate);

            GUILayout.Label("腐败设置", EditorStyles.miniBoldLabel);
            itemDraft.FoodEnableSpoilage = EditorGUILayout.Toggle("启用腐败", itemDraft.FoodEnableSpoilage);
            if (itemDraft.FoodEnableSpoilage)
            {
                itemDraft.FoodSpoilageIntervalSeconds = NonNegativeFloatField(
                    "腐败间隔 (秒)",
                    itemDraft.FoodSpoilageIntervalSeconds);
                itemDraft.FoodSpoilageTargetItemId = EditorGUILayout.TextField(
                    new GUIContent("腐败目标 ID", "腐败后替换成的物品稳定 ID"),
                    itemDraft.FoodSpoilageTargetItemId);
            }
        }

        /// <summary>绘制不会接受负数的浮点数输入。</summary>
        private static float NonNegativeFloatField(string label, float value)
        {
            return Mathf.Max(0f, EditorGUILayout.FloatField(label, value));
        }

        #endregion

        private void DuplicateItem(WorkshopItemEntry source)
        {
            WorkshopItemTemplate template = TemplateForCategory(source.Category);
            selectedTemplate = template;
            itemDraft.ResetForTemplate(template);
            ItemDefinitionDto definition = source.Definition;
            itemDraft.DisplayName = source.DisplayName + " 副本";
            itemDraft.Description = source.Description;
            itemDraft.Icon = source.Icon;
            itemDraft.Durability = definition.MaxDurability ?? definition.Durability ?? itemDraft.Durability;
            itemDraft.Amount = definition.Amount ?? 1f;
            itemDraft.Volume = definition.Volume ?? 1f;
            itemDraft.CanBePickedUp = definition.CanBePickedUp ?? true;
            itemDraft.Tags = string.Join(", ", definition.Tags ?? new List<string>());
            if (definition.Visual != null)
            {
                itemDraft.Tint = definition.Visual.Color ?? Color.white;
                itemDraft.RotationDegrees = NormalizeRotation(
                    definition.Visual.RendererLocalEulerAngles?.z ?? 0f);
                itemDraft.FlipX = definition.Visual.FlipX ?? false;
                itemDraft.FlipY = definition.Visual.FlipY ?? false;
            }
            CopyFoodParameters(definition);
            statusMessage = $"已复制“{source.DisplayName}”的外观与基础数值；稳定 ID 已重新生成。";
            statusType = MessageType.Info;
        }

        #region 食物参数读取

        /// <summary>从已解析的食物模块读取参数，供复制物品时继续编辑。</summary>
        private void CopyFoodParameters(ItemDefinitionDto definition)
        {
            ItemModuleDefinitionDto foodModule = definition?.Modules?
                .FirstOrDefault(pair => string.Equals(pair.Key, "food", StringComparison.OrdinalIgnoreCase))
                .Value;
            if (foodModule == null)
                return;

            JObject foodData = GetObject(foodModule.Data, "FoodData");
            JObject nutrition = GetObject(foodData, "nutrition");
            itemDraft.AddFoodAbility = true;
            itemDraft.FoodCarbohydrates = ReadFloat(nutrition, "Carbohydrates", itemDraft.FoodCarbohydrates);
            itemDraft.FoodMaxCarbohydrates = ReadFloat(
                nutrition,
                "Max_Carbohydrates",
                itemDraft.FoodMaxCarbohydrates);
            itemDraft.FoodFat = ReadFloat(nutrition, "Fat", itemDraft.FoodFat);
            itemDraft.FoodMaxFat = ReadFloat(nutrition, "Max_Fat", itemDraft.FoodMaxFat);
            itemDraft.FoodProtein = ReadFloat(nutrition, "Protein", itemDraft.FoodProtein);
            itemDraft.FoodMaxProtein = ReadFloat(nutrition, "Max_Protein", itemDraft.FoodMaxProtein);
            itemDraft.FoodWater = ReadFloat(nutrition, "Water", itemDraft.FoodWater);
            itemDraft.FoodMaxWater = ReadFloat(nutrition, "Max_Water", itemDraft.FoodMaxWater);
            itemDraft.FoodVitamins = ReadFloat(nutrition, "Vitamins", itemDraft.FoodVitamins);
            itemDraft.FoodMaxVitamins = ReadFloat(nutrition, "Max_Vitamins", itemDraft.FoodMaxVitamins);
            itemDraft.FoodMaxEatingProgress = Mathf.Max(
                1f,
                ReadFloat(foodData, "Max_EatingProgress", itemDraft.FoodMaxEatingProgress));
            itemDraft.FoodNutritionConsumeSpeed = ReadFloat(
                GetObject(foodData, "nutritionConsumeSpeed"),
                "BaseValue",
                itemDraft.FoodNutritionConsumeSpeed);
            itemDraft.FoodWaterConsumeSpeedRate = ReadFloat(
                foodData,
                "WaterConsumeSpeedRate",
                itemDraft.FoodWaterConsumeSpeedRate);
            itemDraft.FoodNutritionConsumeRate = ReadFloat(
                foodData,
                "nutritionConsumeRate",
                itemDraft.FoodNutritionConsumeRate);
            itemDraft.FoodEnableSpoilage = ReadBool(
                foodModule.Data,
                "EnableSpoilage",
                itemDraft.FoodEnableSpoilage);
            itemDraft.FoodSpoilageIntervalSeconds = ReadFloat(
                foodModule.Data,
                "SpoilageIntervalSeconds",
                itemDraft.FoodSpoilageIntervalSeconds);
            itemDraft.FoodSpoilageTargetItemId = ReadString(
                foodModule.Data,
                "SpoilageTargetItemID",
                itemDraft.FoodSpoilageTargetItemId);
            itemDraft.FoodConsumeKind = Mathf.Clamp(
                ReadInt(foodModule.Parameters, "ConsumeKind", itemDraft.FoodConsumeKind),
                0,
                1);
        }

        /// <summary>按不区分大小写的属性名读取 JSON 对象。</summary>
        private static JObject GetObject(JObject source, string propertyName)
        {
            return source?.GetValue(propertyName, StringComparison.OrdinalIgnoreCase) as JObject;
        }

        /// <summary>读取食物浮点字段，字段缺失时保留默认值。</summary>
        private static float ReadFloat(JObject source, string propertyName, float fallback)
        {
            JToken token = source?.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
            return token == null ? fallback : token.Value<float>();
        }

        /// <summary>读取食物整数参数，字段缺失时保留默认值。</summary>
        private static int ReadInt(JObject source, string propertyName, int fallback)
        {
            JToken token = source?.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
            return token == null ? fallback : token.Value<int>();
        }

        /// <summary>读取食物布尔字段，字段缺失时保留默认值。</summary>
        private static bool ReadBool(JObject source, string propertyName, bool fallback)
        {
            JToken token = source?.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
            return token == null ? fallback : token.Value<bool>();
        }

        /// <summary>读取食物字符串字段，字段缺失时保留默认值。</summary>
        private static string ReadString(JObject source, string propertyName, string fallback)
        {
            string value = source?.GetValue(propertyName, StringComparison.OrdinalIgnoreCase)?.Value<string>();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        #endregion

        private void CreateItem()
        {
            try
            {
                repository.CreateItem(itemDraft, selectedTemplate);
                string createdName = itemDraft.DisplayName;
                itemDraft.ResetForTemplate(selectedTemplate);
                statusMessage = $"物品“{createdName}”已创建，并通过完整物品继承校验。";
                statusType = MessageType.Info;
            }
            catch (Exception exception)
            {
                statusMessage = $"物品未创建：{exception.Message}";
                statusType = MessageType.Error;
                Debug.LogException(exception);
            }
        }

        private static WorkshopItemTemplate TemplateForCategory(WorkshopItemCategory category)
        {
            WorkshopItemTemplateKind kind = category switch
            {
                WorkshopItemCategory.Food => WorkshopItemTemplateKind.Food,
                WorkshopItemCategory.Tool => WorkshopItemTemplateKind.Tool,
                WorkshopItemCategory.Weapon => WorkshopItemTemplateKind.Weapon,
                WorkshopItemCategory.Equipment => WorkshopItemTemplateKind.Equipment,
                WorkshopItemCategory.Seed => WorkshopItemTemplateKind.Seed,
                WorkshopItemCategory.Building => WorkshopItemTemplateKind.BuildingSummoner,
                _ => WorkshopItemTemplateKind.Material
            };
            return ItemTemplates.First(template => template.Kind == kind);
        }

        #endregion

        #region 样式与文本

        private void EnsureStyles()
        {
            paletteButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                imagePosition = ImagePosition.ImageAbove,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 10,
                padding = new RectOffset(4, 4, 5, 5)
            };
            slotStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(6, 6, 6, 6)
            };
            centeredTitleStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 15
            };
            templateButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 11,
                padding = new RectOffset(8, 8, 7, 7)
            };
        }

        private static string CategoryLabel(WorkshopItemCategory category)
        {
            return category switch
            {
                WorkshopItemCategory.Material => "材料",
                WorkshopItemCategory.Food => "食物",
                WorkshopItemCategory.Tool => "工具",
                WorkshopItemCategory.Weapon => "武器",
                WorkshopItemCategory.Equipment => "装备",
                WorkshopItemCategory.Seed => "种子",
                WorkshopItemCategory.Building => "建筑",
                WorkshopItemCategory.Other => "其他",
                _ => "全部"
            };
        }

        /// <summary>将 Sprite 转为 IMGUI 可显示缩略图；图集子 Sprite 优先使用裁剪后的 AssetPreview。</summary>
        private static Texture GetIconTexture(Sprite sprite)
        {
            if (sprite == null)
                return null;
            return AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite) ?? sprite.texture;
        }

        /// <summary>把角度限制到 [-180, 180)，避免手工输入不断累积。</summary>
        private static float NormalizeRotation(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
                return 0f;
            return Mathf.Repeat(degrees + 180f, 360f) - 180f;
        }

        #endregion
    }
}

#endif
