using System;
using UnityEngine;

/// <summary>手持检测仪只发布本地面板请求，测量结果完全来自拥有者的实际星球。</summary>
public sealed class Mod_AtmosphereAnalyzer : Module, ILocalItemUsePresentation, IInstanceUI
{
    #region 道具生命周期与面板能力
    public const string ModuleId = "Mod_AtmosphereAnalyzer";
    public Ex_ModData Data = new();
    public static event Action<Mod_AtmosphereAnalyzer, Player> OpenRequested;
    public static event Action<Mod_AtmosphereAnalyzer, Player> CloseRequested;
    public static Func<Mod_AtmosphereAnalyzer, bool> PanelOpenProbe { get; set; }
    private AtmosphereState measuredAtmosphere;
    private AtmosphereMeasurementSnapshot measurement;

    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public bool IsPanelOpen => PanelOpenProbe?.Invoke(this) == true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPanelEvents()
    { OpenRequested = null; CloseRequested = null; PanelOpenProbe = null; }

    protected override void OnLoad()
    { item.OnAct -= Act; item.OnAct += Act; }

    protected override void OnSave() { }

    protected override void OnUnload()
    {
        if (item != null) item.OnAct -= Act;
        I_ClosePanel(); measuredAtmosphere = null; measurement = null;
    }

    public void PresentLocalUse() => Act();
    public override void Act() => I_TogglePanel();
    public void I_ShowPanel()
    {
        if (item?.Owner is Player player && CanMeasure(player)) OpenRequested?.Invoke(this, player);
    }
    public void I_ClosePanel() => CloseRequested?.Invoke(this, item?.Owner as Player);
    public void I_TogglePanel() { if (IsPanelOpen) I_ClosePanel(); else I_ShowPanel(); }

    public bool CanMeasure(Player actor) => IsRuntimeLoaded && item != null && !item.DestructionHandled &&
        item.InHand && actor != null && !actor.DestructionHandled && actor.IsLocalProfile && item.Owner == actor;
    #endregion

    #region 实时只读测量与版本缓存
    public AtmosphereMeasurementSnapshot ReadMeasurement(Player actor)
    {
        if (!CanMeasure(actor)) return AtmosphereMeasurementSnapshot.Unavailable;
        string worldKey = GameplayWorldRegistry.GetItemWorldKey(actor);
        if (!AtmosphereMeasurementService.TryResolvePlanetForWorld(worldKey,
                SaveDataMgr.Instance?.SaveData?.PlanetData_Dict, out PlanetData planet) || planet.Atmosphere == null)
        { measuredAtmosphere = null; return measurement = AtmosphereMeasurementSnapshot.Unavailable; }
        AtmosphereState atmosphere = planet.Atmosphere;
        string planetName = planet.RuntimePlanetName;
        // 库存未变时复用不可变快照，避免面板刷新反复创建成分列表。
        if (measurement != null && ReferenceEquals(measuredAtmosphere, atmosphere) &&
            measurement.Revision == atmosphere.Revision && measurement.WorldKey == worldKey &&
            measurement.PlanetName == planetName)
            return measurement;
        measuredAtmosphere = atmosphere;
        return measurement = AtmosphereMeasurementService.Capture(atmosphere, worldKey, planetName);
    }
    #endregion
}
