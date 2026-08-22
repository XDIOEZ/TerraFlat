using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Hoe : Module
{
#region 数据定义

    [System.Serializable]
    [MemoryPackable]
    public partial class Mod_Hoe_Data
    {
        public int tilledCount = 0; // 已耕地数量统计

        public Mod_Hoe_Data() { }
    }

#endregion

#region 字段和属性

    public Mod_Hoe_Data Data = new Mod_Hoe_Data();
    public Ex_ModData_MemoryPackable ModData;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [Header("锄头配置")]
    [Tooltip("锄地成功时的特效名称（可选）")]
    public string tillEffectName = "";

    [Tooltip("锄地后替换成的目标地块SO")]
    public Tile_Block farmlandTileBlock;

    [Tooltip("可耕种地块SO列表（通过 Tile_Block 的 tileItemName / 模板ID 比较）")]
    public List<Tile_Block> tillableTileBlocks = new List<Tile_Block>();

    private bool _isActBound;

#endregion

#region 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
            _Data.ID = ModText.Tool;
    }

    public override void Load()
    {
        ModData.ReadData(ref Data);
        BindItemActEvent();
    }

    public override void Save()
    {
        ModData.WriteData(Data);
    }

    public override void Unload()
    {
        UnbindItemActEvent();
    }

    private void OnDestroy()
    {
        Unload();
    }

#endregion

#region 锄地逻辑

    public override void Act()
    {
        base.Act();
    }

    private void OnItemAct()
    {
        TryTillByMouse();
    }

    private void TryTillByMouse()
    {

        if (item == null || item.Owner == null)
        {
            Debug.LogWarning("[Mod_Hoe] 锄地失败：物品或玩家为空");
            return;
        }

        GameController controller = ResolveOwnerController();
        Vector3 mouseWorldPos = controller != null
            ? controller.GetMouseWorldPosition()
            : Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0f;
        mouseWorldPos = WorldTopologyRuntime.NormalizePosition(mouseWorldPos);

        if (!TryResolveMapByWorldPos(mouseWorldPos, out Map targetMap))
        {
            Debug.LogWarning($"[Mod_Hoe] 锄地失败：无法根据位置 {mouseWorldPos} 找到有效 Chunk/Map");
            return;
        }

        Vector2Int cellPos = GetTilePosFromWorldPos(targetMap, mouseWorldPos);

        if (TryTillTile(targetMap, cellPos))
        {
            Debug.Log($"[Mod_Hoe] ✓ 成功在 {cellPos} 耕地，已耕地数：{++Data.tilledCount}");

            // 播放特效
            if (!string.IsNullOrEmpty(tillEffectName) && VisualEffectManager.Instance != null)
            {
                VisualEffectManager.Instance.PlayEffect(
                    owner: item.Owner.transform,
                    effectName: tillEffectName,
                    parent: item.Owner.transform
                );
            }
        }
        else
        {
            Debug.LogWarning($"[Mod_Hoe] 无法在 {cellPos} 耕地，该位置不是草地或泥地");
        }
    }

    private void BindItemActEvent()
    {
        if (_isActBound || item == null)
            return;

        item.OnAct += OnItemAct;
        _isActBound = true;
    }

    private void UnbindItemActEvent()
    {
        if (!_isActBound || item == null)
            return;

        item.OnAct -= OnItemAct;
        _isActBound = false;
    }

#endregion

#region 辅助方法

    /// <summary>统一从持有者读取鼠标、手柄或手机径向指向。</summary>
    private GameController ResolveOwnerController()
    {
        Item owner = item?.Owner;
        GameController controller = owner?.itemMods?.GetMod_ByID<GameController>(ModText.Controller);
        return controller != null ? controller : owner?.GetComponent<GameController>();
    }

    private bool TryResolveMapByWorldPos(Vector3 worldPos, out Map map)
    {
        map = null;

        if (ChunkMgr.Instance == null)
            return false;

        ChunkMgr.Instance.GetChunkBy_ItemPosition(worldPos, out Chunk chunk);
        if (chunk == null || chunk.Map == null)
            return false;

        map = chunk.Map;
        return true;
    }

    private static Vector2Int GetTilePosFromWorldPos(Map map, Vector3 worldPos)
    {
        Vector3Int cellPos3D = map.tileMap.WorldToCell(worldPos);
        return new Vector2Int(cellPos3D.x, cellPos3D.y);
    }

    private bool TryTillTile(Map map, Vector2Int tilePos)
    {
        TileData currentTile = map.GetTopTile(tilePos);
        if (currentTile == null)
        {
            Vector2Int localPos = tilePos - map.Data.position;
            bool hasVisualTile = map.tileMap != null && map.tileMap.HasTile(new Vector3Int(tilePos.x, tilePos.y, 0));
            Debug.LogWarning($"[Mod_Hoe] 位置 {tilePos} 没有地块数据。mapPos={map.Data.position}, local={localPos}, size={map.Data.Width}x{map.Data.Height}, tilemapHasTile={hasVisualTile}");
            return false;
        }

        // 使用可配置SO白名单判断是否可耕种，避免字符串配置出错。
        if (!IsTillableBySO(currentTile))
        {
            string currentId = string.IsNullOrEmpty(currentTile.ID) ? "<empty>" : currentTile.ID;
            Debug.LogWarning($"[Mod_Hoe] 位置 {tilePos} 的地块ID={currentId}，不在可耕种SO列表中，无法耕地");
            return false;
        }

        // 通过 Tile_Block SO 的模板克隆耕地数据，避免手写参数与资源配置不一致。
        if (GameRes.Instance == null)
            throw new System.NullReferenceException("[Mod_Hoe] GameRes.Instance 为空，无法读取耕地 Tile_Block");

        Tile_Block farmlandBlock = farmlandTileBlock;
        if (farmlandBlock == null)
            throw new System.InvalidOperationException("[Mod_Hoe] farmlandTileBlock 未配置，请在Inspector挂接耕地Tile_Block SO");

        if (farmlandBlock.tileDataTemplate is not TileData_Farmland templateData)
            throw new System.InvalidCastException($"[Mod_Hoe] Tile_Block({farmlandBlock.name}) 的 tileDataTemplate 不是 TileData_Farmland");

        TileData_Farmland farmlandData = (TileData_Farmland)templateData.Clone();
        farmlandData.position = (Vector3Int)tilePos;
        farmlandData.workTime = 0f;

        // 用耕地数据替换当前顶层地块，避免不断叠层导致数据与显示不一致。
        var allTiles = map.GetAllTiles(tilePos);
        int topIndex = allTiles.Count - 1;
        if (topIndex < 0)
        {
            Debug.LogWarning($"[Mod_Hoe] 位置 {tilePos} 无可替换地块层");
            return false;
        }

        // 锄地同时清除并记录装饰草状态，避免读档或地块刷新后重新出现。
        map.TryConsumeGrassAt(tilePos);
        map.UpdateTile(tilePos, topIndex, farmlandData);

        // 再强制刷新一次，确保鼠标位置地块表现与数据一致。
        map.UpdateTileBaseAtPosition(tilePos);

        // 使用建筑同款的局部寻路更新，避免触发较重的异步全图烘焙。
        UpdateNavigationLikeBuilding(tilePos);

        Debug.Log($"[Mod_Hoe] 已在 {tilePos} 创建耕地（肥力={farmlandData.fertilityValue.Value:F1}, 水分={farmlandData.waterValue:F1}/{farmlandData.maxWater:F1}）");
        return true;
    }

    private bool IsTillableBySO(TileData currentTile)
    {
        if (currentTile == null)
            return false;

        if (tillableTileBlocks == null || tillableTileBlocks.Count == 0)
            return false;

        for (int i = 0; i < tillableTileBlocks.Count; i++)
        {
            Tile_Block block = tillableTileBlocks[i];
            if (block == null)
                continue;

            // 优先用 tileItemName 比较；若策划未填，则回退模板ID。
            if (!string.IsNullOrEmpty(block.tileItemName) && block.tileItemName == currentTile.ID)
                return true;

            if (block.tileDataTemplate != null && !string.IsNullOrEmpty(block.tileDataTemplate.ID) && block.tileDataTemplate.ID == currentTile.ID)
                return true;
        }

        return false;
    }

    private void UpdateNavigationLikeBuilding(Vector2Int tilePos)
    {
        Vector2 gridCenter = new Vector2(tilePos.x + 0.5f, tilePos.y + 0.5f);

        if (ChunkMgr.Instance == null)
        {
            Debug.LogWarning("[Mod_Hoe] ChunkMgr.Instance 为空，无法更新寻路");
            return;
        }

        ChunkMgr.Instance.GetChunkBy_ItemPosition(gridCenter, out Chunk chunk);
        if (chunk == null || chunk.Map == null)
        {
            Debug.LogWarning($"[Mod_Hoe] 无法找到位置 {tilePos} 对应的 Chunk/Map，跳过寻路更新");
            return;
        }

        chunk.Map.BackTilePenalty_Cell_3x3(gridCenter);
    }

#endregion
}
