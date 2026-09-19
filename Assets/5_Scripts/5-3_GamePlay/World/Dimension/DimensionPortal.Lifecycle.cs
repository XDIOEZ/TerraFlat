using FlatWorld.Networking;
using UnityEngine;

public sealed partial class DimensionPortal
{
    #region 封堵与修复

    private DamageReceiver entranceHealth;
    private Color unblockedTint;
    private bool tintCaptured;
    public bool IsBlockedExit { get; private set; }

    private void BindEntranceLifecycle()
    {
        UnbindEntranceLifecycle();
        entranceHealth = portalItem?.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (entranceHealth != null) entranceHealth.DeathStarted += OnEntranceDestroyed;
        if (portalItem?.Sprite != null)
        {
            unblockedTint = portalItem.Sprite.color;
            tintCaptured = true;
        }
        if (generatedWorldPortal || (building != null && building.IsInstalled())) OnBuildingPlacementCommitted();
        RefreshBlockedState();
    }

    private void UnbindEntranceLifecycle()
    {
        if (entranceHealth != null) entranceHealth.DeathStarted -= OnEntranceDestroyed;
        entranceHealth = null;
        if (tintCaptured && portalItem?.Sprite != null) portalItem.Sprite.color = unblockedTint;
        tintCaptured = false;
        IsBlockedExit = false;
    }

    public void OnBuildingPlacementCommitted()
    {
        CachePortalContext();
        WorldAddress source = DimensionManager.ExistingInstance?.ActiveAddress ?? default;
        if (!GameNetwork.HasStateAuthority || !source.IsSurface || portalItem == null ||
            targetDimensionId != WorldAddress.CaveDimensionId || (entranceHealth != null && entranceHealth.Hp <= 0f)) return;
        DimensionTravelProgressStore.SetSurfaceEntranceState(SaveDataMgr.Instance?.SaveData, source,
            portalItem.transform.position, blocked: false, generated: generatedWorldPortal);
    }

    private void OnEntranceDestroyed(DamageReceiver receiver)
    {
        WorldAddress source = DimensionManager.ExistingInstance?.ActiveAddress ?? default;
        if (!GameNetwork.HasStateAuthority || !source.IsSurface || portalItem == null ||
            targetDimensionId != WorldAddress.CaveDimensionId) return;
        DimensionTravelProgressStore.SetSurfaceEntranceState(SaveDataMgr.Instance?.SaveData, source,
            portalItem.transform.position, blocked: true, generated: generatedWorldPortal);
    }

    /// <summary>地表矿洞入口被打坏时应真正销毁并掉落石料，不能按普通建筑拆回完整召唤器。</summary>
    public bool UseDamageReceiverFatalResolution(DamageReceiver receiver)
    {
        WorldAddress source = DimensionManager.ExistingInstance?.ActiveAddress ?? default;
        return source.IsSurface && portalItem != null &&
            targetDimensionId == WorldAddress.CaveDimensionId;
    }

    /// <summary>封堵是保存下来的旅行状态，不通过查找当前场景里是否存在地表入口来猜测。</summary>
    private void RefreshBlockedState()
    {
        WorldAddress source = DimensionManager.ExistingInstance?.ActiveAddress ?? default;
        IsBlockedExit = targetDimensionId == WorldAddress.SurfaceDimensionId &&
            DimensionTravelProgressStore.IsCaveExitBlocked(SaveDataMgr.Instance?.SaveData, source, portalItem, generatedWorldPortal);
        if (tintCaptured && portalItem?.Sprite != null)
            portalItem.Sprite.color = IsBlockedExit ? new Color(unblockedTint.r * 0.45f, unblockedTint.g * 0.45f,
                unblockedTint.b * 0.45f, unblockedTint.a) : unblockedTint;
    }

    #endregion
}
