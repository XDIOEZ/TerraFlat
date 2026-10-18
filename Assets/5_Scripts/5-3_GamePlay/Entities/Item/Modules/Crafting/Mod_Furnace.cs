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

/// <summary>炉体内容配置；燃烧、库存和供热全部由 FurnaceLogic 管理。</summary>
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
    [Min(.1f), Tooltip("每份燃料留在槽位中吸收的秒数，完成后才扣除并增加燃料余量。")]
    public float fuelAbsorptionSeconds = 3f;
    [Min(0f), Tooltip("炉内材料升温倍率百分比；100为原速，0停止主动升温，冷却不变。")]
    public float heatingRatePercent = 100f;
    public bool acceptsMechanicalBellows;
    [Tooltip("是否自动吸收落在同一格的燃料掉落物到燃料槽。")]
    public bool absorbDroppedFuel;
    [Tooltip("是否让炉心使用真实炉温，并以独立目标温度温暖相邻地块。")]
    public bool publishCellTemperature;
    [Tooltip("燃烧时播放的空间循环 Cue；留空时不播放燃烧声。")]
    public string burningLoopCueId;
    [Tooltip("炉体周围的目标环境温度。")]
    public float neighborTargetTemperature = 40f;
    public List<FurnaceFuelByproductRule> fuelByproductRules = new();
    public List<string> ignitionItemIds = new() { "FireSeed" };
    public List<string> ignitionTags = new() { Tag.CombustionTinder };
    public float ignitionFuelValueOverride = 8f;
    public float ignitionMaxTemperatureOverride = 180f;
    public GameObject UI_Prefab;
    #endregion
}
