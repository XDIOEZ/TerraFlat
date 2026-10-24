using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>电灯只消费电网已经结算的真实供电，灯光代理不参与耗电。</summary>
public sealed class ElectricLightLogic : MachineLogic
{
    #region 供电状态与同步
    private ElectricLightState state;
    public float PowerFraction => Mathf.Clamp01(state.SuppliedWatts / Entity.Definition.Electrical.PowerWatts);
    public override float TickInterval => .1f;
    public override bool CanAct => false;
    public override string ActionLabel => "通电自动照明";
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab(Entity.Definition.Lighting.PanelId);
    public override string Status => FlatWorldLocalizationService.GetUiFormat(
        "{0}\n实际供电 {1:0.#}/{2:0.#} W\n照明半径 {3:0.#} 格",
        FlatWorldLocalizationService.GetUiText(PowerFraction > 0f ? "正在照明" : "没有电"),
        state.SuppliedWatts, Entity.Definition.Electrical.PowerWatts, Entity.Definition.Lighting.Radius);

    public ElectricLightLogic(MachineEntity entity) : base(entity)
    {
        state = MachinePersistence.Read<ElectricLightState>(entity.Snapshot, "electric-light") ?? new ElectricLightState();
        if (GameNetwork.HasStateAuthority) state.SuppliedWatts = 0f;
    }

    public override void Tick(float seconds)
    {
        float supplied = Mathf.Clamp(Entity.ElectricalSuppliedWatts, 0f, Entity.Definition.Electrical.PowerWatts);
        if (state.SuppliedWatts == supplied) return;
        state.SuppliedWatts = supplied;
        NotifyChanged(visual: true);
    }

    public override bool Execute(string operation, string argument, Player actor) => operation == "begin-interaction";
    public override void Capture() => MachinePersistence.Write(Entity.Snapshot, "electric-light", state);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        ElectricLightState incoming = MachinePersistence.Read<ElectricLightState>(snapshot, "electric-light");
        if (incoming == null) return false;
        state = incoming;
        NotifyRemoteChanged();
        return true;
    }

    /// <summary>冷表现读取同一供电快照，普通客户端不另算一张电网。</summary>
    public static float GetPowerFraction(MachineEntity entity)
    {
        if (entity?.Definition.Lighting == null) return 0f;
        if (entity.Logic is ElectricLightLogic light) return light.PowerFraction;
        ElectricLightState saved = MachinePersistence.Read<ElectricLightState>(entity.Snapshot, "electric-light");
        return saved == null ? 0f : Mathf.Clamp01(saved.SuppliedWatts / entity.Definition.Electrical.PowerWatts);
    }

    public override void Dispose()
    {
        // 休眠和拆除立即熄灯，冷快照不能继续冒充正在供电。
        if (GameNetwork.HasStateAuthority && state.SuppliedWatts > 0f)
        {
            state.SuppliedWatts = 0f;
            Capture();
            NotifyChanged(visual: true);
        }
        base.Dispose();
    }
    #endregion
}

[MemoryPack.MemoryPackable]
[Serializable]
public sealed partial class MachineLightDefinition
{
    #region 通用机器照明配置
    public float Radius = 16f;
    public float InnerRadius = .1f;
    public float Intensity = 1f;
    public Color Color = new(1f, .9f, .7f, 1f);
    public Vector3 LocalPosition = new(0f, .3f, 0f);
    public string LitSpriteState = "powered";
    public bool EmissiveSprite = true;
    public float EmissiveStartLocalY = .14f;
    public string PanelId = "UI_Mechanical";
    public void Validate(string id, ElectricalDefinition electrical)
    {
        if (!MachineDefinition.Positive(Radius) || Radius > 64f ||
            !MachineDefinition.NonNegative(InnerRadius) || InnerRadius > Radius ||
            !MachineDefinition.Positive(Intensity) ||
            !MachineDefinition.NonNegative(Color.r) || !MachineDefinition.NonNegative(Color.g) ||
            !MachineDefinition.NonNegative(Color.b) || !MachineDefinition.NonNegative(Color.a) || Color.a > 1f ||
            !MachineDefinition.Finite(LocalPosition.x) || !MachineDefinition.Finite(LocalPosition.y) ||
            !MachineDefinition.Finite(LocalPosition.z) || !MachineDefinition.Finite(EmissiveStartLocalY) ||
            string.IsNullOrWhiteSpace(LitSpriteState) || string.IsNullOrWhiteSpace(PanelId) ||
            electrical?.IsConsumer != true || !MachineDefinition.Positive(electrical.PowerWatts))
            throw new ArgumentException("机器照明必须声明有效供电与光照配置：" + id);
    }
    #endregion
}

[Serializable, MemoryPackable]
public partial class ElectricLightState
{
    #region 同步状态
    public float SuppliedWatts;
    #endregion
}
