using Sirenix.OdinInspector;
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
    public EnvironmentConditionRange Condition;


    [Header("地形配置")]
    public BiomeTerrainConfig TerrainConfig;

    [Header("温度表现（℃）")]
    [Tooltip("开启后，此群系会使用专属温度区间覆盖全局温度映射")]
    public bool UseCustomTemperatureRange = false;
    [Tooltip("此群系的摄氏温度区间（x=最低温，y=最高温）")]
    public Vector2 TemperatureRangeCelsius = new Vector2(20f, 30f);

    public bool IsEnvironmentValid(EnvironmentFactors factors)
    {
        return Condition.IsMatch(factors);
    }
    
    private void OnValidate()
    {
        // 调用子类的OnValidate函数
        if (Condition != null)
        {
            // EnvironmentConditionRange的验证会在Unity编辑器中自动处理
        }
        
        if (TerrainConfig != null)
        {
            TerrainConfig.OnValidate();
            // BiomeTerrainConfig的验证会在Unity编辑器中自动处理
        }

        if (TemperatureRangeCelsius.x > TemperatureRangeCelsius.y)
        {
            TemperatureRangeCelsius.y = TemperatureRangeCelsius.x;
        }
    }
}
