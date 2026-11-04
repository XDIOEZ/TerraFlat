using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>
/// 通用燃料交互模块：提供面板入口、库存投料和火种点燃。
/// 只协调燃料、燃烧和可选建筑模块，不包含任何具体物品语义。
/// </summary>
public sealed class Mod_FuelInteraction : Module, IInteractable, IItemModuleDependencyBinder, IInventory
{
    #region 数据与依赖

    public const string ModuleId = "Mod_FuelInteraction";
    public const float MinimumHotItemIgnitionTemperatureCelsius = 500f;
    private const float FuelEpsilon = 0.01f;

    public Ex_ModData_MemoryPackable ModData = new() { ID = ModuleId };
    public Inventory IgnitionInventory = new() { Data = MachineInventory.NewData("火种", 1) };

    [Tooltip("世界中允许玩家打开燃料面板的交互距离。")]
    public float reach = 2f;

    [Tooltip("可以重新点燃对象的物品 ID。")]
    public string[] ignitionItemIds = { "FireSeed" };

    [Tooltip("可以重新点燃对象的物品 Tag。")]
    public string[] ignitionTags = { Tag.CombustionTinder };

    [Tooltip("燃料为零时，火种最多提供多少燃料用于起燃。")]
    public float ignitionFuelValueOverride = 8f;

    public static event Action<Mod_FuelInteraction, Item> OpenRequested;
    public event Action Changed;

    private Mod_Fuel fuel;
    private Mod_Combustion combustion;
    private Mod_Building building;

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 1f;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ??
                         throw new ArgumentException("燃料交互模块数据类型错误。");
    }

    public bool IsBurning => combustion?.IsBurning == true;
    public float CurrentFuel => fuel?.Data != null
        ? Mathf.Clamp(fuel.Data.Fuel.x, 0f, Mathf.Max(0f, fuel.Data.Fuel.y))
        : 0f;
    public float MaxFuel => fuel?.Data?.Fuel.y ?? 0f;
    public float FuelRatio => fuel?.GetFuelRatio() ?? 0f;

    public void BindModuleDependencies(ItemMods modules)
    {
        fuel = modules?.RequireSingleModById<Mod_Fuel>(ModText.Fuel);
        combustion = modules?.RequireSingleModById<Mod_Combustion>(Mod_Combustion.ModuleId);
        building = modules?.GetMod_ByID<Mod_Building>(ModText.Building);
    }

    #endregion

    #region 生命周期与交互

    protected override void OnLoad()
    {
        ValidateDependencies();
        IgnitionInventory.item = item;
        InventoryModuleDataPersistence.TryRestore(IgnitionInventory,
            InventoryModuleDataPersistence.TryRead(ModData), "火种");
        IgnitionInventory.InitData();
        IgnitionInventory.RuntimeAccessGuard = () => false;
        IgnitionInventory.Data.SetItemAcceptancePolicy((source, _) => CanUseItemForIgnition(source));
        IgnitionInventory.Data.Event_OnDataChanged += HandleIgnitionInventoryChanged;
        item.OnAct -= Act;
        item.OnAct += Act;
        combustion.Changed -= HandleStateChanged;
        combustion.Changed += HandleStateChanged;
    }

    protected override void OnSave()
    {
        var saved = new Inventory_ModuleData { ID = ModData.ID, Name = ModData.Name };
        InventoryModuleDataPersistence.Capture(saved, "火种", IgnitionInventory);
        InventoryModuleDataPersistence.Write(ModData, saved);
    }

    protected override void OnUnload()
    {
        if (item != null)
            item.OnAct -= Act;
        if (combustion != null)
            combustion.Changed -= HandleStateChanged;
        IgnitionInventory.Data.Event_OnDataChanged -= HandleIgnitionInventoryChanged;
        IgnitionInventory.UnbindRuntimeDataEvents();
        IgnitionInventory.RuntimeAccessGuard = () => false;
        IgnitionInventory.DefaultTarget_Inventory = null;
        Changed = null;
    }

    public override void ModUpdate(float deltaTime)
    {
        if (GameNetwork.HasStateAuthority)
            IgnitionInventory.ModUpdate(deltaTime);
    }

    public Inventory GetDefaultTargetInventory() => IgnitionInventory;

    private void HandleIgnitionInventoryChanged(ItemSlot _)
    {
        Save();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        Changed?.Invoke();
    }

    public override void Act()
    {
        if (!GameNetwork.HasStateAuthority || !item.InHand || item.Owner == null)
            return;
        if (building?.TryHandlePlacementAction() == true)
            return;
        if (CanOperate(item.Owner))
            OpenRequested?.Invoke(this, item.Owner);
    }

    public void OnInteractStart(Item actor)
    {
        if (CanOperate(actor))
            OpenRequested?.Invoke(this, actor);
    }

    public void OnInteractCancel(Item actor)
    {
    }

    public bool CanOperate(Item actor)
    {
        if (!IsRuntimeLoaded || !GameNetwork.HasStateAuthority || actor == null || actor.DestructionHandled ||
            item == null || item.DestructionHandled ||
            !(actor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp > 0f))
        {
            return false;
        }

        if (item.InHand)
            return item.Owner == actor;

        return item.Owner == null &&
               WorldTopologyRuntime.ShortestDelta(actor.transform.position, item.transform.position).sqrMagnitude <=
               reach * reach;
    }

    private void HandleStateChanged() => Changed?.Invoke();

    private void ValidateDependencies()
    {
        if (fuel == null)
            throw new InvalidOperationException($"{item?.name ?? name} 的燃料交互缺少 {ModText.Fuel}。");
        if (combustion == null)
            throw new InvalidOperationException($"{item?.name ?? name} 的燃料交互缺少 {Mod_Combustion.ModuleId}。");
    }

    #endregion

    #region 库存投料

    /// <summary>
    /// 投料区只补充燃料；火种存入独立库存并由点燃按钮消费。
    /// 每次只消费一件，并通过来源库存事务扣除。
    /// </summary>
    public bool TryFeedFromInventoryItem(ItemData source, Item actor, float maximumItemAmount)
    {
        if (!CanOperate(actor) || source == null || item?.itemData == null ||
            IsSameItemData(source, item.itemData) ||
            float.IsNaN(maximumItemAmount) || maximumItemAmount < 1f ||
            !InventoryContextResolver.TryResolveContainingInventory(actor, source, out Inventory inventory))
        {
            return false;
        }

        ItemSlot sourceSlot = inventory.Data.itemSlots.Find(slot => IsSameItemData(slot?.itemData, source));
        if (sourceSlot?.itemData?.Stack == null || sourceSlot.itemData.Stack.Amount < 1f)
            return false;

        bool taggedFuel = source.Tags?.ContainsTag(Tag.CombustionFuel) == true;
        FuelData sourceFuel = null;
        bool hasFuelData = taggedFuel && !IsSpecialIgnitionSource(source) &&
                           Mod_Fuel.TryResolveItemData(source, out sourceFuel);

        if (!hasFuelData || sourceFuel.Fuel.x <= FuelEpsilon)
        {
            ItemActionFeedback.Show(actor, "这个物品不能作为燃料。");
            return false;
        }

        float availableSpace = Mathf.Max(0f, MaxFuel - CurrentFuel);
        if (availableSpace <= FuelEpsilon)
        {
            ItemActionFeedback.Show(actor, "燃料已经满了。");
            return false;
        }

        if (!inventory.Data.TryConsumeFromSlot(sourceSlot, 1, out _))
            return false;

        fuel.AddFuel(sourceFuel.Fuel.x);
        fuel.Save();
        combustion.NotifyFuelChanged(notifyNetwork: true);
        ItemActionFeedback.Show(actor, "已补充燃料。");
        Changed?.Invoke();
        return true;
    }

    // 特殊火种消耗一份，高温物品仅提供点火且按点击时的真实温度判定。
    public bool TryIgniteFromStoredItem(Item actor)
    {
        if (!CanOperate(actor) || IsBurning || MachineInventory.IsBeingDragged(IgnitionInventory))
            return false;
        ItemSlot slot = IgnitionInventory.Data.GetItemSlot(0);
        ItemData source = slot.itemData;
        if (!CanUseItemForIgnition(source) || source.Stack.Amount < 1f)
            return false;
        if (!combustion.CanCombustNow())
        {
            ItemActionFeedback.Show(actor, "氧气不足 5%，无法点火。");
            return false;
        }

        bool specialIgnitionSource = IsSpecialIgnitionSource(source);
        float starterFuel = 0f;
        if (!fuel.HasFuel())
        {
            if (!specialIgnitionSource || !Mod_Fuel.TryResolveItemData(source, out FuelData sourceFuel) ||
                sourceFuel.Fuel.x <= FuelEpsilon)
                return false;
            starterFuel = Mathf.Min(Mathf.Max(0f, ignitionFuelValueOverride), sourceFuel.Fuel.x);
            if (starterFuel <= FuelEpsilon)
                return false;
        }
        if (specialIgnitionSource && !IgnitionInventory.Data.TryConsumeFromSlot(slot, 1, out _))
            return false;
        fuel.AddFuel(starterFuel);
        return combustion.Ignite();
    }

    public bool TryExtinguish(Item actor)
    {
        if (!CanOperate(actor) || !IsBurning)
            return false;
        combustion.Extinguish();
        return true;
    }

    private bool CanUseItemForIgnition(ItemData source) =>
        IsSpecialIgnitionSource(source) ||
        source?.MatterState?.Initialized == true &&
        float.IsFinite(source.MatterState.TemperatureCelsius) &&
        source.MatterState.TemperatureCelsius >= MinimumHotItemIgnitionTemperatureCelsius;

    private bool IsSpecialIgnitionSource(ItemData source)
    {
        if (source == null)
            return false;

        if (ignitionItemIds != null)
        {
            for (int i = 0; i < ignitionItemIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(ignitionItemIds[i]) &&
                    string.Equals(source.IDName, ignitionItemIds[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        if (source.Tags == null || ignitionTags == null)
            return false;

        for (int i = 0; i < ignitionTags.Length; i++)
        {
            string tag = ignitionTags[i];
            if (!string.IsNullOrWhiteSpace(tag) && source.Tags.Contains(tag))
                return true;
        }

        return false;
    }

    private static bool IsSameItemData(ItemData left, ItemData right) =>
        ReferenceEquals(left, right) ||
        (left != null && right != null && left.Guid != 0 && left.Guid == right.Guid);

    #endregion
}
