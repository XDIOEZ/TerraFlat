using System;
using FlatWorld.Networking;

/// <summary>大气供氧只读取真实大气和水淹接触，不向氧气状态模块注入环境规则。</summary>
public sealed class Mod_AtmosphericOxygenSupply : Module, IItemModuleDependencyBinder
{
    #region 显式能力与依赖
    public const string ModuleId = "大气供氧模块";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => .2f;
    public Ex_ModData_MemoryPackable ModData = new() { ID = ModuleId };
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? throw new ArgumentException("大气供氧模块数据类型错误。");
    }
    private Mod_Oxygen oxygen;
    private Mod_TileEffectReceiver tileContact;
    private Mod_Equipment equipment;

    public void BindModuleDependencies(ItemMods modules)
    {
        oxygen = modules.RequireSingleModById<Mod_Oxygen>(Mod_Oxygen.ModuleId);
        tileContact = modules.GetMod_ByID<Mod_TileEffectReceiver>(ModText.Mod_TileEffectReceiver);
        equipment = modules.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
    }
    #endregion

    #region 主动供给与生命周期
    protected override void OnLoad()
    {
        BindModuleDependencies(item.itemMods);
    }
    protected override void OnUnload()
    {
        oxygen = null;
        tileContact = null;
        equipment = null;
    }
    protected override void OnSave() { }
    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || item == null || oxygen == null || !oxygen.CanReceiveOxygen || deltaTime <= 0f) return;
        if (tileContact != null && tileContact.IsWaterBreathBlocked || equipment != null && equipment.BlocksAmbientOxygen) return;
        if (ItemEnvironmentSources.TryGet(item, out _))
        {
            oxygen.SupplyOxygen(ItemEnvironmentSources.SupplyAmbientOxygen(item, oxygen.GetOxygenSupplyAmount(deltaTime)));
            return;
        }
        if (AtmosphereService.TryGetForWorld(item.gameObject.scene.name, out AtmosphereState atmosphere) && AtmosphereService.CanBreathe(atmosphere))
            oxygen.SupplyOxygen(oxygen.GetOxygenSupplyAmount(deltaTime));
    }
    #endregion
}
