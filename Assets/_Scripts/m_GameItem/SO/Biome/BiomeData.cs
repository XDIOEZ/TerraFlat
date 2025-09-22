using Sirenix.OdinInspector;
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
    
    private void OnValidate()
    {
        // ���������OnValidate����
        if (Condition != null)
        {
            // EnvironmentConditionRange����֤����Unity�༭�����Զ�����
        }
        
        if (TerrainConfig != null)
        {
            TerrainConfig.OnValidate();
            // BiomeTerrainConfig����֤����Unity�༭�����Զ�����
        }
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
    
    private void OnValidate()
    {
        // ȷ����Χֵ����Ч��
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
            
        // ȷ��ֵ�ں���Χ��
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
    [Tooltip("����̬Ⱥϵ���ܰ����ĵؿ�Ԥ����")]
    [InlineEditor]
    public List<BiomeTileSpawn> TileSpawns;

    [InlineEditor]
    [Tooltip("�ڸ���̬Ⱥϵ�п������ɵ���Ʒ������")]
    public List<Biome_ItemSpawn> ItemSpawn = new ();

    [Tooltip("�ڸ���̬Ⱥϵ�п������ɵ���Ʒ������")]
    public List<Biome_ItemSpawn_NoSO> ItemSpawn_NoSO     = new ();

    [Tooltip("����̬Ⱥϵ�ĵ�������")]
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
    
    public void OnValidate()
    {
        // ��֤TileSpawns�б�
        if (TileSpawns != null)
        {
            for (int i = 0; i < TileSpawns.Count; i++)
            {
                if (TileSpawns[i] != null)
                {
                    // ����BiomeTileSpawn��OnValidate��������ڣ�
                }
            }
        }
        
        // ��֤ItemSpawn�б�
        if (ItemSpawn != null)
        {
            for (int i = 0; i < ItemSpawn.Count; i++)
            {
                if (ItemSpawn[i] != null)
                {
                    // ����Biome_ItemSpawn��OnValidate��������ڣ�
                }
            }
        }
        
        // ��֤ItemSpawn_NoSO�б�
        if (ItemSpawn_NoSO != null)
        {
            for (int i = 0; i < ItemSpawn_NoSO.Count; i++)
            {
                if (ItemSpawn_NoSO[i] != null)
                {
                    ItemSpawn_NoSO[i].OnValidate();
                    // ����Biome_ItemSpawn_NoSO��OnValidate��������ڣ�
                }
            }
        }
    }
}