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

    [MemoryPackIgnore]
    public int TileCount => TileData.Count == 0 ? 0 : TileData.Values.Sum(list => list?.Count ?? 0);
}

[System.Serializable]
[MemoryPackable]
public partial class TileData
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
}