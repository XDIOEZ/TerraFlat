using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public class Item_Tile_Grass : Item
{
    [SerializeField]
    private BlockData data = new BlockData();
    public override ItemData itemData { get => data; set => data = value as BlockData; }

    [SerializeField]
    TileData_Grass _tileData;
    public TileData TileData { get => _tileData; set => _tileData = (TileData_Grass)value; }

    public override void Act()
    {
    }

    public void Set_TileBase_ToWorld(TileData tileData)
    {
        // 获取鼠标在屏幕上的位置
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        // 获取 MapCore 对象和 Map 脚本
        GameObject mapCore = GameObject.FindGameObjectWithTag("MapCore");
        Map mapCoreScript = mapCore.GetComponent<Map>();

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

}

