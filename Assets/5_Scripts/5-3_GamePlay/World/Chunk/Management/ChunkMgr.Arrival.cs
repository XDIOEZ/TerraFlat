using UnityEngine;

public partial class ChunkMgr
{
    #region 玩家到达的严格窗口检查

    // 到达门禁必须匹配目标中心和实际视距，空窗口及旧窗口不能继承专用服务器的免等待语义。
    public bool HasRuntimeArrivalWindow(Vector2 position, Vector2Int loadDistance)
    {
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) ||
            !IsSurfaceStreamingScene || !runtimeWindowUsesLocalPresentation ||
            runtimeWindowTargets.Count == 0 || runtimeReadyTargets.Count == 0 ||
            runtimeWindowDimensionId != ResolveCurrentDimensionId() || ActiveGenerationProfile == null)
            return false;
        Vector2Int size = new(ActiveGenerationProfile.Width, ActiveGenerationProfile.Height);
        Rect required = CalculateRuntimeWindowBounds(ResolveRuntimeChunkOrigin(position), size, loadDistance);
        return runtimeActiveWindowBounds == required;
    }

    public bool IsRuntimeArrivalPresentationReady(Vector2 position, Vector2Int loadDistance) =>
        HasRuntimeArrivalWindow(position, loadDistance) &&
        TryGetRuntimeChunkView(position, out _) && AreRuntimeWindowPresentationsReady;

    #endregion
}
