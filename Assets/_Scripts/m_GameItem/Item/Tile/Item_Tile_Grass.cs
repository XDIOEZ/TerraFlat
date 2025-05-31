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

    public void Tile_Exit(Item item, TileData tileData)
    {
       // throw new System.NotImplementedException();
    }

    //这可如何是好 水方块只会影响移动效果 我不希望其影响其他的 但是草方块又不是单独只影响移动效果
    public void Tile_Enter(Item item, TileData tileData)
    {
        //ToDo 在此方法中实现减速效果 通过实例化Buff对象完成 而不是直接修改Item的Speed属性
        //  item.GetComponentInChildren<IMover>().Speed = 0.5f;

        //1. 实例化Buff对象 并设置相关属性
        //GameObject buffObj = RunTimeItemManager.Instance.InstantiateItem(Data.buff);

        //2.激活Buff对象

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

