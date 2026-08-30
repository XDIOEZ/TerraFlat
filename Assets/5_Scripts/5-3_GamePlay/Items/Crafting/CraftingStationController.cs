using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 合成站共享交互控制器：以输入库存为唯一候选来源，维护候选配方、当前选择、点击进度、
/// 双输出预览与原子提交。手工台和世界工作台只负责各自槽位及面板生命周期，不再各存一份合成状态。
/// 候选按钮复用正式 Prefab 中的隐藏模板，条目高度由 Prefab 保证不低于 60 逻辑像素。
/// </summary>
public sealed class CraftingStationController : IDisposable
{
    #region 节点契约

    public const string CandidateContentName = "配方候选内容";
    public const string CandidateTemplateName = "配方候选模板";
    public const string CandidateIconName = "图标";
    public const string CandidateLabelName = "名称";
    public const string CandidateAmountName = "数量";

    #endregion

    #region 运行时状态

    private readonly BasePanel panel;
    private readonly Inventory inputInventory;
    private readonly Inventory outputInventory;
    private readonly CraftingCapabilities capabilities;
    private readonly Button craftButton;
    private readonly Func<int> requiredClickCount;
    private readonly Func<Player> resolveActor;
    private readonly Action<string> log;
    private readonly RectTransform candidateContent;
    private readonly Button candidateTemplate;
    private readonly List<CandidateEntry> candidateEntries = new List<CandidateEntry>();
    private readonly List<CraftingOutputPreview> outputPreviews = new List<CraftingOutputPreview>();

    private IReadOnlyList<CraftingRecipeMatch> matches = Array.Empty<CraftingRecipeMatch>();
    private RuntimeRecipe selectedRecipe;
    private string lastFailureMessage;
    private int currentClickProgress;
    private bool disposed;

    #endregion

    #region 初始化与清理

    public CraftingStationController(
        BasePanel panel,
        Inventory inputInventory,
        Inventory outputInventory,
        CraftingCapabilities capabilities,
        Func<int> requiredClickCount,
        Func<Player> resolveActor,
        Action<string> log = null)
    {
        this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
        this.inputInventory = inputInventory ?? throw new ArgumentNullException(nameof(inputInventory));
        this.outputInventory = outputInventory ?? throw new ArgumentNullException(nameof(outputInventory));
        this.capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        this.requiredClickCount = requiredClickCount ?? throw new ArgumentNullException(nameof(requiredClickCount));
        this.resolveActor = resolveActor ?? throw new ArgumentNullException(nameof(resolveActor));
        this.log = log;

        craftButton = panel.GetButton("合成按钮")
            ?? throw new InvalidOperationException("[CraftingStationController] 面板缺少合成按钮");
        candidateContent = FindRect(panel.transform, CandidateContentName)
            ?? throw new InvalidOperationException($"[CraftingStationController] 面板缺少 {CandidateContentName}");
        candidateTemplate = FindRect(panel.transform, CandidateTemplateName)?.GetComponent<Button>()
            ?? throw new InvalidOperationException($"[CraftingStationController] 面板缺少 {CandidateTemplateName} Button");

        candidateTemplate.gameObject.SetActive(false);
        AdoptExistingEntries();
        BindOutputPreviews();

        craftButton.onClick.RemoveListener(OnCraftButtonClick);
        craftButton.onClick.AddListener(OnCraftButtonClick);
        inputInventory.Data.Event_OnDataChanged -= OnInputChanged;
        inputInventory.Data.Event_OnDataChanged += OnInputChanged;
        outputInventory.Data.Event_OnDataChanged -= OnOutputChanged;
        outputInventory.Data.Event_OnDataChanged += OnOutputChanged;

        RefreshCandidates();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        craftButton.onClick.RemoveListener(OnCraftButtonClick);
        if (inputInventory.Data != null)
            inputInventory.Data.Event_OnDataChanged -= OnInputChanged;
        if (outputInventory.Data != null)
            outputInventory.Data.Event_OnDataChanged -= OnOutputChanged;

        for (int index = 0; index < candidateEntries.Count; index++)
            candidateEntries[index].Unbind();
        candidateEntries.Clear();
        outputPreviews.Clear();
    }

    #endregion

    #region 输入、选择与制作

    private void OnInputChanged(ItemSlot _)
    {
        ResetProgress();
        RefreshCandidates();
    }

    private void OnOutputChanged(ItemSlot _)
    {
        RefreshSelectedRecipe();
    }

    private void SelectRecipe(RuntimeRecipe recipe)
    {
        if (recipe == null || ReferenceEquals(selectedRecipe, recipe))
            return;

        selectedRecipe = recipe;
        ResetProgress();
        RefreshSelectionVisuals();
        RefreshSelectedRecipe();
        log?.Invoke($"已选择配方：{recipe.Id}");
    }

    private void OnCraftButtonClick()
    {
        RuntimeRecipe recipe = selectedRecipe;
        if (recipe == null)
            return;

        CraftingResult preview = CraftingService.PreviewRecipe(
            inputInventory,
            outputInventory,
            capabilities,
            recipe);
        if (!preview.Success)
        {
            ReportFailure(preview);
            ResetProgress();
            RefreshCandidates();
            return;
        }

        int requiredClicks = Mathf.Max(1, requiredClickCount());
        currentClickProgress = Mathf.Min(currentClickProgress + 1, requiredClicks);
        RefreshOutputPreviews(preview.Outputs, currentClickProgress / (float)requiredClicks);
        if (currentClickProgress < requiredClicks)
            return;

        CraftingResult result = CraftingService.CraftRecipe(
            inputInventory,
            outputInventory,
            capabilities,
            recipe,
            resolveActor());
        if (!result.Success)
            ReportFailure(result);
        else
            PlaySuccess();

        ResetProgress();
        RefreshCandidates();
    }

    #endregion

    #region 候选列表

    /// <summary>输入变化时一次更新候选集合；输出空间不会隐藏材料本身能够制作的配方。</summary>
    private void RefreshCandidates()
    {
        string previousRecipeId = selectedRecipe?.Id;
        if (!CraftingRecipeMatcher.TryMatchAll(
                inputInventory,
                capabilities,
                out matches,
                out _))
        {
            matches = Array.Empty<CraftingRecipeMatch>();
        }

        EnsureEntryCount(matches.Count);
        selectedRecipe = FindRecipe(previousRecipeId) ?? (matches.Count > 0 ? matches[0].Recipe : null);

        for (int index = 0; index < candidateEntries.Count; index++)
        {
            CandidateEntry entry = candidateEntries[index];
            if (index >= matches.Count)
            {
                entry.Clear();
                continue;
            }

            entry.Show(matches[index].Recipe);
        }

        RefreshSelectionVisuals();
        RefreshSelectedRecipe();
        LayoutRebuilder.MarkLayoutForRebuild(candidateContent);
        log?.Invoke($"候选刷新：数量={matches.Count}，选择={selectedRecipe?.Id ?? "<无>"}");
    }

    private RuntimeRecipe FindRecipe(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId))
            return null;

        for (int index = 0; index < matches.Count; index++)
        {
            RuntimeRecipe recipe = matches[index]?.Recipe;
            if (recipe != null && string.Equals(recipe.Id, recipeId, StringComparison.OrdinalIgnoreCase))
                return recipe;
        }

        return null;
    }

    private void EnsureEntryCount(int count)
    {
        bool hierarchyChanged = false;
        while (candidateEntries.Count < count)
        {
            Button button = UnityEngine.Object.Instantiate(candidateTemplate, candidateContent, false);
            button.name = $"配方候选项_{candidateEntries.Count + 1}";
            ClearTemplateText(button.transform);
            candidateEntries.Add(CreateEntry(button));
            hierarchyChanged = true;
        }

        if (hierarchyChanged)
            panel.RefreshUIComponents();
    }

    private void AdoptExistingEntries()
    {
        for (int index = 0; index < candidateContent.childCount; index++)
        {
            Transform child = candidateContent.GetChild(index);
            if (child == candidateTemplate.transform || !child.name.StartsWith("配方候选项_", StringComparison.Ordinal))
                continue;

            Button button = child.GetComponent<Button>();
            if (button != null)
                candidateEntries.Add(CreateEntry(button));
        }
    }

    private CandidateEntry CreateEntry(Button button)
    {
        var entry = new CandidateEntry(
            button,
            FindRect(button.transform, CandidateIconName)?.GetComponent<Image>(),
            FindRect(button.transform, CandidateLabelName)?.GetComponent<TextMeshProUGUI>(),
            FindRect(button.transform, CandidateAmountName)?.GetComponent<TextMeshProUGUI>());
        entry.Bind(() => SelectRecipe(entry.Recipe));
        return entry;
    }

    private void RefreshSelectionVisuals()
    {
        for (int index = 0; index < candidateEntries.Count; index++)
            candidateEntries[index].SetSelected(ReferenceEquals(candidateEntries[index].Recipe, selectedRecipe));
    }

    #endregion

    #region 预览与诊断

    private void BindOutputPreviews()
    {
        outputPreviews.Clear();
        for (int index = 0; index < outputInventory.itemSlot_UI.Count; index++)
        {
            CraftingOutputPreview preview = CraftingOutputPreview.Attach(panel, outputInventory.itemSlot_UI[index]);
            if (preview != null)
                outputPreviews.Add(preview);
        }
    }

    private void RefreshSelectedRecipe()
    {
        if (selectedRecipe == null)
        {
            craftButton.interactable = false;
            ClearOutputPreviews();
            lastFailureMessage = string.Empty;
            return;
        }

        CraftingResult preview = CraftingService.PreviewRecipe(
            inputInventory,
            outputInventory,
            capabilities,
            selectedRecipe);
        craftButton.interactable = preview.Success;

        CraftingResult description = CraftingService.DescribeRecipe(selectedRecipe);
        if (description.Success)
        {
            int requiredClicks = Mathf.Max(1, requiredClickCount());
            RefreshOutputPreviews(description.Outputs, currentClickProgress / (float)requiredClicks);
        }
        else
        {
            ClearOutputPreviews();
        }
    }

    /// <summary>按空闲输出槽顺序显示所选配方产物，已占用槽保持真实物品优先。</summary>
    private void RefreshOutputPreviews(IReadOnlyList<ItemData> outputs, float progress01)
    {
        ClearOutputPreviews();
        if (outputs == null || outputs.Count == 0)
            return;

        int previewIndex = 0;
        for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
        {
            while (previewIndex < outputPreviews.Count && outputPreviews[previewIndex].IsOutputOccupied())
                previewIndex++;
            if (previewIndex >= outputPreviews.Count)
                return;

            outputPreviews[previewIndex].Show(outputs[outputIndex], progress01);
            previewIndex++;
        }
    }

    private void ResetProgress()
    {
        currentClickProgress = 0;
        for (int index = 0; index < outputPreviews.Count; index++)
            outputPreviews[index].SetProgress(0f);
    }

    private void ClearOutputPreviews()
    {
        for (int index = 0; index < outputPreviews.Count; index++)
            outputPreviews[index].Clear();
    }

    private void PlaySuccess()
    {
        for (int index = 0; index < outputPreviews.Count; index++)
        {
            if (outputPreviews[index].IsOutputOccupied())
                outputPreviews[index].PlaySuccess();
        }
    }

    private void ReportFailure(CraftingResult result)
    {
        CraftingPreviewDiagnostics.ReportFailure(
            nameof(CraftingStationController),
            inputInventory,
            result,
            true,
            ref lastFailureMessage,
            capabilities);
    }

    #endregion

    #region 层级工具与候选条目

    private static RectTransform FindRect(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root as RectTransform;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index] != null && transforms[index].name == name)
                return transforms[index] as RectTransform;
        }

        return null;
    }

    private static void ClearTemplateText(Transform root)
    {
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int index = 0; index < texts.Length; index++)
            texts[index].text = string.Empty;
    }

    private sealed class CandidateEntry
    {
        private readonly Button button;
        private readonly Image background;
        private readonly Image icon;
        private readonly TextMeshProUGUI label;
        private readonly TextMeshProUGUI amount;
        private UnityAction selectAction;

        public CandidateEntry(Button button, Image icon, TextMeshProUGUI label, TextMeshProUGUI amount)
        {
            this.button = button ?? throw new ArgumentNullException(nameof(button));
            background = button.targetGraphic as Image ?? button.GetComponent<Image>();
            this.icon = icon ?? throw new InvalidOperationException("配方候选模板缺少图标 Image");
            this.label = label ?? throw new InvalidOperationException("配方候选模板缺少名称 TMP");
            this.amount = amount ?? throw new InvalidOperationException("配方候选模板缺少数量 TMP");
        }

        public RuntimeRecipe Recipe { get; private set; }

        public void Bind(UnityAction action)
        {
            Unbind();
            selectAction = action;
            button.onClick.AddListener(selectAction);
        }

        public void Unbind()
        {
            if (selectAction != null)
                button.onClick.RemoveListener(selectAction);
            selectAction = null;
        }

        public void Show(RuntimeRecipe recipe)
        {
            Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            CraftingResult description = CraftingService.DescribeRecipe(recipe);
            ItemData primaryOutput = description.PrimaryOutput;
            string itemId = primaryOutput?.IDName ?? recipe.outputs?.results?[0]?.ItemName;

            icon.sprite = null;
            icon.enabled = false;
            if (GameRes.Instance != null &&
                GameRes.Instance.TryGetItemPresentation(itemId, out string displayName, out Sprite sprite))
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                ConfigureItemLabel(itemId, displayName);
            }
            else
            {
                label.text = FlatWorldLocalizationService.GetUiText(recipe.name);
            }

            float outputAmount = primaryOutput?.Stack?.Amount ?? recipe.outputs?.results?[0]?.amount ?? 1f;
            int extraOutputCount = Mathf.Max(0, (recipe.outputs?.results?.Count ?? 1) - 1);
            amount.text = extraOutputCount > 0
                ? $"×{outputAmount:0.##}  +{extraOutputCount}"
                : $"×{outputAmount:0.##}";
            button.interactable = description.Success;
            button.gameObject.SetActive(true);
        }

        public void Clear()
        {
            Recipe = null;
            icon.sprite = null;
            icon.enabled = false;
            label.text = string.Empty;
            amount.text = string.Empty;
            button.gameObject.SetActive(false);
        }

        public void SetSelected(bool selected)
        {
            if (background != null)
                background.color = selected ? FlatWorldUITheme.Selection : FlatWorldUITheme.SurfaceRaised;
        }

        private void ConfigureItemLabel(string itemId, string displayName)
        {
            if (GameRes.Instance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
            {
                LocalizedTextBinder binder = label.GetComponent<LocalizedTextBinder>();
                if (binder == null)
                    binder = label.gameObject.AddComponent<LocalizedTextBinder>();
                binder.Configure(
                    FlatWorldLocalizationService.DefaultTable,
                    definition.LabelKey,
                    displayName);
                return;
            }

            label.text = displayName;
        }
    }

    #endregion
}
