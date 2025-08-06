using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Item_Tile_Dirt : Item, IBlockTile
{
    [SerializeField]
    private BlockData data = new BlockData();
    public override ItemData itemData { get => data; set => data = value as BlockData; }
    public TileBase _tileBase;
    [SerializeField]
    TileData _tileData;
    public TileData TileData { get => _tileData; set => _tileData = (TileData)value; }


    public void Awake()
    {
        if (data.tileData.Name_TileBase == "")
        {
            data.tileData = _tileData;
        }
        else
        {
            _tileData = data.tileData as TileData;
        }
    }

    public override void Act()
    {
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

    }

    public void Tile_Update(Item item, TileData tileData)
    {
        /*// �жϸ���Ʒ�Ƿ����ڡ�Plant������
        if (item.Item_Data.ItemTags.Item_TypeTag.Exists(x => x == "Plant"))
        {
            item.GetComponent<IPlant>().Grow(Time.deltaTime);
        }*/
    }
}
