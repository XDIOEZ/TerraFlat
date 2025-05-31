using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public class Item_Tile_Grass : Item,IBlockTile
{
    [SerializeField]
    private Data_Tile_Block data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Tile_Block; }
    public TileData Data_Tile { get=> Data.tileData; set=> Data.tileData = value; }
    public Data_Tile_Block Data { get => data; set => data = value; }

    public override void Act()
    {
        Set_TileBase_ToWorld(Data_Tile);
    }

    public void Set_TileBase_ToWorld(TileData tileData)
    {
        // ��ȡ�������Ļ�ϵ�λ��
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        // ��ȡ MapCore ����� Map �ű�
        GameObject mapCore = GameObject.FindGameObjectWithTag("MapCore");
        Map mapCoreScript = mapCore.GetComponent<Map>();

        // ʹ�� Map �ű��е� tileMap
        Tilemap tileMap = mapCoreScript.tileMap;

        // ����������ת��Ϊ��������
        Vector3Int cellPos3D = tileMap.WorldToCell(worldPos);
        Vector2Int cellPos2D = new Vector2Int(cellPos3D.x, cellPos3D.y);

        // ���� TileData ������
        tileData.position = cellPos3D;

        // ��Ӳ�ˢ�� Tile
        mapCoreScript.ADDTile(cellPos2D, tileData);
        mapCoreScript.UpdateTileBaseAtPosition(cellPos2D); // ȷ�������������
    }

    public void Tile_Exit(Item item, TileData tileData)
    {
       // throw new System.NotImplementedException();
    }

    //�������Ǻ� ˮ����ֻ��Ӱ���ƶ�Ч�� �Ҳ�ϣ����Ӱ�������� ���ǲݷ����ֲ��ǵ���ֻӰ���ƶ�Ч��
    public void Tile_Enter(Item item, TileData tileData)
    {
        //ToDo �ڴ˷�����ʵ�ּ���Ч�� ͨ��ʵ����Buff������� ������ֱ���޸�Item��Speed����
        //  item.GetComponentInChildren<IMover>().Speed = 0.5f;

        //1. ʵ����Buff���� �������������
        //GameObject buffObj = RunTimeItemManager.Instance.InstantiateItem(Data.buff);

        //2.����Buff����

    }
}

public interface IBlockTile
{
    public TileData Data_Tile { get; set; }
    public Data_Tile_Block Data { get; set; }
    public void Set_TileBase_ToWorld(TileData tileData);
    public void Tile_Enter(Item item, TileData tileData);
    public void Tile_Exit(Item item, TileData tileData);
}

