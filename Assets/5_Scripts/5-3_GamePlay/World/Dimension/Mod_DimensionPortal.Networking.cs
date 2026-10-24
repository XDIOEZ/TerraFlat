using System;
using System.Collections;
using UnityEngine;

public sealed partial class Mod_DimensionPortal
{
    #region 联机入口校验与失败释放
    public bool ValidateNetworkTransition(Player actor, WorldAddress source, string target, bool generated)
    {
        CachePortalContext();
        if (!runtimeLoaded || !isActiveAndEnabled || portalItem == null || portalItem.InHand ||
            !string.Equals(targetDimensionId, target, StringComparison.Ordinal) ||
            building != null && (building.IsSummoner || !building.IsInstalled()) ||
            requiresInstalledBuilding && building == null) return false;
        bool actualGenerated = generatedWorldPortal || source.IsSurface &&
            target == WorldAddress.CaveDimensionId &&
            DimensionTravelProgressStore.WasGeneratedSurfaceEntrance(SaveDataMgr.Instance?.SaveData, source,
                WorldLocalPresentation.ToLogical(portalItem.transform.position));
        return actualGenerated == generated && !DimensionTravelProgressStore.IsCaveExitBlocked(
            SaveDataMgr.Instance?.SaveData, source, portalItem, actualGenerated);
    }

    // 源入口会继续服务其他玩家，成功或失败后都释放门禁，并覆盖同步回调早于赋值的情况。
    public void CancelNetworkTransitionRequest()
    {
        // 异步网络回执可能晚于入口销毁，避免访问已释放的 Unity 组件。
        if (this == null) return;
        transitionRequested = false;
        if (isActiveAndEnabled) StartCoroutine(ReleaseNetworkRequestNextFrame());
    }

    private IEnumerator ReleaseNetworkRequestNextFrame()
    {
        yield return null;
        transitionRequested = false;
    }
    #endregion
}
