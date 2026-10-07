using MemoryPack;
using Newtonsoft.Json.Linq;
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

    [Tooltip("手持该燃料时自动点亮，离手后自动熄灭")]
    public bool igniteWhileHeld = false;

    private void OnValidate()
    {
        _Data.ID = ModText.Fuel;
    }

    protected override void OnLoad()
    {
        ExData.ReadData(ref Data);
        fuelLight ??= item.GetComponentInChildren<Light2D>(true);

        item.OnInHandChanged -= HandleInHandChanged;
        if (igniteWhileHeld)
        {
            item.OnInHandChanged += HandleInHandChanged;
            SetIgnited(item.InHand && HasFuel());
        }
        else
        {
            UpdateLightState();
        }
    }

    protected override void OnSave()
    {
        ExData.WriteData(Data);
    }

    private void OnDestroy()
    {
        if (item != null)
            item.OnInHandChanged -= HandleInHandChanged;
    }

    private void HandleInHandChanged(bool inHand)
    {
        if (igniteWhileHeld)
            SetIgnited(inHand && HasFuel());
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
        if (amount <= 0f) return;
        // 完整保留最后一份燃料的热值，超过显示容量的部分作为隐藏储备继续燃烧。
        Data.Fuel.x += amount;
    }

    /// <summary>
    /// 消耗燃料
    /// </summary>
    public bool ConsumeFuel(float amount)
    {
        // 根据燃烧速度系数调整消耗量
        float actualAmount =
            amount *
            burnSpeedMultiplier *
            GameDifficultyService.Current.Production.FuelConsumptionMultiplier;
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

    #region 冷数据解析
    /// <summary>库存里的 ItemData 没有实例化 Module 时，从保存态或当前物品定义解析燃料数据。</summary>
    public static bool TryResolveItemData(ItemData source, out FuelData fuelData)
    {
        fuelData = null;
        if (source?.ModuleDataDic == null)
            return false;

        string stableModuleName = null;
        Ex_ModData_MemoryPackable storage = null;
        if (source.ModuleDataDic.TryGetValue(ModText.Fuel, out ModuleData direct) &&
            direct is Ex_ModData_MemoryPackable directStorage &&
            string.Equals(directStorage.ID, ModText.Fuel, System.StringComparison.Ordinal))
        {
            stableModuleName = ModText.Fuel;
            storage = directStorage;
        }
        else
        {
            foreach (var pair in source.ModuleDataDic)
            {
                if (pair.Value is not Ex_ModData_MemoryPackable candidate ||
                    !string.Equals(candidate.ID, ModText.Fuel, System.StringComparison.Ordinal))
                {
                    continue;
                }

                stableModuleName = pair.Key;
                storage = candidate;
                break;
            }
        }

        if (storage == null)
            return false;

        if (storage.BitData != null && storage.BitData.Length > 0)
        {
            fuelData = new FuelData();
            storage.ReadData(ref fuelData);
            return fuelData != null;
        }

        GameRes gameRes = GameRes.ExistingInstance;
        if (gameRes == null ||
            !gameRes.TryGetItemDefinition(source.IDName, out RuntimeItemDefinition definition))
        {
            return false;
        }

        string[] parameterKeys =
        {
            stableModuleName,
            storage.Name,
            ModText.Fuel
        };
        for (int i = 0; i < parameterKeys.Length; i++)
        {
            string key = parameterKeys[i];
            if (string.IsNullOrWhiteSpace(key) ||
                !definition.TryGetModuleParameters(key, out string json) ||
                string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            JObject parameters = JObject.Parse(json);
            JToken dataToken = parameters["Data"];
            if (dataToken == null)
                continue;

            fuelData = dataToken.ToObject<FuelData>();
            if (fuelData != null)
                return true;
        }

        return false;
    }
    #endregion
}

[MemoryPackable]
[System.Serializable]
public partial class FuelData
{
    /// <summary>
    /// x = 实际燃料值（允许高于显示容量）, y = 显示容量/自动补充阈值
    /// </summary>
    public Vector2 Fuel = new Vector2(100f, 100f);
    [Tooltip("燃烧时提供的最大温度")]
    public float MaxTemperature = 100f;
}
