using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 可携带的配方加工容器：石臼通过捣击执行普通合成，坩埚通过加热执行容器配方。
/// 两种模式共用动态库存、快捷转移、事务匹配和 ItemData 持久化；UI 只负责交互与反馈。
/// </summary>
public sealed class Mod_Mortar : Module, IInteractable, IInventory
{
    #region 配置与状态
    public const string ModuleId = "Mod_Mortar";
    public const string CrucibleStationId = "crucible";
    public Ex_ModData_MemoryPackable Data = new Ex_ModData_MemoryPackable(); // 独立模块数据。
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => IsCrucible ? ModuleTickMode.FixedInterval : ModuleTickMode.Disabled;
    public override float FixedTickInterval => IsCrucible ? 1f : base.FixedTickInterval;
    public GameObject PanelPrefab; // 正式交互面板。
    public string StationId = "mortar"; // 配方工作站标识。
    public bool EnableStrikeGesture = true; // 坩埚模式关闭捣击手势，只保留材料容器面板。
    public string ContainerLabel = "石臼"; // 面板标题和异常信息使用的容器名称。
    private readonly MortarInventory bowl = new MortarInventory();
    private MortarState state;
    private BasePanel panel;
    private MortarInteractionView view;
    private RuntimeRecipe batch;
    private bool processing; // 库存事务通知期间合并视图刷新，避免中断持续捣击手势。
    private Player actor;
    private bool loaded;
    private CraftingCapabilities capabilities;
    public bool IsCrucible => string.Equals(StationId, CrucibleStationId, StringComparison.OrdinalIgnoreCase);
    #endregion

    #region 生命周期与存档
    public override void Load()
    {
        ContainerLabel = string.IsNullOrWhiteSpace(ContainerLabel) ? (IsCrucible ? "坩埚" : "石臼") : ContainerLabel;
        // 新道具创建初始状态；已有存档严格反序列化，不吞掉损坏数据。
        state = Data.BitData == null || Data.BitData.Length == 0
            ? new MortarState { Bowl = new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) }, ContainerLabel) }
            : Data.GetData<MortarState>();
        if (state?.Bowl?.itemSlots == null)
            throw new InvalidOperationException($"{ContainerLabel}存档缺少容器内库存。");
        bowl.item = item;
        bowl.StationId = StationId;
        bowl.MaterialOnly = IsCrucible;
        bowl.Data = state.Bowl;
        bowl.NormalizeStoredStacks();
        bowl.Data.SetUnlimitedSlots(true);
        bowl.InitData();
        capabilities = new CraftingCapabilities
        {
            RecipeType = IsCrucible ? RecipeType.Smelting : RecipeType.Crafting,
            StationId = StationId,
            AllowOutputIntoInput = true
        };
        if (!IsCrucible)
            RebuildBatch();
        bowl.Data.Event_OnDataChanged += OnBowlChanged;
        item.OnAct += OnUse;
        loaded = true;
    }

    public override void Save()
    {
        if (state != null)
        {
            state.Bowl = bowl.Data;
            Data.WriteData(state);
        }
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
            if (view != null && EnableStrikeGesture)
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
        if (item.InHand && item.Owner != null)
        {
            if (TryUseLiquidVessel(item.Owner))
                return;
            OnInteractStart(item.Owner);
        }
    }

    public void OnInteractStart(Item playerItem)
    {
        if (panel != null && panel.IsOpen()) { panel.Close(); return; }
        if (TryUseLiquidVessel(playerItem))
            return;
        actor = playerItem as Player;
        Inventory hand = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (hand == null) throw new InvalidOperationException($"{ContainerLabel}交互缺少玩家手部库存。");
        if (panel == null)
        {
            panel = UIManager.Instance.CreatePanelFromGameObject(PanelPrefab);
            view = panel.GetComponentInChildren<MortarInteractionView>(true);
            if (view == null) throw new InvalidOperationException($"{ContainerLabel}面板缺少容器视图。");
            bowl.basePanel = panel;
            bowl.itemSlot_UI.Clear();
            view.SyncSlots(bowl);
            panel.PrepareForGamepadNavigation("关闭");
            if (EnableStrikeGesture)
                view.Struck += OnStrike;
            panel.Closed += OnClosed;
            panel.GetButton("关闭").onClick.AddListener(panel.Close);
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(.5f, .5f);
            panel.rectTransform.anchoredPosition = Vector2.zero;
        }
        bowl.DefaultTarget_Inventory = hand;
        BuildingPanelActions buildingActions = panel.GetComponent<BuildingPanelActions>();
        if (buildingActions == null)
            throw new InvalidOperationException($"{ContainerLabel}面板缺少 BuildingPanelActions，正式 Prefab 未完成操作绑定。");
        buildingActions.Bind(item);
        panel.Open();
        panel.SetText("标题", ContainerLabel);
        view.SyncSlots(bowl);
        view.gameObject.SetActive(EnableStrikeGesture);
        view.ResetPresentation();
        bowl.SyncQuickTransferTarget(panel);
        bowl.RefreshUI();
    }

    public void OnInteractCancel(Item playerItem) { if (panel != null) panel.Close(); }
    private void OnClosed()
    {
        Save();
        view?.ResetPresentation();
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
        if (!IsCrucible)
            RebuildBatch();
        Save();
        view?.SyncSlots(bowl);
    }

    /// <summary>选择当前可执行的一份配方，不放大原料和产物数量。</summary>
    private void RebuildBatch()
    {
        if (IsCrucible)
            return;
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
        if (!EnableStrikeGesture || panel == null || !panel.IsOpen() || batch == null) return;
        processing = true;
        try
        {
            CraftingResult result = CraftingService.CraftRecipe(bowl, bowl, capabilities, batch, actor);
            if (result.Success)
                view?.PlayProcessingDust();
        }
        finally { processing = false; }
        OnBowlChanged(null);
    }

    #endregion

    #region 坩埚加热

    /// <summary>判断库存 ItemData 是否声明为坩埚模式，炉体无需依赖具体物品类型或建筑类型。</summary>
    public static bool IsCrucibleItem(ItemData itemData)
    {
        if (itemData?.ModuleDataDic == null || GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition))
            return false;

        foreach (KeyValuePair<string, ModuleData> pair in itemData.ModuleDataDic)
        {
            if (!string.Equals(pair.Value?.ID, ModuleId, StringComparison.Ordinal) ||
                !definition.TryGetModuleParameters(pair.Key, out string json) || string.IsNullOrWhiteSpace(json))
                continue;

            string stationId = JObject.Parse(json).Value<string>(nameof(StationId));
            return string.Equals(stationId, CrucibleStationId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>接受通用热源提供的温度和时间，只修改坩埚自身材料与液体状态。</summary>
    public static bool TryProcessCrucibleHeat(ItemData itemData, float temperature, float seconds)
    {
        if (!TryReadCrucibleState(itemData, out Ex_ModData_MemoryPackable storage, out MortarState state))
            return false;

        bool hasLiquid = Mod_WaterVessel.TryRead(
            itemData,
            out Ex_ModData_MemoryPackable liquidStorage,
            out LiquidContainerState liquidState) &&
            !Mod_WaterVessel.IsEmptyAmount(liquidState.Amount);
        if (hasLiquid)
        {
            // 液体仍在热源内时不进入被动冷却；热源当前温度就是坩埚液体温度。
            liquidState.Temperature = Mathf.Max(0f, temperature);
            liquidState.ProcessingSeconds = 0f;
            liquidStorage.WriteData(liquidState);
        }

        if (GameRes.ExistingInstance == null || state.Bowl?.itemSlots == null)
            return true;

        var materialInventory = new MortarInventory
        {
            StationId = CrucibleStationId,
            MaterialOnly = true,
            Data = state.Bowl
        };
        materialInventory.Data.SetUnlimitedSlots(true);
        CraftingCapabilities heatCapabilities = new CraftingCapabilities
        {
            RecipeType = RecipeType.Smelting,
            StationId = CrucibleStationId,
            AllowOutputIntoInput = true
        };

        RuntimeRecipe selectedRecipe = null;
        CraftingRecipeMatch selectedMatch = null;
        foreach (RuntimeRecipe recipe in GameRes.ExistingInstance.GetRecipes(RecipeType.Smelting))
        {
            if (!string.Equals(recipe.RequiredStation, CrucibleStationId, StringComparison.OrdinalIgnoreCase) ||
                recipe.LiquidOutput == null || temperature < recipe.Temperature || temperature > recipe.Temperature_Max)
                continue;
            if (hasLiquid && !string.Equals(recipe.LiquidOutput.LiquidId, liquidState.LiquidId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!CraftingRecipeMatcher.TryMatchRecipe(materialInventory, recipe, heatCapabilities, out CraftingRecipeMatch match))
                continue;

            selectedRecipe = recipe;
            selectedMatch = match;
            break;
        }

        if (selectedRecipe == null)
        {
            if (state.HeatingSeconds > 0f)
            {
                state.HeatingSeconds = 0f;
                storage.WriteData(state);
            }
            return true;
        }

        state.HeatingSeconds += Mathf.Max(0f, seconds);
        if (state.HeatingSeconds < selectedRecipe.ProcessingSeconds)
        {
            storage.WriteData(state);
            return true;
        }

        RuntimeLiquidOutput liquidOutput = selectedRecipe.LiquidOutput;
        if (!Mod_WaterVessel.CanAddLiquidToItemData(itemData, liquidOutput.LiquidId, liquidOutput.Amount) ||
            !CraftingTransaction.TryCreateInputOnly(materialInventory, selectedMatch,
                out CraftingTransaction transaction, out _))
        {
            storage.WriteData(state);
            return true;
        }

        if (!transaction.Commit(out _))
        {
            storage.WriteData(state);
            return true;
        }

        if (!Mod_WaterVessel.TryAddLiquidToItemData(itemData, liquidOutput.LiquidId, liquidOutput.Amount, temperature))
        {
            transaction.Rollback();
            storage.WriteData(state);
            return true;
        }

        transaction.Complete();
        state.HeatingSeconds = 0f;
        storage.WriteData(state);
        return true;
    }

    private static bool TryReadCrucibleState(
        ItemData itemData,
        out Ex_ModData_MemoryPackable storage,
        out MortarState state)
    {
        storage = null;
        state = null;
        if (!IsCrucibleItem(itemData))
            return false;

        foreach (ModuleData module in itemData.ModuleDataDic.Values)
        {
            if (!string.Equals(module?.ID, ModuleId, StringComparison.Ordinal) ||
                module is not Ex_ModData_MemoryPackable data)
                continue;

            storage = data;
            data.ReadData(ref state);
            state ??= new MortarState();
            if (state.Bowl?.itemSlots == null)
                throw new InvalidOperationException("坩埚存档缺少容器内库存。");
            return true;
        }

        return false;
    }

    private bool TryUseLiquidVessel(Item playerItem)
    {
        if (!IsCrucible || playerItem == null ||
            item.itemMods.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId) is not Mod_WaterVessel vessel)
            return false;

        bool hasLiquid = !Mod_WaterVessel.IsEmptyAmount(vessel.Data?.Amount ?? 0f);
        if (item.InHand && item.Owner == playerItem && (hasLiquid || vessel.HasCurrentWorldLiquidTarget(playerItem)))
        {
            vessel.Act();
            return true;
        }

        if (!item.InHand && hasLiquid)
        {
            vessel.OnInteractStart(playerItem);
            return true;
        }

        return false;
    }

    #endregion

    #region 坩埚离开热源后的冷却

    /// <summary>坩埚脱离热源后每秒降低一度；固体与液体分别保存在两套容器状态中。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (!IsCrucible || !GameNetwork.HasStateAuthority || item?.itemData == null ||
            float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f)
            return;

        Mod_WaterVessel vessel = item.itemMods.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId);
        if (vessel == null || Mod_WaterVessel.IsEmptyAmount(vessel.Data?.Amount ?? 0f))
            return;

        if (vessel.Data.Temperature > 0f)
            vessel.Data.Temperature = Mathf.Max(0f, vessel.Data.Temperature - deltaTime);
        LiquidSolidification solidification = vessel.CurrentLiquid?.Solidification;
        if (solidification == null && vessel.Data.Temperature <= 0f)
            return;
        if (solidification != null && vessel.Data.Temperature < solidification.MeltingPoint)
        {
            if (!TrySolidifyCrucible(vessel, solidification))
                vessel.CommitExternalState();
            return;
        }

        vessel.CommitExternalState();
    }

    /// <summary>把一份已冷却液体原子转换成固体，并保留坩埚中其他固体材料。</summary>
    private bool TrySolidifyCrucible(Mod_WaterVessel vessel, LiquidSolidification solidification)
    {
        if (vessel == null || solidification == null || state?.Bowl?.itemSlots == null ||
            GameRes.ExistingInstance == null)
            return false;

        state.Bowl.EnsureSpareSlot();
        var materialInventory = new MortarInventory
        {
            StationId = CrucibleStationId,
            MaterialOnly = true,
            Data = state.Bowl
        };
        materialInventory.Data.SetUnlimitedSlots(true);

        ItemData output = GameRes.ExistingInstance.CreateItemData(solidification.OutputItemId);
        output.Stack.Amount = Mathf.Max(1, Mathf.RoundToInt(vessel.Data.Amount * solidification.OutputAmount));
        if (!CraftingTransaction.TryCreateGrant(
                materialInventory,
                new[] { output },
                out CraftingTransaction transaction,
                out _))
            return false;

        if (!transaction.Commit(out _))
            return false;

        transaction.Complete();
        vessel.Data.LiquidId = null;
        vessel.Data.Amount = 0f;
        vessel.Data.Temperature = 0f;
        vessel.Data.ProcessingSeconds = 0f;
        vessel.CommitExternalState();
        state.Bowl = materialInventory.Data;
        bowl.Data = state.Bowl;
        bowl.Data.SetUnlimitedSlots(true);
        Save();
        view?.SyncSlots(bowl);
        return true;
    }

    #endregion

    /// <summary>只接收该工作站的原料与产物，避免无关物品或石臼自身被放入容器。</summary>
    private sealed class MortarInventory : Inventory
    {
        public string StationId;
        public bool MaterialOnly;

        /// <summary>加载时修正旧运行过程中被空白投放区拆散的同类堆叠，不改变不同物品的相对顺序。</summary>
        public void NormalizeStoredStacks()
        {
            if (Data?.itemSlots == null)
                return;

            for (int i = 0; i < Data.itemSlots.Count; i++)
            {
                ItemSlot targetSlot = Data.itemSlots[i];
                ItemData targetItem = targetSlot?.itemData;
                if (targetItem?.Stack == null || !targetItem.Stack.Stackable)
                    continue;

                for (int j = i + 1; j < Data.itemSlots.Count && !targetSlot.IsFull; j++)
                {
                    ItemSlot sourceSlot = Data.itemSlots[j];
                    ItemData sourceItem = sourceSlot?.itemData;
                    if (sourceItem?.Stack == null || !targetItem.CanStackWith(sourceItem))
                        continue;

                    float available = Mathf.Max(0f, targetSlot.SlotMaxVolume - targetItem.Stack.Amount);
                    float moved = Mathf.Min(available, sourceItem.Stack.Amount);
                    if (moved <= 0f)
                        continue;

                    targetItem.Stack.Amount += moved;
                    sourceItem.Stack.Amount -= moved;
                    if (sourceItem.Stack.Amount <= 0.0001f)
                        sourceSlot.itemData = null;
                }
            }
        }

        /// <summary>
        /// 石臼的空槽 UI 覆盖整块碗内区域；连续投放同类物品时必须先并入已有未满堆叠，
        /// 否则每次轻触都会命中新生成的尾部空槽，造成“一件一个槽位”的异常扩容。
        /// </summary>
        protected override int ResolveIncomingSlotIndex(ItemSlot sourceSlot, int requestedIndex)
        {
            if (Data?.itemSlots == null || sourceSlot?.itemData?.Stack == null ||
                requestedIndex < 0 || requestedIndex >= Data.itemSlots.Count ||
                Data.itemSlots.Contains(sourceSlot))
                return requestedIndex;

            ItemSlot requestedSlot = Data.itemSlots[requestedIndex];
            if (requestedSlot == null || requestedSlot.itemData != null || !sourceSlot.itemData.Stack.Stackable)
                return requestedIndex;

            for (int i = 0; i < Data.itemSlots.Count; i++)
            {
                if (i == requestedIndex)
                    continue;

                ItemSlot targetSlot = Data.itemSlots[i];
                if (targetSlot?.itemData == null || targetSlot.IsFull)
                    continue;
                if (targetSlot.itemData.CanStackWith(sourceSlot.itemData))
                    return i;
            }

            return requestedIndex;
        }

        public override bool CanAcceptQuickTransfer(ItemSlot sourceSlot, ItemSlot targetSlot)
        {
            ItemData candidate = sourceSlot?.itemData;
            if (candidate == null || ReferenceEquals(candidate, item?.itemData)) return false;
            if (MaterialOnly && Mod_WaterVessel.TryRead(candidate, out _, out _)) return false;
            RecipeType recipeType = string.Equals(StationId, CrucibleStationId, StringComparison.OrdinalIgnoreCase)
                ? RecipeType.Smelting
                : RecipeType.Crafting;
            foreach (RuntimeRecipe recipe in GameRes.Instance.GetRecipes(recipeType))
            {
                if (!string.Equals(recipe.RequiredStation, StationId, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (RuntimeRecipeIngredient ingredient in recipe.inputs.RowItems_List)
                {
                    if (ingredient.matchMode == MatchMode.ExactItem && ingredient.ItemName == candidate.IDName) return true;
                    if (ingredient.matchMode == MatchMode.ByTag && candidate.Tags != null && candidate.Tags.Contains(ingredient.Tag)) return true;
                }
                foreach (RuntimeRecipeResult output in recipe.outputs?.results ?? new List<RuntimeRecipeResult>())
                    if (output.ItemName == candidate.IDName) return true;
            }
            return false;
        }
    }
}
