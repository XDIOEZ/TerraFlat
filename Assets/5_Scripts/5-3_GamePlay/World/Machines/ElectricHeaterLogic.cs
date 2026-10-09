using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>电热器按电网实际供电向现有环境温度场发布热源。</summary>
public sealed class ElectricHeaterLogic : MachineLogic
{
    #region 供电与环境热源
    private ElectricHeaterState state;
    private TemperatureMgr temperatureManager;
    private ElectricHeaterDefinition Heating => Entity.Definition.Heating;
    public override float TickInterval => .1f;
    public override bool CanAct => false;
    public override string ActionLabel => "通电自动加热";
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab(Heating.PanelId);
    public override string Status => FlatWorldLocalizationService.GetUiFormat(
        "{0}\n实际供电 {1:0.#}/{2:0.#} W\n加热半径 {3:0.#} 格 · 中心升温 {4:0.#}℃",
        FlatWorldLocalizationService.GetUiText(state.SuppliedWatts > 0 ? "正在加热" : "没有电"),
        state.SuppliedWatts, Entity.Definition.Electrical.PowerWatts, Heating.Radius, EffectiveOffset);
    private float EffectiveOffset => Heating.CenterTemperatureOffset *
        Mathf.Clamp01(state.SuppliedWatts / Entity.Definition.Electrical.PowerWatts);

    public ElectricHeaterLogic(MachineEntity entity) : base(entity)
    {
        state = MachinePersistence.Read<ElectricHeaterState>(entity.Snapshot, "electric-heater") ?? new ElectricHeaterState();
        if (GameNetwork.HasStateAuthority) state.SuppliedWatts = 0;
        PublishTemperature();
    }

    public override void Tick(float seconds)
    {
        float previous = state.SuppliedWatts;
        state.SuppliedWatts = Mathf.Clamp(Entity.ElectricalSuppliedWatts, 0, Entity.Definition.Electrical.PowerWatts);
        PublishTemperature();
        if (previous != state.SuppliedWatts) NotifyChanged();
    }

    private void PublishTemperature()
    {
        TemperatureMgr current = TemperatureMgr.Instance;
        if (temperatureManager != current)
        {
            if (temperatureManager != null) temperatureManager.RemoveLocalTemperatureSource(this);
            temperatureManager = current;
        }
        if (temperatureManager == null) return;
        if (EffectiveOffset <= 0) temperatureManager.RemoveLocalTemperatureSource(this);
        else temperatureManager.SetLocalTemperatureSource(this, Entity.Position, Heating.Radius, EffectiveOffset);
    }

    public override bool Execute(string operation, string argument, Player actor) => operation == "begin-interaction";
    public override void Capture() => MachinePersistence.Write(Entity.Snapshot, "electric-heater", state);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        ElectricHeaterState incoming = MachinePersistence.Read<ElectricHeaterState>(snapshot, "electric-heater");
        if (incoming == null) return false;
        state = incoming;
        PublishTemperature();
        NotifyRemoteChanged();
        return true;
    }

    public override void Dispose()
    {
        if (temperatureManager != null) temperatureManager.RemoveLocalTemperatureSource(this);
        temperatureManager = null;
        // 休眠快照也撤销供热，远端不能继续保留已停止的热源。
        if (GameNetwork.HasStateAuthority && state.SuppliedWatts > 0)
        {
            state.SuppliedWatts = 0;
            Capture();
            MachineWorld.StateChanged(Entity);
        }
        base.Dispose();
    }
    #endregion
}

[Serializable]
public sealed class ElectricHeaterDefinition
{
    #region 加热配置
    public float Radius = 3;
    public float CenterTemperatureOffset = 60;
    public string PanelId = "UI_Mechanical";
    public void Validate(string id, ElectricalDefinition electrical)
    {
        if (!MachineDefinition.Positive(Radius) || Radius > 64 ||
            !MachineDefinition.Positive(CenterTemperatureOffset) || string.IsNullOrWhiteSpace(PanelId) ||
            electrical?.IsConsumer != true || !MachineDefinition.Positive(electrical.PowerWatts))
            throw new ArgumentException("电热器必须声明有效供电与加热配置：" + id);
    }
    #endregion
}

[Serializable, MemoryPackable]
public partial class ElectricHeaterState
{
    #region 同步状态
    public float SuppliedWatts;
    #endregion
}
