using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BiomeData", menuName = "ScriptObjects/Biome Data")]
public class BiomeData : ScriptableObject
{
    [Header("基本信息")]
    public string BiomeName;
    [Multiline] public string Description;
    public Color PreviewColor = Color.white;

    [Header("环境条件")]
    [Tooltip("温度范围（x=最低温，y=最高温，单位：°C）")]
    public Vector2 TemperatureRange = new Vector2(-20f, 40f);

    [Tooltip("湿度范围（x=最低湿度，y=最高湿度，单位：百分比%）")]
    public Vector2 HumidityRange = new Vector2(30f, 80f);

    [Tooltip("降水范围（x=最小降水，y=最大降水，单位：毫米/年）")]
    public Vector2 PrecipitationRange = new Vector2(0f, 3000f); // 示例：沙漠0~250mm，雨林2000~3000mm

    [Tooltip("固体比例（x=最低固体，y=最高固体，单位：百分比%）")]
    public Vector2 SolidityRange = new Vector2(0f, 100f);



    [Header("地形配置")]
    public List<string> TileData_Name;
    public List<Biome_ItemSpawn> ItemSpawn;
    public string AmbientSoundKey;
    [Range(0f, 1f)] public float BiomeWeight = 1.0f;

    // 综合环境检测方法
    public bool IsEnvironmentValid(float temp, float humid, float precipitation, float solidity)
    {
        return //TemperatureRange.x <= temp && temp <= TemperatureRange.y &&
              //HumidityRange.x <= humid && humid <= HumidityRange.y &&
             //  PrecipitationRange.x <= precipitation && precipitation <= PrecipitationRange.y &&
                SolidityRange.x <= solidity && solidity <= SolidityRange.y;
    }

    // 环境匹配度评分（可选，用于优先级）
    public float GetEnvironmentMatchScore(float temp, float humid, float precipitation)
    {
        float tempScore = Mathf.InverseLerp(TemperatureRange.x, TemperatureRange.y, temp);
        float humidScore = Mathf.InverseLerp(HumidityRange.x, HumidityRange.y, humid);
        float precipScore = Mathf.InverseLerp(PrecipitationRange.x, PrecipitationRange.y, precipitation);
        return (tempScore + humidScore + precipScore) / 3f; // 平均值
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        // 温度范围约束
        TemperatureRange.x = Mathf.Clamp(TemperatureRange.x, -100f, 100f);
        TemperatureRange.y = Mathf.Clamp(TemperatureRange.y, -100f, 100f);

        // 湿度范围约束
        HumidityRange.x = Mathf.Clamp(HumidityRange.x, 0f, 100f);
        HumidityRange.y = Mathf.Clamp(HumidityRange.y, 0f, 100f);

        // 降水范围约束
        PrecipitationRange.x = Mathf.Max(0f, PrecipitationRange.x); // 降水不能为负
        PrecipitationRange.y = Mathf.Max(PrecipitationRange.x, PrecipitationRange.y); // 确保max>=min

        // 自动修正最小值>最大值的情况
        if (TemperatureRange.x > TemperatureRange.y) TemperatureRange.x = TemperatureRange.y;
        if (HumidityRange.x > HumidityRange.y) HumidityRange.x = HumidityRange.y;
        if (PrecipitationRange.x > PrecipitationRange.y) PrecipitationRange.x = PrecipitationRange.y;
    }
#endif
}