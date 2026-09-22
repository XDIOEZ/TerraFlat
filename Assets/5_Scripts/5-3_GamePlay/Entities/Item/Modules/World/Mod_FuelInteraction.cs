using System;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 通用燃料交互模块：提供面板入口、库存投料和火种点燃。
/// 只协调燃料、燃烧和可选建筑模块，不包含任何具体物品语义。
/// </summary>
public sealed class Mod_FuelInteraction : Module, IInteractable, IItemModuleDependencyBinder
{
    #region 数据与依赖

    public const string ModuleId = "Mod_FuelInteraction";
    private const float FuelEpsilon = 0.01f;

    public Ex_ModData_MemoryPackable ModData = new() { ID = ModuleId };

    [Tooltip("世界中允许玩家打开燃料面板的交互距离。")]
    public float reach = 2f;

    [Tooltip("可以重新点燃对象的物品 ID。")]
    public string[] ignitionItemIds = { "FireSeed" };

    [Tooltip("可以重新点燃对象的物品 Tag。")]
    public string[] ignitionTags = { "火种" };

    [Tooltip("燃料为零时，火种最多提供多少燃料用于起燃。")]
    public float ignitionFuelValueOverride = 8f;

    public static event Action<Mod_FuelInteraction, Item> OpenRequested;
    public event Action Changed;

    private Mod_Fuel fuel;
    private Mod_Combustion combustion;
    private Mod_Building building;

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ??
                         throw new ArgumentException("燃料交互模块数据类型错误。");
    }

    public bool IsBurning => combustion?.IsBurning == true;
    public float CurrentFuel => fuel?.Data?.Fuel.x ?? 0f;
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

    public override void Load()
    {
        ValidateDependencies();
        item.OnAct -= Act;
        item.OnAct += Act;
        combustion.Changed -= HandleStateChanged;
        combustion.Changed += HandleStateChanged;
    }

    public override void Save()
    {
    }

    public override void Unload()
    {
        if (item != null)
            item.OnAct -= Act;
        if (combustion != null)
            combustion.Changed -= HandleStateChanged;
        Changed = null;
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
        if (!GameNetwork.HasStateAuthority || actor == null || actor.DestructionHandled ||
            item == null || item.DestructionHandled ||
            !(actor.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp)?.Hp > 0f))
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
    /// 熄灭时火种优先负责点火；其它带 Mod_Fuel 的物品按自身燃料值补充燃料。
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

        bool ignitionSource = IsIgnitionSource(source);
        bool hasFuelData = TryResolveFuelData(source, out FuelData sourceFuel);

        if (!combustion.IsBurning && ignitionSource)
        {
            float starterFuel = 0f;
            if (!fuel.HasFuel())
            {
                if (!hasFuelData || sourceFuel.Fuel.x <= FuelEpsilon)
                {
                    ItemActionFeedback.Show(actor, "没有燃料，火种无法点燃。");
                    return false;
                }

                starterFuel = Mathf.Min(
                    Mathf.Max(0f, ignitionFuelValueOverride),
                    Mathf.Max(0f, sourceFuel.Fuel.x));
                if (starterFuel <= FuelEpsilon)
                    return false;
            }

            if (!inventory.Data.TryConsumeFromSlot(sourceSlot, 1, out _))
                return false;

            if (starterFuel > FuelEpsilon)
                fuel.AddFuel(starterFuel);
            fuel.Save();
            if (!combustion.Ignite())
                return false;

            ItemActionFeedback.Show(actor, "已重新点燃。");
            Changed?.Invoke();
            return true;
        }

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

    private bool IsIgnitionSource(ItemData source)
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

    /// <summary>库存冷数据没有运行时 Module 时，从保存态或当前物品定义读取通用 FuelData。</summary>
    private static bool TryResolveFuelData(ItemData source, out FuelData fuelData)
    {
        fuelData = null;
        if (source?.ModuleDataDic == null)
            return false;

        string stableModuleName = null;
        Ex_ModData_MemoryPackable storage = null;
        foreach (var pair in source.ModuleDataDic)
        {
            if (pair.Value is not Ex_ModData_MemoryPackable candidate ||
                !string.Equals(candidate.ID, ModText.Fuel, StringComparison.Ordinal))
            {
                continue;
            }

            stableModuleName = pair.Key;
            storage = candidate;
            break;
        }

        if (storage == null)
            return false;

        if (storage.BitData != null && storage.BitData.Length > 0)
        {
            fuelData = new FuelData();
            storage.ReadData(ref fuelData);
            return fuelData != null;
        }

        if (GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(source.IDName, out RuntimeItemDefinition definition))
        {
            return false;
        }

        string[] parameterKeys =
        {
            stableModuleName,
            storage.Name,
            ModText.Fuel
        };
        for (int i = 0; i < parameterKeys.Length; i++)
        {
            string key = parameterKeys[i];
            if (string.IsNullOrWhiteSpace(key) ||
                !definition.TryGetModuleParameters(key, out string json) ||
                string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            JObject parameters = JObject.Parse(json);
            JToken dataToken = parameters["Data"];
            if (dataToken == null)
                continue;

            fuelData = dataToken.ToObject<FuelData>();
            if (fuelData != null)
                return true;
        }

        return false;
    }

    private static bool IsSameItemData(ItemData left, ItemData right) =>
        ReferenceEquals(left, right) ||
        (left != null && right != null && left.Guid != 0 && left.Guid == right.Guid);

    #endregion
}
