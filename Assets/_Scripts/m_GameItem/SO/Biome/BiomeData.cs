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
    [Tooltip("�¶ȷ�Χ��x=����£�y=����£���λ����C��")]
    public Vector2 TemperatureRange = new Vector2(-20f, 40f);

    [Tooltip("ʪ�ȷ�Χ��x=���ʪ�ȣ�y=���ʪ�ȣ���λ���ٷֱ�%��")]
    public Vector2 HumidityRange = new Vector2(30f, 80f);

    [Tooltip("��ˮ��Χ��x=��С��ˮ��y=���ˮ����λ������/�꣩")]
    public Vector2 PrecipitationRange = new Vector2(0f, 3000f); // ʾ����ɳĮ0~250mm������2000~3000mm

    [Tooltip("���������x=��͹��壬y=��߹��壬��λ���ٷֱ�%��")]
    public Vector2 SolidityRange = new Vector2(0f, 100f);



    [Header("��������")]
    public List<string> TileData_Name;
    public List<Biome_ItemSpawn> ItemSpawn;
    public string AmbientSoundKey;
    [Range(0f, 1f)] public float BiomeWeight = 1.0f;

    // �ۺϻ�����ⷽ��
    public bool IsEnvironmentValid(float temp, float humid, float precipitation, float solidity)
    {
        return //TemperatureRange.x <= temp && temp <= TemperatureRange.y &&
              //HumidityRange.x <= humid && humid <= HumidityRange.y &&
             //  PrecipitationRange.x <= precipitation && precipitation <= PrecipitationRange.y &&
                SolidityRange.x <= solidity && solidity <= SolidityRange.y;
    }

    // ����ƥ������֣���ѡ���������ȼ���
    public float GetEnvironmentMatchScore(float temp, float humid, float precipitation)
    {
        float tempScore = Mathf.InverseLerp(TemperatureRange.x, TemperatureRange.y, temp);
        float humidScore = Mathf.InverseLerp(HumidityRange.x, HumidityRange.y, humid);
        float precipScore = Mathf.InverseLerp(PrecipitationRange.x, PrecipitationRange.y, precipitation);
        return (tempScore + humidScore + precipScore) / 3f; // ƽ��ֵ
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        // �¶ȷ�ΧԼ��
        TemperatureRange.x = Mathf.Clamp(TemperatureRange.x, -100f, 100f);
        TemperatureRange.y = Mathf.Clamp(TemperatureRange.y, -100f, 100f);

        // ʪ�ȷ�ΧԼ��
        HumidityRange.x = Mathf.Clamp(HumidityRange.x, 0f, 100f);
        HumidityRange.y = Mathf.Clamp(HumidityRange.y, 0f, 100f);

        // ��ˮ��ΧԼ��
        PrecipitationRange.x = Mathf.Max(0f, PrecipitationRange.x); // ��ˮ����Ϊ��
        PrecipitationRange.y = Mathf.Max(PrecipitationRange.x, PrecipitationRange.y); // ȷ��max>=min

        // �Զ�������Сֵ>���ֵ�����
        if (TemperatureRange.x > TemperatureRange.y) TemperatureRange.x = TemperatureRange.y;
        if (HumidityRange.x > HumidityRange.y) HumidityRange.x = HumidityRange.y;
        if (PrecipitationRange.x > PrecipitationRange.y) PrecipitationRange.x = PrecipitationRange.y;
    }
#endif
}