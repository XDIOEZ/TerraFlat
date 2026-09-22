using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 通用燃烧状态。燃料数量由 Mod_Fuel 独立保存，本模块只保存是否燃烧和世界时间基线。
/// 字段顺序保持与旧可燃光源状态一致，便于现有存档迁移。
/// </summary>
[Serializable, MemoryPackable]
public partial class CombustionState
{
    public bool IsBurning = true;
    public string ClockId;
    public float LastWorldTime;
    public bool HasWorldTimeBaseline;
}

/// <summary>
/// 通用燃烧模块：按世界时间消耗同一物品的 Mod_Fuel，并向其它组合模块广播燃烧状态。
/// 不负责光照、攻击、建筑或 UI；这些能力通过独立模块组合。
/// </summary>
public sealed class Mod_Combustion : Module, IItemModuleDependencyBinder
{
    #region 数据与依赖

    public const string ModuleId = "Mod_Combustion";

    public Ex_ModData_MemoryPackable ModData = new() { ID = ModuleId };
    public CombustionState Data = new();

    [Tooltip("没有保存状态的新实例是否默认点燃。")]
    public bool startBurning = true;

    [Tooltip("手持时是否持续燃烧。")]
    public bool burnWhileHeld = true;

    [Tooltip("作为世界实体存在时是否持续燃烧。")]
    public bool burnWhileInWorld = true;

    [Tooltip("收在普通库存中时是否持续燃烧。")]
    public bool burnWhileStored;

    [Min(0f), Tooltip("世界时间换算为燃料消耗量的倍率。")]
    public float fuelConsumptionMultiplier = 1f;

    public event Action Changed;

    private readonly List<ICombustionStateReceiver> receivers = new();
    private Mod_Fuel fuel;

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 1f;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ??
                         throw new ArgumentException("燃烧模块数据类型错误。");
    }

    public bool IsBurning => Data?.IsBurning == true && fuel?.HasFuel() == true;
    public bool IsActivelyBurning => IsBurning && CanCombustNow();

    public void BindModuleDependencies(ItemMods modules)
    {
        fuel = modules?.RequireSingleModById<Mod_Fuel>(ModText.Fuel);
        receivers.Clear();
        if (modules?.Mods == null)
            return;

        var seen = new HashSet<ICombustionStateReceiver>();
        foreach (Module module in modules.Mods.Values)
        {
            if (module == null || ReferenceEquals(module, this) ||
                module is not ICombustionStateReceiver receiver ||
                !seen.Add(receiver))
            {
                continue;
            }

            receivers.Add(receiver);
        }
    }

    #endregion

    #region 生命周期与燃烧

    public override void Load()
    {
        bool hasStoredState = ModData?.BitData != null && ModData.BitData.Length > 0;
        if (hasStoredState)
            ModData.ReadData(ref Data);
        else
            Data = new CombustionState { IsBurning = startBurning };

        Data ??= new CombustionState { IsBurning = startBurning };
        ValidateDependencies();

        item.OnInHandChanged -= HandleInHandChanged;
        item.OnInHandChanged += HandleInHandChanged;

        if (!fuel.HasFuel())
            Data.IsBurning = false;

        if (CanCombustNow())
            ResetWorldTimeBaseline();
        else
            ClearWorldTimeBaseline();

        ApplyCombustionState();
        Save();
    }

    public override void Save()
    {
        Data ??= new CombustionState { IsBurning = startBurning };
        ModData.WriteData(Data);
    }

    public override void Unload()
    {
        if (item != null)
            item.OnInHandChanged -= HandleInHandChanged;

        if (item?.Owner != null && !burnWhileStored)
            ClearWorldTimeBaseline();
        else if (GameNetwork.HasStateAuthority)
            ConsumeElapsedWorldTime(commit: false);

        fuel?.Save();
        Save();
        receivers.Clear();
        Changed = null;
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority)
        {
            ApplyCombustionState();
            return;
        }

        if (!CanCombustNow())
        {
            ClearWorldTimeBaseline();
            ApplyCombustionState();
            return;
        }

        if (!Data.IsBurning || !fuel.HasFuel())
        {
            if (!fuel.HasFuel())
                Data.IsBurning = false;
            ResetWorldTimeBaseline();
            ApplyCombustionState();
            return;
        }

        ConsumeElapsedWorldTime(commit: true);
    }

    /// <summary>点燃已有燃料的对象；不负责消耗火种或添加燃料。</summary>
    public bool Ignite()
    {
        if (!GameNetwork.HasStateAuthority || fuel == null || !fuel.HasFuel())
            return false;

        Data.IsBurning = true;
        ResetWorldTimeBaseline();
        Commit(notifyNetwork: true);
        return true;
    }

    /// <summary>熄灭对象但保留剩余燃料。</summary>
    public void Extinguish()
    {
        if (!GameNetwork.HasStateAuthority)
            return;

        Data.IsBurning = false;
        ClearWorldTimeBaseline();
        Commit(notifyNetwork: true);
    }

    /// <summary>燃料被其它组件修改后刷新燃烧输出与持久化表现。</summary>
    public void NotifyFuelChanged(bool notifyNetwork = true)
    {
        if (!fuel.HasFuel())
            Data.IsBurning = false;
        Commit(notifyNetwork);
    }

    /// <summary>当前承载形态是否允许燃烧。</summary>
    public bool CanCombustNow()
    {
        if (item == null || item.DestructionHandled)
            return false;

        if (item.InHand)
            return burnWhileHeld && item.Owner != null;
        if (item.Owner == null)
            return burnWhileInWorld;
        return burnWhileStored;
    }

    /// <summary>按当前场景解析后的世界绝对时间扣燃料，睡觉、加速和跳时都会被计入。</summary>
    private void ConsumeElapsedWorldTime(bool commit)
    {
        if (!TryGetWorldTime(out string clockId, out float currentTime))
            return;

        if (!Data.HasWorldTimeBaseline ||
            !string.Equals(Data.ClockId, clockId, StringComparison.Ordinal))
        {
            Data.ClockId = clockId;
            Data.LastWorldTime = currentTime;
            Data.HasWorldTimeBaseline = true;
            ApplyCombustionState();
            if (commit)
                Commit(notifyNetwork: false);
            return;
        }

        float elapsed = currentTime - Data.LastWorldTime;
        Data.LastWorldTime = currentTime;
        if (elapsed <= 0f)
        {
            if (elapsed < 0f && commit)
                Commit(notifyNetwork: false);
            ApplyCombustionState();
            return;
        }

        if (Data.IsBurning && fuel.HasFuel())
            fuel.ConsumeFuel(elapsed * Mathf.Max(0f, fuelConsumptionMultiplier));
        if (!fuel.HasFuel())
            Data.IsBurning = false;

        if (commit)
            Commit(notifyNetwork: true);
        else
            ApplyCombustionState();
    }

    private bool TryGetWorldTime(out string clockId, out float currentTime)
    {
        clockId = null;
        currentTime = 0f;
        DayTimeSystem timeSystem = DayTimeSystem.Instance;
        if (timeSystem == null)
            return false;

        string sceneName = SceneManager.GetActiveScene().name;
        if (!timeSystem.TryGetResolvedTimeData(sceneName, out string resolvedSceneName, out TimeData timeData) ||
            timeData == null)
        {
            return false;
        }

        clockId = resolvedSceneName;
        currentTime = timeData.GetTotalGameTime();
        return true;
    }

    private void HandleInHandChanged(bool inHand)
    {
        if (CanCombustNow())
            ResetWorldTimeBaseline();
        else
            ClearWorldTimeBaseline();

        ApplyCombustionState();
        Save();
    }

    private void ResetWorldTimeBaseline()
    {
        if (TryGetWorldTime(out string clockId, out float currentTime))
        {
            Data.ClockId = clockId;
            Data.LastWorldTime = currentTime;
            Data.HasWorldTimeBaseline = true;
            return;
        }

        ClearWorldTimeBaseline();
    }

    private void ClearWorldTimeBaseline()
    {
        Data.ClockId = null;
        Data.LastWorldTime = 0f;
        Data.HasWorldTimeBaseline = false;
    }

    private void ApplyCombustionState()
    {
        bool active = IsActivelyBurning;
        for (int i = 0; i < receivers.Count; i++)
            receivers[i]?.SetCombustionActive(active);
    }

    private void ValidateDependencies()
    {
        if (fuel == null)
            throw new InvalidOperationException($"{item?.name ?? name} 的燃烧模块缺少 {ModText.Fuel}。");
    }

    #endregion

    #region 状态提交

    private void Commit(bool notifyNetwork)
    {
        fuel?.Save();
        Save();
        ApplyCombustionState();
        RefreshContainingInventoryPresentation();
        item?.OnUIRefresh?.Invoke();
        if (notifyNetwork && item != null)
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        Changed?.Invoke();
    }

    private void RefreshContainingInventoryPresentation()
    {
        if (item?.Owner == null || item.itemData == null ||
            !InventoryContextResolver.TryResolveContainingInventory(item.Owner, item.itemData, out Inventory inventory))
        {
            return;
        }

        inventory.Data?.NotifyItemStateChanged(item.itemData);
    }

    #endregion
}
