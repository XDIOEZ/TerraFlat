using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public class Item_Tile_Grass : Item,IBlockTile
{
    public Data_Tile_Block Data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Tile_Block; }
    public TileData Data_Tile { get=> Data.tileData; set=> Data.tileData = value; }

    public override void Act()
    {
        SetTileBase(Data_Tile);
    }

    public void SetTileBase(TileData tileData)
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

    public void TileInteract(Item item)
    {

    }
}

public interface IBlockTile
{
    public TileData Data_Tile { get; set; }
    public void SetTileBase(TileData tileData);
    public void TileInteract(Item item);
}

