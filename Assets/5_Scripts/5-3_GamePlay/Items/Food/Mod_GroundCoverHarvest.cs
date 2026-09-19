using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>
/// 剪刀等地表植被采集工具的通用模块。复用 Item.OnAct 和陶罐的地格白框，
/// 键鼠右键、手机使用按钮走同一目标解析；默认距离为 2 格，不为花朵添加 Collider 或常驻 Item。
/// </summary>
public sealed class Mod_GroundCoverHarvest : Module
{
    #region 数据与配置

    public const string ModuleId = "Mod_GroundCoverHarvest";
    public Ex_ModData ModData = new(); // 工具模块身份；本模块没有独立存档状态。
    [Min(0.1f), Tooltip("从角色位置到目标地格的最大采集距离。")]
    public float reach = 2f;
    [Tooltip("可选草层产物 ID；留空的工具只采集花朵等独立植被。")]
    public string grassYieldItemId = "";
    [Min(1)] public int grassYieldAmount = 2;
    private WorldTileTargetOutline targetOutline; // 仅当前本地手持工具拥有的白色目标框。
    private bool actBound; // 防止重复 Load 订阅同一使用事件。
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("地表植被采集模块数据类型错误。");
    }

    #endregion

    #region 生命周期

    /// <summary>初始化稳定模块身份后进入通用模块生命周期。</summary>
    public override void Awake()
    {
        ModData.ID = ModuleId;
        base.Awake();
    }

    /// <summary>绑定工具使用入口；采集状态由世界持有，不在工具中恢复。</summary>
    public override void Load()
    {
        if (reach <= 0f || float.IsNaN(reach) || float.IsInfinity(reach))
            throw new InvalidOperationException("地表植被采集距离必须是有限正数。");
        if (actBound) return;
        item.OnAct += Act;
        actBound = true;
    }

    /// <summary>本模块无独立运行态，自动保存不解除输入监听。</summary>
    public override void Save() { }

    /// <summary>换手、回池或卸载时解除动作绑定并释放白框。</summary>
    public override void Unload()
    {
        if (actBound && item != null) item.OnAct -= Act;
        actBound = false;
        ReleaseOutline();
    }

    /// <summary>只更新手持工具的目标表现，不启用物品 Tick。</summary>
    private void LateUpdate()
    {
        if (!TryResolveTarget(out GroundCoverTarget target, out RuntimeTerrainTileSample grass, out bool cuttingGrass))
        {
            targetOutline?.Hide();
            return;
        }
        targetOutline ??= WorldTileTargetOutline.Create("Ground Cover Target Outline");
        targetOutline.Show(cuttingGrass ? grass.WorldCell : target.WorldCell);
    }

    /// <summary>禁用期间不保留独立于手持物的目标框。</summary>
    private void OnDisable() => ReleaseOutline();

    /// <summary>销毁路径与回池路径使用同一清理入口。</summary>
    private void OnDestroy() => Unload();

    #endregion

    #region 使用与选格

    /// <summary>重新解析当前白框所使用的目标并提交一次采集。</summary>
    public override void Act()
    {
        if (!TryResolveTarget(out GroundCoverTarget target, out RuntimeTerrainTileSample grass, out bool cuttingGrass))
            return;
        if (cuttingGrass)
        {
            // 先准备真实掉落，再消费草层；失败不清草，连续使用也不会重复出货。
            DroppedItemHandle product = DroppedItemService.SpawnLoot(grassYieldItemId,
                (Vector2)grass.WorldCell + Vector2.one * 0.5f, grassYieldAmount);
            if (!RuntimeGrassClearing.Clear(grass)) DroppedItemService.Remove(product);
            targetOutline?.Hide();
        }
        else if (GroundCoverSystem.TryHarvest(target))
        {
            targetOutline?.Hide();
            ItemActionFeedback.Show(item.Owner, $"已采集{target.Definition.DisplayName}。");
        }
    }

    /// <summary>白框与实际采集共用手持、存活、输入锁、UI 遮挡、地格和环绕距离判定。</summary>
    private bool TryResolveTarget(out GroundCoverTarget target, out RuntimeTerrainTileSample grass, out bool cuttingGrass)
    {
        target = default;
        grass = default;
        cuttingGrass = false;
        if (!GameNetwork.HasStateAuthority || item == null || !item.InHand || item.DestructionHandled ||
            item.Owner is not Player actor || !actor.IsLocalProfile || actor.DestructionHandled ||
            !(actor.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp)?.Hp > 0f))
            return false;

        GameController controller = actor.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (controller == null || controller.IsGameplayInputLocked ||
            (!controller.IsUsingMobile && controller.IsPointerOverUI()))
            return false;

        Vector2 pointer = controller.GetMouseWorldPosition();
        if (!string.IsNullOrWhiteSpace(grassYieldItemId) && ChunkMgr.ExistingInstance != null &&
            ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(pointer, out grass) &&
            FarmlandSystem.IsOpen(grass) && grass.Terrain.GetGrass(grass.LocalCell.x, grass.LocalCell.y) > 0 &&
            FarmlandSystem.IsWithinReach(actor.transform.position, grass.WorldCell, reach))
        {
            cuttingGrass = true;
            return true;
        }
        if (!GroundCoverSystem.TryResolve(pointer, out target)) return false;

        return FarmlandSystem.IsWithinReach(actor.transform.position, target.WorldCell, reach) &&
            !BuildingOccupancyRegistry.IsOccupied(target.WorldCell);
    }

    /// <summary>释放目标框及其自有材质，不影响陶罐等其他工具。</summary>
    private void ReleaseOutline()
    {
        if (targetOutline == null) return;
        Destroy(targetOutline.gameObject);
        targetOutline = null;
    }

    #endregion
}
