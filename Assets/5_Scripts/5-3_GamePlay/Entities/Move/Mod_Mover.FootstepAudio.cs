using FlatWorld.Audio;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using UnityEngine;

/// <summary>本地玩家以真实位移计步，按脚下地表材质播放短音。</summary>
public partial class Mod_Mover
{
    #region 位移音效共用空间快照

    internal readonly struct MotionAudioSpatialFrame
    {
        private readonly WorldTopologyDomain topology;
        public readonly Vector2 Position;

        public MotionAudioSpatialFrame(Vector2 position, in WorldTopologyDomain topology)
        {
            this.topology = topology;
            float2 normalized = topology.Normalize(new float2(position.x, position.y));
            Position = new Vector2(normalized.x, normalized.y);
        }

        // 脚步和游泳各自保留上帧基线，只复用本帧位置与坐标域。
        public Vector2 DeltaFrom(Vector2 previous)
        {
            float2 delta = topology.ShortestDelta(new float2(previous.x, previous.y),
                new float2(Position.x, Position.y));
            return new Vector2(delta.x, delta.y);
        }
    }

    #endregion

    #region 地表脚步音效

    private const float WalkingStride = 2.3f;
    private const float RunningStride = 2.9f;
    // 缩短实际计步距离并压低单次鼓点，保留各地表原有音色。
    private const float FootstepFrequencyMultiplier = 1.6f;
    private const float FootstepVolumeMultiplier = 0.6f;
    private const float MaximumStepDisplacement = 1.6f;
    private const float DeepWaterThreshold = WaterEnvironmentRules.SwimmingDepthThreshold;
    private const float ShallowSnowVolume = 0.45f;
    private const float DeepSnowVolume = 1.6f;
    private const float ShallowSnowPitch = 1.1f;
    private const float DeepSnowPitch = 0.86f;

    private bool footstepAudioEnabled;
    private Vector2 footstepPreviousPosition;
    private float footstepDistance;

    private void InitializeFootstepAudio()
    {
        footstepAudioEnabled = item is Player player && player.IsLocalProfile && rb != null;
        footstepPreviousPosition = rb != null ? WorldTopologyRuntime.NormalizePosition(rb.position) : Vector2.zero;
        footstepDistance = 0f;
    }

    private void TickFootstepAudio(in MotionAudioSpatialFrame frame)
    {
        if (!footstepAudioEnabled || rb == null)
            return;

        Vector2 currentPosition = frame.Position;
        Vector2 displacement = frame.DeltaFrom(footstepPreviousPosition);
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
        float stride = (IsRunning ? RunningStride : WalkingStride) / FootstepFrequencyMultiplier;
        if (footstepDistance < stride)
            return;

        footstepDistance -= stride;
        string cueId = SampleFootstepCue(currentPosition, out float depthVolume, out float depthPitch);
        if (cueId != null)
            AudioService.Instance.PlayAttached(
                cueId, item.transform, (IsRunning ? 0.55f : 0.42f) * FootstepVolumeMultiplier * depthVolume,
                depthPitch);
    }

    private static string SampleFootstepCue(Vector2 position, out float depthVolume, out float depthPitch)
    {
        depthVolume = 1f;
        depthPitch = 1f;
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
        string cueId = ResolveFootstepCue(sample.Cell.BiomeId, waterDepth, snowDepth, onSupport, grass);
        depthVolume = ResolveFootstepDepthVolume(cueId, waterDepth, snowDepth);
        depthPitch = ResolveFootstepDepthPitch(cueId, snowDepth);
        return cueId;
    }

    // 十档雪深逐级加重压雪声，水和其他材质仍走各自原有音量规则。
    internal static float ResolveFootstepDepthVolume(string cueId, float waterDepth, float snowDepth = 0f)
    {
        if (cueId == AudioEventIds.FootstepWater)
            return ResolveWaterAudioVolume(waterDepth);
        if (cueId != AudioEventIds.FootstepSnow)
            return 1f;
        float depth = SnowDepthLayer.Quantize(snowDepth);
        return depth > 0f
            ? Mathf.Lerp(ShallowSnowVolume, DeepSnowVolume, Mathf.InverseLerp(SnowDepthLayer.LayerStep, 1f, depth))
            : 0f;
    }

    // 深雪适度降低音高并拉长嘎吱声，超过十层只封顶声音表现。
    internal static float ResolveFootstepDepthPitch(string cueId, float snowDepth)
    {
        if (cueId != AudioEventIds.FootstepSnow)
            return 1f;
        float depth = SnowDepthLayer.Quantize(snowDepth);
        return depth > 0f
            ? Mathf.Lerp(ShallowSnowPitch, DeepSnowPitch, Mathf.InverseLerp(SnowDepthLayer.LayerStep, 1f, depth))
            : 1f;
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
