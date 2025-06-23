
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Item_Tile_Water : Item, IBlockTile
{
    [SerializeField]
    private BlockData data;


    public override ItemData Item_Data { get => data; set => data = value as BlockData; }


    [SerializeField]
    TileData_Water _tileData;
    public TileData TileData { get => _tileData; set => _tileData = (TileData_Water)value; }

    public List<Buff_Data> BuffInfo;

    public void Awake()
    {
        if (data.tileData.Name_ItemName == "")
        {
            data.tileData = _tileData;
        }
        else
        {
            _tileData = (TileData_Water)data.tileData;
        }
    }
    public override void Act()
    {
        Set_TileBase_ToWorld(TileData);
    }

    public void Set_TileBase_ToWorld(TileData tileData)
    {
        // ��ȡ�������Ļ�ϵ�λ��
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        Map mapCoreScript = (Map)RunTimeItemManager.Instance.GetItemsByNameID("MapCore")[0];

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

    //tiledata.ˮ����� =10m 
    public void Tile_Enter(Item item, TileData tileData)
    {
        if (item == null)
        {
            Debug.LogError("[Tile_Enter] item �� null���޷�ִ�� Buff ���");
            return;
        }

        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        if (buffManager == null)
        {
            Debug.LogError($"[Tile_Enter] item {item.name} û���ҵ� BuffManager ���");
            return;
        }

        if (BuffInfo == null || BuffInfo.Count == 0)
        {
            Debug.LogWarning("[Tile_Enter] BuffInfo �б�Ϊ�գ��� Buff �����");
            return;
        }

       // Debug.Log($"[Tile_Enter] ��ʼ�� item {item.name} ��� Buff������ {BuffInfo.Count} ��");

        foreach (Buff_Data buffData in BuffInfo)
        {
            if (buffData == null)
            {
                Debug.LogWarning("[Tile_Enter] ��⵽�յ� Buff_Info������");
                continue;
            }
        //    Debug.Log($"[Tile_Enter] ��� Buff: {buffData.buff_Name} �� {item.name}");
            buffManager.AddBuffByData(new BuffRunTime(buffData, this, item));
        }
    }


    public void Tile_Exit(Item item, TileData tileData)
    {
        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        foreach (Buff_Data buffData in BuffInfo)
        {
            buffManager.RemoveBuff(buffData.buff_ID);
        }
    }

    public void Tile_Update(Item item, TileData tileData)
    {
        //throw new System.NotImplementedException();
    }
}
