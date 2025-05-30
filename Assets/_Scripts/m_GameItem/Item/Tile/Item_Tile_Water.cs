using Codice.CM.WorkspaceServer;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Item_Tile_Water : Item, IBlockTile
{
    [SerializeField]
    private Data_Tile_Block data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Tile_Block; }
    public TileData Data_Tile { get => Data.tileData; set => Data.tileData = value; }
    public Data_Tile_Block Data { get => data; set => data = value; }

    public override void Act()
    {
        Set_TileBase_ToWorld(Data_Tile);
    }

    public void Set_TileBase_ToWorld(TileData tileData)
    {
        // 获取鼠标在屏幕上的位置
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        Map mapCoreScript = (Map)RunTimeItemManager.Instance.GetItemsByNameID("MapCore")[0];

        // 使用 Map 脚本中的 tileMap
        Tilemap tileMap = mapCoreScript.tileMap;

        // 把世界坐标转换为格子坐标
        Vector3Int cellPos3D = tileMap.WorldToCell(worldPos);
        Vector2Int cellPos2D = new Vector2Int(cellPos3D.x, cellPos3D.y);

        // 设置 TileData 的坐标
        tileData.position = cellPos3D;

        // 添加并刷新 Tile
        mapCoreScript.ADDTile(cellPos2D, tileData);
        mapCoreScript.UpdateTileBaseAtPosition(cellPos2D); // 确保你有这个方法
    }

    public void Tile_Interact(Item item,TileData tileData)
    {
        
        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        foreach (BuffData buffData in tileData.buffData)
        {
            buffManager.AddBuffByData(buffData);
        }
    }

    public void Tile_Exit(Item item, TileData tileData)
    {
        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        foreach (BuffData buffData in tileData.buffData)
        {
            buffManager.RemoveBuffByData(buffData);
        }
    }
}
