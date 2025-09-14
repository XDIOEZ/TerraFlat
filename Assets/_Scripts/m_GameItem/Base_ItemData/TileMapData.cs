using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

[System.Serializable]
[MemoryPackable]
public partial class Data_TileMap : ItemData
{
    [HideInInspector]
    [SerializeField]
    public Dictionary<Vector2Int, List<TileData>> TileData = new();

    [Tooltip("��ͼ��λ��")]
    public Vector2Int position = new Vector2Int(0,0);

    public bool TileLoaded = false;

    public EnvironmentFactors[,] EnvironmentData = new EnvironmentFactors[0, 0];
}

[System.Serializable]
[MemoryPackable]
[MemoryPackUnion(54, typeof(TileData_Grass))]//ǽ������
[MemoryPackUnion(55, typeof(TileData_Water))]//��������
public  abstract partial class TileData
{
    //��Ʒ�Ļ������ ����ʵ��
    public string Name_TileBase;
    //��Ӧ����Ʒ����--���ڻ�ȡ��Ʒ�еķ���
    public string Name_ItemName;
    //�ؿ��Tag
    public string TileTag = "";
    //�ؿ�����λ��
    public Vector3Int position;
    //�������ʱ��
    public float DemolitionTime;
    //��ǰ�����ʱ��
    public float workTime;
    //�ؿ��ƶ�Ȩ��
    public uint Penalty = 1000;


    // �麯�������ݻ�����ʼ��
    public virtual void Initialize(EnvironmentFactors env) { }
    /// <summary>
    /// ��дToString���������ض������ϸ��Ϣ
    /// </summary>
    /// <returns>���������ֶ���Ϣ���ַ���</returns>
    public override string ToString()
    {
        return $"TileData {{\n" +
        $"�ؿ��������: {Name_TileBase},\n" +
        $" ��Ӧ��Ʒ����: {Name_ItemName},\n" +
        $"�ؿ��ǩ: {TileTag},\n" +
        $" �ؿ�λ��: ({position.x}, {position.y}, {position.z}),\n" +
        $"�������ʱ��: {DemolitionTime:F2},\n" + // ���� 2 λС������ֵ��ֱ��
        $"��ǰ���ʱ��: {workTime:F2}\n" +
        "}";
    }
}
[System.Serializable]
[MemoryPackable]
public partial class TileData_Grass : TileData
{
    public GameValue_float FertileValue = new GameValue_float();
}
[System.Serializable]
[MemoryPackable]
public partial class TileData_Water : TileData
{
    public GameValue_float DeepValue = new GameValue_float();

    public override void Initialize(EnvironmentFactors env)
    {
        // �߶� 0.5 �� ��� 0
        // �߶� 0   �� ��� 1
        DeepValue.BaseValue = (0.5f - env.Hight) / 0.5f;
    }
    /// <summary>
    /// ��дToString����������ˮ�ؿ����ϸ��Ϣ�����ĸ�ʽ��
    /// </summary>
    /// <returns>����������Ϣ��ˮ��ֵ�ĸ�ʽ���ַ���</returns>
    public override string ToString()
    {
        // �������ַ������Ƴ���β�Ĵ����Ų�����ԭ������
        string parentInfo = base.ToString()
            .TrimStart('{', ' ')
            .TrimEnd('}')
            .Replace("\n  ", "\n    "); // �����ֶ���������һ�����������ֶ�����

        return $"TileData_Water {{\n" +
               $"  {parentInfo},\n" +  // �̳и����������Ϣ
               $"  ˮ�����ֵ: {DeepValue.BaseValue:F2}\n" +  // ˮ��ֵ����2λС��
               "}";
    }

}

