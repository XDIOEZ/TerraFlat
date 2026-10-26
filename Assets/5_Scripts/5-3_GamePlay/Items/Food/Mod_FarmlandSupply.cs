using System;
using FlatWorld.Networking;
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
    [SerializeField, Min(0)] private int waterAmount = 3;
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

    protected override void OnLoad()
    {
        BindActEvent();
    }

    protected override void OnSave()
    {
    }

    protected override void OnUnload()
    {
        UnbindActEvent();
    }

    private void OnDestroy()
    {
        Unload();
    }

#endregion

#region 使用逻辑

    public override void Act()
    {
        base.Act();

        if (!GameNetwork.HasStateAuthority || item == null || !item.InHand || item.Owner == null)
            return;

        Mod_GameController controller = ResolveOwnerController();
        if (controller == null)
            return;

        TrySupplyFarmlandAtWorldPosition(controller.GetMouseWorldPosition());
    }

    // 指向使用和自动回归共用正式持有、距离、水肥提交与扣量规则。
    public bool TrySupplyFarmlandAtWorldPosition(Vector3 worldPosition)
    {
        if (!GameNetwork.HasStateAuthority || item == null || !item.InHand || item.Owner == null ||
            item.itemData?.Stack == null || !float.IsFinite(item.itemData.Stack.Amount) || item.itemData.Stack.Amount < 1f ||
            !float.IsFinite(worldPosition.x) || !float.IsFinite(worldPosition.y))
            return false;

        worldPosition = WorldTopologyRuntime.NormalizePosition(worldPosition);
        if (!TryResolveFarmland(worldPosition, out Vector2Int tilePos, out TileData_Farmland farmlandData))
        {
            Debug.LogWarning($"[Mod_FarmlandSupply] {worldPosition} 不是耕地，未消耗物品", item);
            return false;
        }
        if (!FarmlandSystem.IsWithinReach(item.Owner.transform.position, tilePos, 2f))
            return false;

        farmlandData.NormalizeValues();
        int previousWater = farmlandData.waterValue;
        float previousFertility = farmlandData.Fertility;
        farmlandData.AddWater(waterAmount);
        farmlandData.AddFertility(fertilityAmount);

        if (previousWater == farmlandData.waterValue &&
            Mathf.Approximately(previousFertility, farmlandData.Fertility))
        {
            Debug.Log($"[Mod_FarmlandSupply] 地块 {tilePos} 的水分和肥力已经充足，未消耗物品", item);
            return false;
        }

        FarmlandSystem.CommitSoil(farmlandData);
        item.itemData.Stack.Amount--;
        item.OnUIRefresh?.Invoke();
        Debug.Log(
            $"[Mod_FarmlandSupply] 地块 {tilePos} 补给完成：水分 {previousWater}→{farmlandData.waterValue}，" +
            $"肥力 {previousFertility:F2}→{farmlandData.Fertility:F2}",
            item);

        if (item.itemData.Stack.Amount <= 0)
            item.DestroySelf();
        return true;
    }

    private static bool TryResolveFarmland(
        Vector3 worldPosition,
        out Vector2Int tilePos,
        out TileData_Farmland farmlandData)
    {
        Vector3 normalized = WorldTopologyRuntime.NormalizePosition(worldPosition);
        tilePos = new Vector2Int(Mathf.FloorToInt(normalized.x), Mathf.FloorToInt(normalized.y));
        return FarmlandSystem.TryReadSoil(tilePos, out farmlandData);
    }

#endregion

#region 事件绑定

    /// <summary>统一从持有者读取鼠标、手柄或手机径向指向。</summary>
    private Mod_GameController ResolveOwnerController()
    {
        Item owner = item?.Owner;
        Mod_GameController controller = owner?.itemMods?.GetMod_ByID<Mod_GameController>(ModText.Controller);
        return controller != null ? controller : owner?.GetComponent<Mod_GameController>();
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
