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
    }
}
