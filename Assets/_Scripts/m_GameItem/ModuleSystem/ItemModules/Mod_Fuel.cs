using MemoryPack;
using UnityEngine;

public class Mod_Fuel : Module
{

    public Ex_ModData_MemoryPackable ExData;
    public override ModuleData _Data { get => ExData; set => ExData = (Ex_ModData_MemoryPackable)value; }
    public FuelData Data = new FuelData();


    // 是否点燃
    public bool IsIgnited { get; private set; } = false;

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Fuel;
        }
    }

    public override void Load()
    {
        ExData.ReadData(ref Data);
    }

    public override void Save()
    {
        ExData.WriteData(Data);
    }

    /// <summary>
    /// 是否有燃料
    /// </summary>
    public bool HasFuel()
    {
        return Data.Fuel.x > 0f;
    }

    /// <summary>
    /// 添加燃料
    /// </summary>
    public void AddFuel(float amount)
    {
        Data.Fuel.x = Mathf.Min(Data.Fuel.x + amount, Data.Fuel.y);
    }

    /// <summary>
    /// 消耗燃料
    /// </summary>
    public bool ConsumeFuel(float amount)
    {
        if (Data.Fuel.x <= 0f) return false;
        Data.Fuel.x = Mathf.Max(Data.Fuel.x - amount, 0f);
        if (Data.Fuel.x <= 0f) IsIgnited = false; // 燃料耗尽自动熄灭
        return true;
    }

    /// <summary>
    /// 点燃
    /// </summary>
    public void Ignite()
    {
        if (HasFuel())
        {
            IsIgnited = true;
        }
    }

    /// <summary>
    /// 燃料剩余比例 (0~1)
    /// </summary>
    public float GetFuelRatio()
    {
        if (Data.Fuel.y <= 0) return 0f;
        return Mathf.Clamp01(Data.Fuel.x / Data.Fuel.y);
    }
}
[MemoryPackable]
[System.Serializable]
public partial class FuelData
{
    /// <summary>
    /// x = 当前燃料值, y = 最大燃料值
    /// </summary>
    /// 
    public Vector2 Fuel = new Vector2(100f, 100f);
    [Tooltip("燃烧时提供的最大温度")]
    public float MaxTemperature = 100f;
}
