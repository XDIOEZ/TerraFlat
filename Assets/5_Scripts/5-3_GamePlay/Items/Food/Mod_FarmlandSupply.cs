using System;
using UnityEngine;

/// <summary>
/// 耕地补给模块：把物品的一次使用转换为固定水分和肥力，成功后只消耗一个物品。
/// </summary>
public class Mod_FarmlandSupply : Module
{
#region 配置

    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    public Ex_ModData ModData = new Ex_ModData();
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData)value;
    }

    [Header("单次补给量")]
    [SerializeField, Min(0f)] private float waterAmount = 25f;
    [SerializeField, Min(0f)] private float fertilityAmount = 0.35f;

    private bool _isActBound;

#endregion

#region 生命周期

    public override void Awake()
    {
        base.Awake();
        if (string.IsNullOrEmpty(_Data.ID))
            _Data.ID = nameof(Mod_FarmlandSupply);
    }

    public override void Load()
    {
        BindActEvent();
    }

    public override void Save()
    {
        UnbindActEvent();
    }

    private void OnDestroy()
    {
        UnbindActEvent();
    }

#endregion

#region 使用逻辑

    public override void Act()
    {
        base.Act();

        if (item == null || item.Owner == null)
        {
            Debug.LogWarning("[Mod_FarmlandSupply] 补给失败：物品未被玩家持有", item);
            return;
        }

        if (Camera.main == null)
        {
            Debug.LogWarning("[Mod_FarmlandSupply] 补给失败：缺少主摄像机", item);
            return;
        }

        GameController controller = ResolveOwnerController();
        Vector3 mouseWorldPosition = controller != null
            ? controller.GetMouseWorldPosition()
            : Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPosition = WorldTopologyRuntime.NormalizePosition(mouseWorldPosition);
        if (!TryResolveFarmland(mouseWorldPosition, out Vector2Int tilePos, out TileData_Farmland farmlandData))
        {
            Debug.LogWarning($"[Mod_FarmlandSupply] {mouseWorldPosition} 不是耕地，未消耗物品", item);
            return;
        }

        farmlandData.NormalizeValues();
        float previousWater = farmlandData.waterValue;
        float previousFertility = farmlandData.Fertility;
        farmlandData.AddWater(waterAmount);
        farmlandData.AddFertility(fertilityAmount);

        if (Mathf.Approximately(previousWater, farmlandData.waterValue) &&
            Mathf.Approximately(previousFertility, farmlandData.Fertility))
        {
            Debug.Log($"[Mod_FarmlandSupply] 地块 {tilePos} 的水分和肥力已经充足，未消耗物品", item);
            return;
        }

        item.itemData.Stack.Amount--;
        item.OnUIRefresh?.Invoke();
        Debug.Log(
            $"[Mod_FarmlandSupply] 地块 {tilePos} 补给完成：水分 {previousWater:F1}→{farmlandData.waterValue:F1}，" +
            $"肥力 {previousFertility:F2}→{farmlandData.Fertility:F2}",
            item);

        if (item.itemData.Stack.Amount <= 0)
            item.DestroySelf();
    }

    private static bool TryResolveFarmland(
        Vector3 worldPosition,
        out Vector2Int tilePos,
        out TileData_Farmland farmlandData)
    {
        tilePos = default;
        farmlandData = null;

        if (ChunkMgr.Instance == null)
            return false;

        ChunkMgr.Instance.GetChunkBy_ItemPosition(worldPosition, out Chunk chunk);
        if (chunk == null || chunk.Map == null || chunk.Map.tileMap == null)
            return false;

        Vector3Int cellPosition = chunk.Map.tileMap.WorldToCell(worldPosition);
        tilePos = new Vector2Int(cellPosition.x, cellPosition.y);
        farmlandData = chunk.Map.GetTileAt(tilePos, 0) as TileData_Farmland;
        return farmlandData != null;
    }

#endregion

#region 事件绑定

    /// <summary>统一从持有者读取鼠标、手柄或手机径向指向。</summary>
    private GameController ResolveOwnerController()
    {
        Item owner = item?.Owner;
        GameController controller = owner?.itemMods?.GetMod_ByID<GameController>(ModText.Controller);
        return controller != null ? controller : owner?.GetComponent<GameController>();
    }

    private void BindActEvent()
    {
        if (_isActBound || item == null)
            return;

        item.OnAct += Act;
        _isActBound = true;
    }

    private void UnbindActEvent()
    {
        if (!_isActBound || item == null)
            return;

        item.OnAct -= Act;
        _isActBound = false;
    }

#endregion
}
