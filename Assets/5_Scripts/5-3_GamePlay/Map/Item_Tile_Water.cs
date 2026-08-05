   
using UnityEngine;
using UnityEngine.Tilemaps;

public class Item_Tile_Water : Item
{
    //Item的游戏数据 用于存档
    [SerializeField]
    private BlockData data;

    //Item的基础数据
    public override ItemData itemData { get => data; set => data = value as BlockData; }

    public override void Act()
    {

    }

    public void Set_TileBase_ToWorld(TileData tileData)
    {
        // 获取鼠标在屏幕上的位置
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        Map mapCoreScript = (Map)ItemMgr.Instance.GetItemsByNameID("MapCore")[0];

        // 使用 Map 脚本中的 tileMap
        Tilemap tileMap = mapCoreScript.tileMap;

        // 把世界坐标转换为格子坐标
        Vector3Int cellPos3D = tileMap.WorldToCell(worldPos);
        Vector2Int cellPos2D = new Vector2Int(cellPos3D.x, cellPos3D.y);

        // 设置 TileData 的坐标
        tileData.position = cellPos3D;

        // 添加并刷新 Tile
        mapCoreScript.PushTile(cellPos2D, tileData);
        mapCoreScript.UpdateTileBaseAtPosition(cellPos2D); // 确保你有这个方法
    }
}
