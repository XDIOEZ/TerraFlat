
using UnityEngine;


[System.Serializable]
public class EnvironmentConditionRange
{
    [Tooltip("温度范围（x=最低温，y=最高温")]
    public Vector2 TemperatureRange = new Vector2(0, 1);

    [Tooltip("降水范围（x=最小降水，y=最大降水）")]
    public Vector2 PrecipitationRange = new Vector2(0, 1);

    [Tooltip("高度范围（x=最低高度，y=最高高度）")]
    public Vector2 HeightRange = new Vector2(0, 1);

    // 判断当前值是否在范围内
    public bool IsMatch(EnvironmentLayers layers, int x, int y)
    {
        if (layers == null || !layers.Contains(x, y))
            return false;

        return IsMatch(new EnvironmentSample(
            layers.Temperature[x, y],
            layers.TemperatureCelsius[x, y],
            layers.Precipitation[x, y],
            layers.Height[x, y]));
    }

    public bool IsMatch(EnvironmentSample sample)
    {
        return TemperatureRange.x <= sample.Temperature && sample.Temperature <= TemperatureRange.y &&
               PrecipitationRange.x <= sample.Precipitation && sample.Precipitation <= PrecipitationRange.y &&
               HeightRange.x <= sample.Height && sample.Height <= HeightRange.y;
    }

    public bool TryValidate(out string reason)
    {
        if (!IsFiniteRange(TemperatureRange) || !IsFiniteRange(PrecipitationRange) || !IsFiniteRange(HeightRange))
        {
            reason = "范围必须为 0~1 内的有限递增值";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool IsFiniteRange(Vector2 range)
    {
        return !float.IsNaN(range.x) && !float.IsInfinity(range.x) &&
               !float.IsNaN(range.y) && !float.IsInfinity(range.y) &&
               range.x >= 0f && range.y <= 1f && range.x <= range.y;
    }
    
    private void OnValidate()
    {
        // 确保范围值的有效性
        if (TemperatureRange.x > TemperatureRange.y)
            TemperatureRange.y = TemperatureRange.x;
        
        if (PrecipitationRange.x > PrecipitationRange.y)
            PrecipitationRange.y = PrecipitationRange.x;

        if (HeightRange.x > HeightRange.y)
            HeightRange.y = HeightRange.x;
            
        // 确保值在合理范围内
        TemperatureRange.x = Mathf.Clamp01(TemperatureRange.x);
        TemperatureRange.y = Mathf.Clamp01(TemperatureRange.y);
        
        PrecipitationRange.x = Mathf.Clamp01(PrecipitationRange.x);
        PrecipitationRange.y = Mathf.Clamp01(PrecipitationRange.y);

        HeightRange.x = Mathf.Clamp01(HeightRange.x);
        HeightRange.y = Mathf.Clamp01(HeightRange.y);
    }
}
