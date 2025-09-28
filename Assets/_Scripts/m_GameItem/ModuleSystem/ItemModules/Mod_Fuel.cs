using MemoryPack;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class Mod_Fuel : Module
{
    public Ex_ModData_MemoryPackable ExData;
    public override ModuleData _Data { get => ExData; set => ExData = (Ex_ModData_MemoryPackable)value; }
    public FuelData Data = new FuelData();

    // �Ƿ��ȼ
    public bool IsIgnited { get; private set; } = false;
    
    // �ƹ��������
    [Tooltip("ȼ��ʱ����ĵƹ����")]
    public Light2D fuelLight;
    
    // �ƹ����ǿ��
    [Tooltip("�ƹ����ǿ��")]
    public float lightBaseIntensity = 1f;
    
    // ȼ�������ٶ�ϵ�������Ƶƹ�仯��
    [Tooltip("ȼ�������ٶ�ϵ��")]
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
        // ��ʼ��ʱ���ݵ�ȼ״̬���õƹ�
        fuelLight = item.GetComponentInChildren<Light2D>();
        UpdateLightState();

    }

    public override void Save()
    {
        ExData.WriteData(Data);
    }

    /// <summary>
    /// �Ƿ���ȼ��
    /// </summary>
    public bool HasFuel()
    {
        return Data.Fuel.x > 0f;
    }

    /// <summary>
    /// ���ȼ��
    /// </summary>
    public void AddFuel(float amount)
    {
        Data.Fuel.x = Mathf.Min(Data.Fuel.x + amount, Data.Fuel.y);
    }

    /// <summary>
    /// ����ȼ��
    /// </summary>
    public bool ConsumeFuel(float amount)
    {
        // ����ȼ���ٶ�ϵ������������
        float actualAmount = amount * burnSpeedMultiplier;
        Data.Fuel.x = Mathf.Max(Data.Fuel.x - actualAmount, 0f);
        
        if (Data.Fuel.x <= 0.01f) 
        {
            SetIgnited(false); // ȼ�Ϻľ��Զ�Ϩ��
        }
        else
        {
            // ���µƹ�ǿ�ȣ�����ʣ��ȼ�ϱ�����
            UpdateLightIntensity();
        }
        
        return true;
    }

    /// <summary>
    /// ��ȼ
    /// </summary>
    public void Ignite()
    {
        if (HasFuel())
        {
            SetIgnited(true);
        }
    }

    /// <summary>
    /// Ϩ��
    /// </summary>
    public void Extinguish()
    {
        SetIgnited(false);
    }

    /// <summary>
    /// ���õ�ȼ״̬
    /// </summary>
    /// <param name="ignited">�Ƿ��ȼ</param>
    public void SetIgnited(bool ignited)
    {
        IsIgnited = ignited;
        UpdateLightState();
    }

    /// <summary>
    /// �л���ȼ״̬
    /// </summary>
    public void ToggleIgnited()
    {
        SetIgnited(!IsIgnited);
    }

    /// <summary>
    /// ��ȡ��ȼ״̬
    /// </summary>
    public bool GetIgnitedState()
    {
        return IsIgnited;
    }

    /// <summary>
    /// ȼ��ʣ����� (0~1)
    /// </summary>
    public float GetFuelRatio()
    {
        if (Data.Fuel.y <= 0) return 0f;
        return Mathf.Clamp01(Data.Fuel.x / Data.Fuel.y);
    }

    /// <summary>
    /// ���µƹ�״̬
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
    /// ���µƹ�ǿ��
    /// </summary>
    private void UpdateLightIntensity()
    {
        if (fuelLight != null && IsIgnited)
        {
            // �ƹ�ǿ�ȸ���ȼ��ʣ�������ȼ���ٶȵ���
            float fuelRatio = GetFuelRatio();
            float intensity = lightBaseIntensity * fuelRatio;
            
            // �������һЩ�������ʹ�ƹ����Ȼ
            intensity *= Random.Range(0.9f, 1.1f);
            
            fuelLight.intensity = intensity;
        }
    }

    /// <summary>
    /// ����ȼ���ٶ�ϵ��
    /// </summary>
    /// <param name="multiplier">�ٶ�ϵ��</param>
    public void SetBurnSpeedMultiplier(float multiplier)
    {
        burnSpeedMultiplier = Mathf.Max(multiplier, 0.1f); // ������Сֵ�������
    }

    /// <summary>
    /// ��ȡȼ���ٶ�ϵ��
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
    /// x = ��ǰȼ��ֵ, y = ���ȼ��ֵ
    /// </summary>
    public Vector2 Fuel = new Vector2(100f, 100f);
    [Tooltip("ȼ��ʱ�ṩ������¶�")]
    public float MaxTemperature = 100f;
}