
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Item_Tile_Water : Item, IBlockTile
{
    //Item的游戏数据 用于存档
    [SerializeField]
    private BlockData data;

    //Item的基础数据
    public override ItemData itemData { get => data; set => data = value as BlockData; }

    //TileData 方便策划设置 TileData的相关参数 和玩家修改TileData的数据
    [SerializeField]
    TileData_Water _tileData;
    //实现接口
    public TileData TileData { get => _tileData; set => _tileData = (TileData_Water)value; }
    //挂接的Buff
    public List<Buff_Data> BuffInfo;

    public void Awake()
    {
        if (data.tileData.Name_TileBase == "")
        {
            data.tileData = _tileData;
        }
        else
        {
            _tileData = data.tileData as TileData_Water;
        }
    }
    public override void Act()
    {
        Set_TileBase_ToWorld(TileData);
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
        mapCoreScript.ADDTile(cellPos2D, tileData);
        mapCoreScript.UpdateTileBaseAtPosition(cellPos2D); // 确保你有这个方法
    }
    //tiledata.水的深度 =10m 
    public void Tile_Enter(Item item, TileData tileData)
    {
        bool validItem = item != null;
        BuffManager buffManager = validItem ? item.GetComponentInChildren<BuffManager>() : null;

        // Buff 添加逻辑
        if (!validItem)
        {
            Debug.LogError("[Tile_Enter] item 是 null，无法执行 Buff 添加");
        }
        else if (buffManager == null)
        {
            Debug.LogError($"[Tile_Enter] item {item.name} 没有找到 BuffManager 组件");
        }
        else if (BuffInfo == null || BuffInfo.Count == 0)
        {
            Debug.LogWarning("[Tile_Enter] BuffInfo 列表为空，无 Buff 被添加");
        }
        else
        {
            foreach (Buff_Data buffData in BuffInfo)
            {
                if (buffData == null)
                {
                    Debug.LogWarning("[Tile_Enter] 检测到空的 Buff_Info，跳过");
                    continue;
                }

                buffManager.AddBuffRuntime(buffData,item);
            }
        }

        // 模块添加逻辑（入水特效）
        if (validItem && item.itemMods.GetMod_ByID("入水特效")==null)
        {
            Module.ADDModTOItem(item, "入水特效");

            // 获取模块的 Transform 并修改位置
            Transform modTransform = item.itemMods.GetMod_ByID("入水特效").transform;
            Vector3 pos = modTransform.localPosition;
            //tile的位置
            //通过Tile获取env的参数
            TileData_Water water  = tileData as TileData_Water;

            pos.y = Mathf.Lerp(-0.7f, 0,water.DeepValue.Value );
            pos.x = 0f;
            modTransform.localPosition = pos;
        }
    }



    public void Tile_Exit(Item item, TileData tileData)
    {
        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        if (buffManager == null) return;

        foreach (Buff_Data buffData in BuffInfo)
        {
            if (buffManager.HasBuff(buffData.buff_ID))  // 假设有这个方法
            {
                buffManager.RemoveBuff(buffData.buff_ID);
            }
        }

        if (item.itemMods.GetMod_ByID("入水特效") != null)
        Module.REMOVEModFROMItem(item, "入水特效");
    }


    public void Tile_Update(Item item, TileData tileData)
    {
        //throw new System.NotImplementedException();
    }
}
