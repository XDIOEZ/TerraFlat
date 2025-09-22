using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class PlanetData
{
    //��������
    public string Name;
    //����뾶
    public int Radius = 1000;
    //�¶�ƫ��ֵ
    public int TemperatureOffset = 0;
    //����ƫ��
    public float RainOffset = 0;
    //����߶�
    public float OceanHeight = -1;
    //��������
    public float NoiseScale = 0.01f;

    //�����ͼ��С
    public Vector2Int ChunkSize = new Vector2Int(100, 100);

    [ShowInInspector]
    //�����ͼ����
    public Dictionary<string, MapSave> MapData_Dict = new();

    [Tooltip("�����Ƿ��Զ����ɵ�ͼ")]
    public bool AutoGenerateMap = true;

}