using MemoryPack;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class Mod_Fuel : Module
{
    public Ex_ModData_MemoryPackable ExData;
    public override ModuleData _Data { get => ExData; set => ExData = (Ex_ModData_MemoryPackable)value; }
    public FuelData Data = new FuelData();

    // 是否点燃
    public bool IsIgnited { get; private set; } = false;
    
    // 灯光组件引用
    [Tooltip("燃烧时激活的灯光组件")]
    public Light2D fuelLight;
    
    // 灯光基础强度
    [Tooltip("灯光基础强度")]
    public float lightBaseIntensity = 1f;
    
    // 燃烧消耗速度系数（控制灯光变化）
    [Tooltip("燃烧消耗速度系数")]
    public float burnSpeedMultiplier = 1f;

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
        // 初始化时根据点燃状态设置灯光
        fuelLight = item.GetComponentInChildren<Light2D>();
        UpdateLightState();

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
        // 根据燃烧速度系数调整消耗量
        float actualAmount = amount * burnSpeedMultiplier;
        Data.Fuel.x = Mathf.Max(Data.Fuel.x - actualAmount, 0f);
        
        if (Data.Fuel.x <= 0.01f) 
        {
            SetIgnited(false); // 燃料耗尽自动熄灭
        }
        else
        {
            // 更新灯光强度（根据剩余燃料比例）
            UpdateLightIntensity();
        }
        
        return true;
    }

    /// <summary>
    /// 点燃
    /// </summary>
    public void Ignite()
    {
        if (HasFuel())
        {
            SetIgnited(true);
        }
    }

    /// <summary>
    /// 熄灭
    /// </summary>
    public void Extinguish()
    {
        SetIgnited(false);
    }

    /// <summary>
    /// 设置点燃状态
    /// </summary>
    /// <param name="ignited">是否点燃</param>
    public void SetIgnited(bool ignited)
    {
        IsIgnited = ignited;
        UpdateLightState();
    }

    /// <summary>
    /// 切换点燃状态
    /// </summary>
    public void ToggleIgnited()
    {
        SetIgnited(!IsIgnited);
    }

    /// <summary>
    /// 获取点燃状态
    /// </summary>
    public bool GetIgnitedState()
    {
        return IsIgnited;
    }

    /// <summary>
    /// 燃料剩余比例 (0~1)
    /// </summary>
    public float GetFuelRatio()
    {
        if (Data.Fuel.y <= 0) return 0f;
        return Mathf.Clamp01(Data.Fuel.x / Data.Fuel.y);
    }

    /// <summary>
    /// 更新灯光状态
    /// </summary>
    private void UpdateLightState()
    {
        if (fuelLight != null)
        {
            fuelLight.enabled = IsIgnited && HasFuel();
            if (fuelLight.enabled)
            {
                UpdateLightIntensity();
            }
        }
    }

    /// <summary>
    /// 更新灯光强度
    /// </summary>
    private void UpdateLightIntensity()
    {
        if (fuelLight != null && IsIgnited)
        {
            // 灯光强度根据燃料剩余比例和燃烧速度调整
            float fuelRatio = GetFuelRatio();
            float intensity = lightBaseIntensity * fuelRatio;
            
            // 可以添加一些随机波动使灯光更自然
            intensity *= Random.Range(0.9f, 1.1f);
            
            fuelLight.intensity = intensity;
        }
    }

    /// <summary>
    /// 设置燃烧速度系数
    /// </summary>
    /// <param name="multiplier">速度系数</param>
    public void SetBurnSpeedMultiplier(float multiplier)
    {
        burnSpeedMultiplier = Mathf.Max(multiplier, 0.1f); // 限制最小值避免除零
    }

    /// <summary>
    /// 获取燃烧速度系数
    /// </summary>
    public float GetBurnSpeedMultiplier()
    {
        return burnSpeedMultiplier;
    }
}

[MemoryPackable]
[System.Serializable]
public partial class FuelData
{
    /// <summary>
    /// x = 当前燃料值, y = 最大燃料值
    /// </summary>
    public Vector2 Fuel = new Vector2(100f, 100f);
    [Tooltip("燃烧时提供的最大温度")]
    public float MaxTemperature = 100f;
}