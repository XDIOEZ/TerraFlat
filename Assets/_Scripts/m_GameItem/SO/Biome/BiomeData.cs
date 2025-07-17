using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BiomeData", menuName = "ScriptObjects/Biome Data")]
public class BiomeData : ScriptableObject
{
    [Header("������Ϣ")]
    public string BiomeName;
    [Multiline] public string Description;
    public Color PreviewColor = Color.white;

    [Header("��������")]
    public EnvironmentConditionRange Condition;

    [Header("��������")]
    public BiomeTerrainConfig TerrainConfig;

    public bool IsEnvironmentValid(EnvironmentFactors factors)
    {
        return Condition.IsMatch(factors);
    }
}

[System.Serializable]
public class EnvironmentConditionRange
{
    [Tooltip("�¶ȷ�Χ��x=����£�y=�����")]
    public Vector2 TemperatureRange = new Vector2(0, 1);

    [Tooltip("ʪ�ȷ�Χ��x=���ʪ�ȣ�y=���ʪ�ȣ�")]
    public Vector2 HumidityRange = new Vector2(0, 1);

    [Tooltip("��ˮ��Χ��x=��С��ˮ��y=���ˮ��")]
    public Vector2 PrecipitationRange = new Vector2(0, 1);

    [Tooltip("���������x=��͹��壬y=��߹��壩")]
    public Vector2 SolidityRange = new Vector2(0, 1);

    [Tooltip("�߶ȷ�Χ��x=��͸߶ȣ�y=��߸߶ȣ�")]
    public Vector2 HightRange = new Vector2(0, 1);

    // �жϵ�ǰֵ�Ƿ��ڷ�Χ��
    public bool IsMatch(EnvironmentFactors factors)
    {
        return TemperatureRange.x <= factors.Temperature && factors.Temperature <= TemperatureRange.y &&
               HumidityRange.x <= factors.Humidity && factors.Humidity <= HumidityRange.y &&
               PrecipitationRange.x <= factors.Precipitation && factors.Precipitation <= PrecipitationRange.y &&
               HightRange.x <= factors.Hight && factors.Hight <= HightRange.y&&
        SolidityRange.x <= factors.Solidity && factors.Solidity <= SolidityRange.y;
    }
}


[System.Serializable]
public class BiomeTerrainConfig
{
    [Tooltip("����̬Ⱥϵ���ܰ����ĵؿ�Ԥ����")]
    public List<BiomeTileSpawn> TileSpawns;

    [Tooltip("�ڸ���̬Ⱥϵ�п������ɵ���Ʒ������")]
    public List<Biome_ItemSpawn> ItemSpawn = new ();

    /// <summary>
    /// ���ݻ������ӣ��ɰ�������������һ���ؿ�Ԥ����
    /// </summary>
    public string GetTilePrefab(EnvironmentFactors env)
    {
        if (TileSpawns == null || TileSpawns.Count == 0)
        {
            Debug.LogError("TileData_Prefab �б�Ϊ�գ�");
            return null;
        }

        //// �򵥰汾��ʹ��ʪ�Ȼ�ˮ��Ϊ����ӳ����Դ
        //float noiseValue = Mathf.InverseLerp(0f, 100f, env.Humidity); // Ҳ������env.Temperature��

        //// ӳ�䵽����
        //int index = Mathf.FloorToInt(noiseValue * TileData_Prefab.Count);
        //index = Mathf.Clamp(index, 0, TileData_Prefab.Count - 1);

        return TileSpawns[0].TileDataName;
    }
}

