using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TemperatureMappingPoint
{
    [Range(0f, 1f)]
    public float noise01 = 0.5f; // 归一化噪声值
    public float celsius = 16f; // 对应摄氏温度
}

[CreateAssetMenu(fileName = "TemperatureMappingProfile", menuName = "ScriptObjects/Map/Temperature Mapping Profile")]
public class TemperatureMappingProfile : ScriptableObject
{
    [Header("默认线性映射（当映射点为空时使用）")]
    public Vector2 fallbackRangeCelsius = new Vector2(-10f, 16f);

    [Header("温度映射表（按 noise01 升序）")]
    public List<TemperatureMappingPoint> mappingPoints = new List<TemperatureMappingPoint>
    {
        new TemperatureMappingPoint { noise01 = 0f, celsius = -10f },
        new TemperatureMappingPoint { noise01 = 0.5f, celsius = 16f },
        new TemperatureMappingPoint { noise01 = 1f, celsius = 35f }
    };

    public float Evaluate(float normalizedValue)
    {
        float t = Mathf.Clamp01(normalizedValue);

        if (mappingPoints == null || mappingPoints.Count == 0)
        {
            return Mathf.Lerp(fallbackRangeCelsius.x, fallbackRangeCelsius.y, t);
        }

        if (mappingPoints.Count == 1)
        {
            return mappingPoints[0].celsius;
        }

        TemperatureMappingPoint first = mappingPoints[0];
        TemperatureMappingPoint last = mappingPoints[mappingPoints.Count - 1];

        if (t <= first.noise01)
            return first.celsius;

        if (t >= last.noise01)
            return last.celsius;

        for (int i = 1; i < mappingPoints.Count; i++)
        {
            TemperatureMappingPoint right = mappingPoints[i];
            if (t > right.noise01)
                continue;

            TemperatureMappingPoint left = mappingPoints[i - 1];
            float segment = Mathf.InverseLerp(left.noise01, right.noise01, t);
            return Mathf.Lerp(left.celsius, right.celsius, segment);
        }

        return last.celsius;
    }

    private void OnValidate()
    {
        if (fallbackRangeCelsius.x > fallbackRangeCelsius.y)
        {
            fallbackRangeCelsius.y = fallbackRangeCelsius.x;
        }

        if (mappingPoints == null)
            return;

        for (int i = 0; i < mappingPoints.Count; i++)
        {
            mappingPoints[i].noise01 = Mathf.Clamp01(mappingPoints[i].noise01);
        }

        mappingPoints.Sort((a, b) => a.noise01.CompareTo(b.noise01));
    }
}
