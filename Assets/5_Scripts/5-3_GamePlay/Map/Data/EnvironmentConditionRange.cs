
using UnityEngine;


[System.Serializable]
public class EnvironmentConditionRange
{
    [Tooltip("温度范围（x=最低温，y=最高温")]
    public Vector2 TemperatureRange = new Vector2(0, 1);

    [Tooltip("湿度范围（x=最低湿度，y=最高湿度）")]
    public Vector2 HumidityRange = new Vector2(0, 1);

    [Tooltip("降水范围（x=最小降水，y=最大降水）")]
    public Vector2 PrecipitationRange = new Vector2(0, 1);

    [Tooltip("固体比例（x=最低固体，y=最高固体）")]
    public Vector2 SolidityRange = new Vector2(0, 1);

    [Tooltip("高度范围（x=最低高度，y=最高高度）")]
    public Vector2 HightRange = new Vector2(0, 1);

    // 判断当前值是否在范围内
    public bool IsMatch(EnvironmentLayers layers, int x, int y)
    {
        if (layers == null || !layers.Contains(x, y))
            return false;

        float temperature = layers.Temperature[x, y];
        float humidity = layers.Humidity[x, y];
        float precipitation = layers.Precipitation[x, y];
        float hight = layers.Hight[x, y];
        float solidity = layers.Solidity[x, y];

        return TemperatureRange.x <= temperature && temperature <= TemperatureRange.y &&
               HumidityRange.x <= humidity && humidity <= HumidityRange.y &&
               PrecipitationRange.x <= precipitation && precipitation <= PrecipitationRange.y &&
               HightRange.x <= hight && hight <= HightRange.y &&
               SolidityRange.x <= solidity && solidity <= SolidityRange.y;
    }
    
    private void OnValidate()
    {
        // 确保范围值的有效性
        if (TemperatureRange.x > TemperatureRange.y)
            TemperatureRange.y = TemperatureRange.x;
        
        if (HumidityRange.x > HumidityRange.y)
            HumidityRange.y = HumidityRange.x;
            
        if (PrecipitationRange.x > PrecipitationRange.y)
            PrecipitationRange.y = PrecipitationRange.x;
            
        if (SolidityRange.x > SolidityRange.y)
            SolidityRange.y = SolidityRange.x;
            
        if (HightRange.x > HightRange.y)
            HightRange.y = HightRange.x;
            
        // 确保值在合理范围内
        TemperatureRange.x = Mathf.Clamp01(TemperatureRange.x);
        TemperatureRange.y = Mathf.Clamp01(TemperatureRange.y);
        
        HumidityRange.x = Mathf.Clamp01(HumidityRange.x);
        HumidityRange.y = Mathf.Clamp01(HumidityRange.y);
        
        PrecipitationRange.x = Mathf.Clamp01(PrecipitationRange.x);
        PrecipitationRange.y = Mathf.Clamp01(PrecipitationRange.y);
        
        SolidityRange.x = Mathf.Clamp01(SolidityRange.x);
        SolidityRange.y = Mathf.Clamp01(SolidityRange.y);
        
        HightRange.x = Mathf.Clamp01(HightRange.x);
        HightRange.y = Mathf.Clamp01(HightRange.y);
    }
}