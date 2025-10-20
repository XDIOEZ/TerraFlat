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
    public bool IsMatch(EnvironmentFactors factors)
    {
        return TemperatureRange.x <= factors.Temperature && factors.Temperature <= TemperatureRange.y &&
               HumidityRange.x <= factors.Humidity && factors.Humidity <= HumidityRange.y &&
               PrecipitationRange.x <= factors.Precipitation && factors.Precipitation <= PrecipitationRange.y &&
               HightRange.x <= factors.Hight && factors.Hight <= HightRange.y&&
        SolidityRange.x <= factors.Solidity && factors.Solidity <= SolidityRange.y;
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


[System.Serializable]
public class BiomeTerrainConfig
{
    [Tooltip("该生态群系可能包含的地块预制体")]
    [InlineEditor]
    public List<BiomeTileSpawn> TileSpawns;

    [InlineEditor]
    [Tooltip("在该生态群系中可能生成的物品及概率")]
    public List<Biome_ItemSpawn> ItemSpawn = new ();

    [Tooltip("在该生态群系中可能生成的物品及概率")]
    public List<Biome_ItemSpawn_NoSO> ItemSpawn_NoSO     = new ();

    [Tooltip("该生态群系的地形类型")]
    public string GetTilePrefab(EnvironmentFactors env)
    {
        if (TileSpawns == null || TileSpawns.Count == 0)
        {
            Debug.LogError("TileData_Prefab 列表为空！");
            return null;
        }

        //// 简单版本：使用湿度或降水作为噪声映射来源
        //float noiseValue = Mathf.InverseLerp(0f, 100f, env.Humidity); // 也可以是env.Temperature等

        //// 映射到索引
        //int index = Mathf.FloorToInt(noiseValue * TileData_Prefab.Count);
        //index = Mathf.Clamp(index, 0, TileData_Prefab.Count - 1);

        return TileSpawns[0].TileDataName;
    }
    
    public void OnValidate()
    {
        // 验证TileSpawns列表
        if (TileSpawns != null)
        {
            for (int i = 0; i < TileSpawns.Count; i++)
            {
                if (TileSpawns[i] != null)
                {
                    // 调用BiomeTileSpawn的OnValidate（如果存在）
                }
            }
        }
        
        // 验证ItemSpawn列表
        if (ItemSpawn != null)
        {
            for (int i = 0; i < ItemSpawn.Count; i++)
            {
                if (ItemSpawn[i] != null)
                {
                    // 调用Biome_ItemSpawn的OnValidate（如果存在）
                }
            }
        }
        
        // 验证ItemSpawn_NoSO列表
        if (ItemSpawn_NoSO != null)
        {
            for (int i = 0; i < ItemSpawn_NoSO.Count; i++)
            {
                if (ItemSpawn_NoSO[i] != null)
                {
                    ItemSpawn_NoSO[i].OnValidate();
                    // 调用Biome_ItemSpawn_NoSO的OnValidate（如果存在）
                }
            }
        }
    }
}