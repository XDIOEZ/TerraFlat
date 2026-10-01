using System.Collections.Generic;
using FlatWorld.Localization;
using UnityEngine;

internal static class FurnaceTemperatureFeedback
{
    #region 显示反馈
    public static string GetDisplayedTemperature(float temperature)
        => FlatWorldLocalizationService.GetUiFormat("当前炉温 {0}°C", Mathf.FloorToInt(Mathf.Max(0f, temperature) / 100f) * 100);
    #endregion
}

/// <summary>炉体内容配置；燃烧、库存和熔炼全部由 FurnaceLogic 管理。</summary>
public class Mod_Furnace : Mod_MachineAuthoring
{
    #region 炉体配置
    public Ex_ModData_MemoryPackable ModSaveData = new();
    public override ModuleData _Data { get => ModSaveData; set => ModSaveData = (Ex_ModData_MemoryPackable)value; }
    public ModSmeltingData Data = new();
    [SerializeReference] public List<string> RawData = new();
    public Inventory InputInventory;
    public Inventory OutputInventory;
    public Inventory FuelInventory;
    public bool acceptsMechanicalBellows;
    public List<FurnaceFuelByproductRule> fuelByproductRules = new();
    public List<string> ignitionItemIds = new() { "FireSeed" };
    public List<string> ignitionTags = new() { Tag.CombustionTinder };
    public float ignitionFuelValueOverride = 8f;
    public float ignitionMaxTemperatureOverride = 180f;
    public GameObject UI_Prefab;
    #endregion
}
