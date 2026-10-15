using FlatWorld.Audio;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>本地玩家以真实位移计步，按脚下地表材质播放短音。</summary>
public partial class Mod_Mover
{
    #region 地表脚步音效

    private const float WalkingStride = 2.3f;
    private const float RunningStride = 2.9f;
    private const float MaximumStepDisplacement = 1.6f;
    private const float DeepWaterThreshold = 0.55f;

    private bool footstepAudioEnabled;
    private Vector2 footstepPreviousPosition;
    private float footstepDistance;

    private void InitializeFootstepAudio()
    {
        footstepAudioEnabled = item is Player player && player.IsLocalProfile && rb != null;
        footstepPreviousPosition = rb != null ? WorldTopologyRuntime.NormalizePosition(rb.position) : Vector2.zero;
        footstepDistance = 0f;
    }

    private void TickFootstepAudio()
    {
        if (!footstepAudioEnabled || rb == null)
            return;

        Vector2 currentPosition = WorldTopologyRuntime.NormalizePosition(rb.position);
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(footstepPreviousPosition, currentPosition);
        footstepPreviousPosition = currentPosition;

        // 风、水流、载具、传送与失重漂移都不是主动踩地，不能触发脚步。
        if (!Enabled || item == null || item.DestructionHandled || !IsMoving ||
            CarrierSource != null || spaceMotionOwned ||
            SpaceSession.Current?.IsSpaceView == true ||
            !rb.simulated || rb.bodyType != RigidbodyType2D.Dynamic ||
            DrivenVelocity.sqrMagnitude <= 0.16f ||
            !float.IsFinite(displacement.x) || !float.IsFinite(displacement.y) ||
            displacement.sqrMagnitude > MaximumStepDisplacement * MaximumStepDisplacement)
        {
            footstepDistance = 0f;
            return;
        }

        footstepDistance += displacement.magnitude;
        float stride = IsRunning ? RunningStride : WalkingStride;
        if (footstepDistance < stride)
            return;

        footstepDistance -= stride;
        string cueId = SampleFootstepCue(currentPosition);
        if (cueId != null)
            AudioService.Instance.PlayAttached(cueId, item.transform, IsRunning ? 0.55f : 0.42f);
    }

    private static string SampleFootstepCue(Vector2 position)
    {
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks == null || !chunks.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample sample))
            return null;

        float waterDepth = sample.LiquidDepth;
        float snowDepth = 0f;
        if (waterDepth <= DeepWaterThreshold)
        {
            SnowCoverState seasonalSnow = null;
            float baselineOffset = 0f;
            WeatherMgr.ExistingInstance?.TryGetSnowCoverageContext(out seasonalSnow, out baselineOffset);
            snowDepth = WorldSnowSystem.GetSurfaceDepth(
                sample.Terrain, sample.LocalCell.x, sample.LocalCell.y,
                seasonalSnow, baselineOffset);
        }
        bool onSupport = TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) != 0;
        byte grass = sample.Terrain.GetGrass(sample.LocalCell.x, sample.LocalCell.y);
        return ResolveFootstepCue(sample.Cell.BiomeId, waterDepth, snowDepth, onSupport, grass);
    }

    // 纯地表选择规则与声源播放分开，可由 Lua 规则直接校验边界。
    internal static string ResolveFootstepCue(
        int biomeId, float waterDepth, float snowDepth, bool hasSupport, byte grass)
    {
        if (waterDepth > DeepWaterThreshold)
            return null;
        if (waterDepth > .03f)
            return AudioEventIds.FootstepWater;
        if (snowDepth > .05f)
            return AudioEventIds.FootstepSnow;
        if (hasSupport || biomeId == (int)SurfaceBiomeKind.Stone)
            return AudioEventIds.FootstepStone;
        if (biomeId == (int)SurfaceBiomeKind.Desert ||
            biomeId == (int)SurfaceBiomeKind.Beach)
            return AudioEventIds.FootstepSand;
        if (grass == ChunkTerrainData.GrassPresent)
            return AudioEventIds.FootstepGrass;
        return AudioEventIds.FootstepDirt;
    }

    #endregion
}
