using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可携带的配方加工容器：每次有效捣击原子执行一份配方，立即产出并保留剩余原料。
/// 碗内库存按需扩容、同类产物自动堆叠；库存独立保存，UI 只负责手势和反馈。
/// </summary>
public sealed class Mod_Mortar : Module, IInteractable, IInventory
{
    #region 配置与状态
    public Ex_ModData_MemoryPackable Data = new Ex_ModData_MemoryPackable(); // 独立模块数据。
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => "Mod_Mortar";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public GameObject PanelPrefab; // 正式交互面板。
    public string StationId = "mortar"; // 配方工作站标识。
    private readonly MortarInventory bowl = new MortarInventory();
    private MortarState state;
    private BasePanel panel;
    private MortarInteractionView view;
    private RuntimeRecipe batch;
    private bool processing; // 库存事务通知期间合并视图刷新，避免中断持续捣击手势。
    private Player actor;
    private bool loaded;
    private CraftingCapabilities capabilities;
    #endregion

    #region 生命周期与存档
    public override void Load()
    {
        // 新道具创建初始状态；已有存档严格反序列化，不吞掉损坏数据。
        state = Data.BitData == null || Data.BitData.Length == 0
            ? new MortarState { Bowl = new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) }, "石臼") }
            : Data.GetData<MortarState>();
        if (state?.Bowl?.itemSlots == null)
            throw new InvalidOperationException("石臼存档缺少碗内库存。");
        bowl.item = item;
        bowl.StationId = StationId;
        bowl.Data = state.Bowl;
        bowl.Data.SetUnlimitedSlots(true);
        bowl.InitData();
        capabilities = new CraftingCapabilities { StationId = StationId, AllowOutputIntoInput = true };
        RebuildBatch();
        bowl.Data.Event_OnDataChanged += OnBowlChanged;
        item.OnAct += OnUse;
        loaded = true;
    }

    public override void Save()
    {
        if (state != null)
            Data.WriteData(state);
    }

    public override void Unload()
    {
        if (!loaded) return;
        loaded = false;
        item.OnAct -= OnUse;
        bowl.Data.Event_OnDataChanged -= OnBowlChanged;
        bowl.UnbindSlotDataEvents();
        if (panel != null)
        {
            panel.Close();
            panel.Closed -= OnClosed;
            view.Struck -= OnStrike;
            UIManager.ExistingInstance?.DestroyPanel(panel);
        }
        panel = null;
        view = null;
        batch = null;
        actor = null;
        bowl.DefaultTarget_Inventory = null;
    }
    private void OnDestroy() => Unload();
    #endregion

    #region 手持与地面交互
    private void OnUse()
    {
        if (item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.TryHandlePlacementAction() == true)
            return;
        if (item.InHand && item.Owner != null) OnInteractStart(item.Owner);
    }

    public void OnInteractStart(Item playerItem)
    {
        if (panel != null && panel.IsOpen()) { panel.Close(); return; }
        actor = playerItem as Player;
        Inventory hand = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (hand == null) throw new InvalidOperationException("石臼交互缺少玩家手部库存。");
        if (panel == null)
        {
            panel = UIManager.Instance.CreatePanelFromGameObject(PanelPrefab);
            view = panel.GetComponentInChildren<MortarInteractionView>(true);
            if (view == null) throw new InvalidOperationException("石臼面板缺少手势视图。");
            bowl.basePanel = panel;
            bowl.itemSlot_UI.Clear();
            view.SyncSlots(bowl);
            panel.PrepareForGamepadNavigation("关闭");
            view.Struck += OnStrike;
            panel.Closed += OnClosed;
            panel.GetButton("关闭").onClick.AddListener(panel.Close);
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(.5f, .5f);
            panel.rectTransform.anchoredPosition = Vector2.zero;
        }
        bowl.DefaultTarget_Inventory = hand;
        BuildingPanelActions buildingActions = panel.GetComponent<BuildingPanelActions>();
        if (buildingActions == null)
            throw new InvalidOperationException("石臼面板缺少 BuildingPanelActions，正式 Prefab 未完成建筑操作绑定。");
        buildingActions.Bind(item);
        panel.Open();
        view.SyncSlots(bowl);
        view.ResetPresentation();
        bowl.SyncQuickTransferTarget(panel);
        bowl.RefreshUI();
    }

    public void OnInteractCancel(Item playerItem) { if (panel != null) panel.Close(); }
    private void OnClosed()
    {
        view.ResetPresentation();
        bowl.DefaultTarget_Inventory = null;
        bowl.SyncQuickTransferTarget(panel);
        actor = null;
    }
    public Inventory GetDefaultTargetInventory() => bowl;
    #endregion

    #region 配方与原子加工
    private void OnBowlChanged(ItemSlot changedSlot)
    {
        if (processing) return;
        bowl.Data.EnsureSpareSlot();
        RebuildBatch();
        view?.SyncSlots(bowl);
    }

    /// <summary>选择当前可执行的一份配方，不放大原料和产物数量。</summary>
    private void RebuildBatch()
    {
        batch = null;
        foreach (RuntimeRecipe recipe in GameRes.Instance.GetRecipes(RecipeType.Crafting))
        {
            if (!string.Equals(recipe.RequiredStation, StationId, StringComparison.OrdinalIgnoreCase)) continue;
            if (recipe.inputs.RowItems_List.Count != 1 || recipe.outputs.results.Count == 0 || recipe.action.Count != 0)
                throw new InvalidOperationException($"石臼配方 {recipe.Id} 必须为无额外动作的单原料、多产物转换。");
            RuntimeRecipeIngredient ingredient = recipe.inputs.RowItems_List[0];
            if (ingredient.amount <= 0 || !CraftingRecipeMatcher.TryMatchRecipe(bowl, recipe, capabilities, out _)) continue;
            batch = recipe;
            ReserveOutputSlots(recipe);
            break;
        }
    }

    /// <summary>提交前为每个产物单位预留空格；实际事务优先合并同类，不同产物可同时原子写入。</summary>
    private void ReserveOutputSlots(RuntimeRecipe recipe)
    {
        int required = 0;
        foreach (RuntimeRecipeResult output in recipe.outputs.results)
            required = checked(required + Mathf.CeilToInt(output.amount));
        int empty = bowl.Data.itemSlots.FindAll(slot => slot.itemData == null).Count;
        while (empty++ < required)
            bowl.Data.itemSlots.Add(new ItemSlot(bowl.Data.itemSlots.Count) { SlotMaxVolume = Inventory_Data.DefaultSlotVolume });
    }

    private void OnStrike()
    {
        if (panel == null || !panel.IsOpen() || batch == null) return;
        processing = true;
        try
        {
            CraftingService.CraftRecipe(bowl, bowl, capabilities, batch, actor);
        }
        finally { processing = false; }
        OnBowlChanged(null);
    }

    #endregion

    /// <summary>只接收该工作站的原料与产物，避免无关物品或石臼自身被放入容器。</summary>
    private sealed class MortarInventory : Inventory
    {
        public string StationId;
        public override bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot)
        {
            ItemData candidate = sourceSlot?.itemData;
            if (candidate == null || ReferenceEquals(candidate, item?.itemData)) return false;
            foreach (RuntimeRecipe recipe in GameRes.Instance.GetRecipes(RecipeType.Crafting))
            {
                if (!string.Equals(recipe.RequiredStation, StationId, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (RuntimeRecipeIngredient ingredient in recipe.inputs.RowItems_List)
                {
                    if (ingredient.matchMode == MatchMode.ExactItem && ingredient.ItemName == candidate.IDName) return true;
                    if (ingredient.matchMode == MatchMode.ByTag && candidate.Tags != null && candidate.Tags.Contains(ingredient.Tag)) return true;
                }
                foreach (RuntimeRecipeResult output in recipe.outputs.results)
                    if (output.ItemName == candidate.IDName) return true;
            }
            return false;
        }
    }
}
