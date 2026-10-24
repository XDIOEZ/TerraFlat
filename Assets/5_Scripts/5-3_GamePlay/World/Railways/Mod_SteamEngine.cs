using System;
using FlatWorld.Networking;
using UnityEngine;

[Serializable]
public sealed class SteamEngineState
{
    #region 蒸汽机保存状态
    public int Version = 1;
    public bool Enabled = true;
    public int OutputLevel = 3;
    public float StoredFuelSeconds;
    #endregion
}

/// <summary>手持模块仅编辑自身配置，库存中的同一数据由动力提供方执行。</summary>
public sealed class Mod_SteamEngine : Module
{
    #region 蒸汽机操作入口
    public const string ModuleId = "Mod_SteamEngine";
    public Ex_ModData Data = new();
    [Min(.01f)] public float MaximumPower = 1f;
    [Min(.0001f)] public float WaterPerSecond = .02f;
    [Min(.01f)] public float FuelSecondsPerUnit = 60f;
    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public SteamEngineState State => SteamEnginePowerProvider.ReadState(Data);
    public static event Action<Mod_SteamEngine, Player> OpenRequested;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvents() => OpenRequested = null;
    protected override void OnLoad()
    {
        _ = State;
        if (!SteamEngineRules.Positive(MaximumPower) || !SteamEngineRules.Positive(WaterPerSecond) || !SteamEngineRules.Positive(FuelSecondsPerUnit))
            throw new InvalidOperationException("蒸汽机功率、水耗和燃料换算必须为有限正数。");
        item.OnAct += Act;
    }
    protected override void OnUnload() { if (item != null) item.OnAct -= Act; }
    protected override void OnSave() => SteamEnginePowerProvider.WriteState(Data, State);
    private void OnDestroy() => Unload();
    public override void Act()
    {
        if (item?.InHand == true && item.Owner is Player player && player.IsLocalProfile)
            OpenRequested?.Invoke(this, player);
    }
    public bool Configure(bool enabled, int outputLevel)
    {
        if (!GameNetwork.HasStateAuthority || item?.InHand != true || item.Owner is not Player player || !player.IsLocalProfile || outputLevel < 1 || outputLevel > 3) return false;
        SteamEngineState state = State; state.Enabled = enabled; state.OutputLevel = outputLevel;
        SteamEnginePowerProvider.WriteState(Data, state);
        if (InventoryContextResolver.TryResolveContainingInventory(player, item.itemData, out Inventory inventory))
        { inventory.Data.NotifyItemStateChanged(item.itemData); inventory.Save(); }
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(player);
        return true;
    }
    #endregion
}
