using System;
using FlatWorld.Audio;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using UnityEngine;

/// <summary>角色移动音效集中管理脚步、游泳与潮湿衣物，位移基线由各自状态持有。</summary>
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
        // 生物使用自身位移发声，玩家仍只由本地实例播放。
        footstepAudioEnabled = rb != null && (item is not Player player || player.IsLocalProfile);
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
        int supportTileId = TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y);
        bool onSupport = supportTileId != 0;
        string surfaceCue = null;
        if (GameRes.ExistingInstance?.TryGetTileDefinition(
                onSupport ? supportTileId : sample.Cell.GroundTileId, out RuntimeTileDefinition definition) == true)
            surfaceCue = definition.FootstepCueId;
        byte grass = sample.Terrain.GetGrass(sample.LocalCell.x, sample.LocalCell.y);
        string cueId = ResolveFootstepCue(sample.Cell.BiomeId, waterDepth, snowDepth, onSupport, grass, surfaceCue);
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
        int biomeId, float waterDepth, float snowDepth, bool hasSupport, byte grass, string surfaceCue = null)
    {
        if (waterDepth > DeepWaterThreshold)
            return null;
        if (waterDepth > .03f)
            return AudioEventIds.FootstepWater;
        if (snowDepth > .05f)
            return AudioEventIds.FootstepSnow;
        if (!string.IsNullOrWhiteSpace(surfaceCue))
            return surfaceCue;
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

    #region 游泳音效

    private const float SwimmingContactDepth = 0.03f;
    private const float SwimmingSubmergedImmersion = 0.95f;
    private const float SwimmingAudioFadeInSeconds = 0.25f;
    private const float SwimmingAudioFadeOutSeconds = 0.35f;
    private const float SwimmingTransitionFadeSeconds = 0.06f;
    private const float WaterAudioDepthChangeSeconds = 0.2f;

    private bool swimmingAudioEnabled;
    private Vector2 swimmingAudioPreviousPosition;
    private int swimmingAudioSceneHandle;
    private uint swimmingAudioContactGeneration;
    private Mod_TileEffectReceiver swimmingAudioTileEffects;
    private Mod_DamageReceiver swimmingAudioHealth;
    private SwimmingAudioState swimmingAudioState;
    private AudioHandle swimmingStrokeHandle = AudioHandle.Invalid;
    private AudioHandle swimmingTransitionHandle = AudioHandle.Invalid;

    // 一至十份水共用低高低曲线，两端保留一成音量，五点五份达到原音量。
    internal static float ResolveWaterAudioVolume(float liquidDepth)
    {
        if (!float.IsFinite(liquidDepth) || liquidDepth <= 0f)
            return 0f;

        float depth = Mathf.InverseLerp(0.1f, 1f, liquidDepth);
        return Mathf.Lerp(0.1f, 1f, 4f * depth * (1f - depth));
    }

    /// <summary>状态只属于本角色；游动控制循环目标音量，不再按格子重复触发。</summary>
    internal struct SwimmingAudioState
    {
        public bool HasBaseline { get; private set; }
        public bool InWater { get; private set; }
        public bool ShouldPlayLoop { get; private set; }
        public float LoopVolume { get; private set; }
        public float WaterVolume { get; private set; }
        public float TransitionVolume { get; private set; }
        private float previousWaterVolume;

        public string Advance(float liquidDepth, float effectiveImmersion, bool waterContact,
            bool activelyMoving, float travelledDistance, float deltaTime)
        {
            bool inWater = waterContact && float.IsFinite(liquidDepth) && liquidDepth > SwimmingContactDepth;
            bool moving = activelyMoving && float.IsFinite(travelledDistance) && travelledDistance > 0f;
            float depthVolume = inWater ? ResolveWaterAudioVolume(liquidDepth) : 0f;
            TransitionVolume = 0f;
            if (!HasBaseline)
            {
                HasBaseline = true;
                InWater = inWater;
                WaterVolume = depthVolume;
                previousWaterVolume = depthVolume;
                return null;
            }

            bool changedWater = InWater != inWater;
            if (changedWater && moving)
                TransitionVolume = inWater ? depthVolume : previousWaterVolume;
            InWater = inWater;
            ShouldPlayLoop = !changedWater && inWater &&
                             liquidDepth > WaterEnvironmentRules.SwimmingDepthThreshold &&
                             float.IsFinite(effectiveImmersion) && effectiveImmersion < SwimmingSubmergedImmersion && moving;
            float elapsed = float.IsFinite(deltaTime) ? Mathf.Max(0f, deltaTime) : 0f;
            if (inWater)
            {
                previousWaterVolume = depthVolume;
                WaterVolume = Mathf.MoveTowards(WaterVolume, depthVolume, elapsed / WaterAudioDepthChangeSeconds);
            }
            // 离水保留最后的水量倍率，让循环尾声继续淡出，出水声也取离开前的水格。
            float fadeSeconds = ShouldPlayLoop ? SwimmingAudioFadeInSeconds : SwimmingAudioFadeOutSeconds;
            // 淡出期间保留同一条循环，快速停走时从当前音量续上，避免接缝和重叠水花。
            LoopVolume = Mathf.MoveTowards(LoopVolume, ShouldPlayLoop ? 1f : 0f, elapsed / fadeSeconds);
            return changedWater && moving ? (inWater ? AudioEventIds.SwimEnter : AudioEventIds.SwimExit) : null;
        }

        public void Reset() => this = default;
    }

    private void InitializeSwimmingAudio()
    {
        DisposeSwimmingAudio();
        swimmingAudioEnabled = item is Player player && player.IsLocalProfile && rb != null;
        if (!swimmingAudioEnabled)
            return;

        swimmingAudioTileEffects = item.itemMods?.GetMod_ByID<Mod_TileEffectReceiver>(ModText.Mod_TileEffectReceiver);
        swimmingAudioHealth = item.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        ResetSwimmingAudio();
    }

    private void TickSwimmingAudio(in MotionAudioSpatialFrame frame)
    {
        if (!swimmingAudioEnabled || rb == null)
            return;

        Vector2 currentPosition = frame.Position;
        Vector2 displacement = frame.DeltaFrom(swimmingAudioPreviousPosition);
        swimmingAudioPreviousPosition = currentPosition;

        bool contactAvailable = swimmingAudioTileEffects != null && swimmingAudioTileEffects.WaterContactPresentationAvailable;
        if (!Enabled || !IsRuntimeLoaded || item == null || item.DestructionHandled ||
            swimmingAudioHealth == null || swimmingAudioHealth.Hp <= 0f ||
            CarrierSource != null || spaceMotionOwned || SpaceSession.Current?.IsSpaceView == true ||
            !rb.simulated || rb.bodyType != RigidbodyType2D.Dynamic || !contactAvailable ||
            item.gameObject.scene.handle != swimmingAudioSceneHandle ||
            swimmingAudioTileEffects.RuntimeGeneration != swimmingAudioContactGeneration ||
            !float.IsFinite(displacement.x) || !float.IsFinite(displacement.y) ||
            displacement.sqrMagnitude > MaximumStepDisplacement * MaximumStepDisplacement)
        {
            ResetSwimmingAudio();
            return;
        }

        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks == null || !chunks.TryGetRuntimeTerrainTile(currentPosition, out RuntimeTerrainTileSample sample))
        {
            ResetSwimmingAudio();
            return;
        }

        // 平台后的有效液深与 WaterContact 来自真实 Liquid 层，岸边交互和岩浆不会冒充入水。
        float liquidDepth = sample.LiquidDepth;
        bool waterContact = liquidDepth > 0f &&
                            WorldLiquidSystem.TryGetDefinition(sample, out LiquidDefinition liquid) &&
                            liquid.WorldWater.WaterContact;
        bool activelyMoving = !IsLock && inputController?.IsGameplayInputLocked != true && IsMoving &&
                              DrivenVelocity.sqrMagnitude > 0.16f &&
                              RequestedMoveInput.sqrMagnitude > InputMoveThresholdSqr;
        float drivenDistance = activelyMoving
            ? Mathf.Min(displacement.magnitude, DrivenVelocity.magnitude * Time.fixedDeltaTime)
            : 0f;
        string cueId = swimmingAudioState.Advance(liquidDepth,
            swimmingAudioTileEffects.CurrentWaterImmersion, waterContact,
            activelyMoving, drivenDistance, Time.fixedDeltaTime);

        if (swimmingAudioState.LoopVolume > 0f)
        {
            float volume = 0.5f * swimmingAudioState.LoopVolume * swimmingAudioState.WaterVolume;
            if (!swimmingStrokeHandle.IsPlaying)
                swimmingStrokeHandle = AudioService.Instance.PlayAttached(AudioEventIds.SwimStroke, item.transform, volume);
            else
                AudioService.Instance.SetVolumeScale(swimmingStrokeHandle, volume);
        }
        else
            StopSwimmingStroke(0f);

        if (cueId == null)
            return;

        swimmingTransitionHandle.Stop(SwimmingTransitionFadeSeconds);
        swimmingTransitionHandle = AudioService.Instance.PlayAttached(
            cueId, item.transform, 0.55f * swimmingAudioState.TransitionVolume);
    }

    /// <summary>传送、换世界与承载切换静默清基线，不补播旧位置的入水或出水。</summary>
    internal void ResetSwimmingAudio()
    {
        swimmingAudioState.Reset();
        StopSwimmingStroke();
        swimmingTransitionHandle.Stop(SwimmingTransitionFadeSeconds);
        swimmingTransitionHandle = AudioHandle.Invalid;
        swimmingAudioPreviousPosition = rb != null
            ? WorldTopologyRuntime.NormalizePosition(rb.position)
            : Vector2.zero;
        swimmingAudioSceneHandle = item != null ? item.gameObject.scene.handle : 0;
        swimmingAudioContactGeneration = swimmingAudioTileEffects != null ? swimmingAudioTileEffects.RuntimeGeneration : 0;
    }

    private void DisposeSwimmingAudio()
    {
        ResetSwimmingAudio();
        swimmingAudioEnabled = false;
        swimmingAudioTileEffects = null;
        swimmingAudioHealth = null;
    }

    private void StopSwimmingStroke(float fadeSeconds = SwimmingAudioFadeOutSeconds)
    {
        swimmingStrokeHandle.Stop(fadeSeconds);
        swimmingStrokeHandle = AudioHandle.Invalid;
    }

    #endregion

    #region 潮湿衣物音效

    private const float WetClothesAudioFadeSeconds = 0.08f;
    private const int WetClothesFallbackMaxStacks = 10;

    private Mod_BuffManager wetClothesBuffManager;
    private Mod_Equipment wetClothesEquipment;
    private AudioHandle wetClothesAudioHandle = AudioHandle.Invalid;
    private int wetClothesAppliedStacks;

    private void InitializeWetClothesAudio()
    {
        DisposeWetClothesAudio();

        if (item is not Player player || !player.IsLocalProfile)
            return;

        wetClothesBuffManager = item.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
        wetClothesEquipment = item.itemMods?.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);

        if (wetClothesBuffManager != null)
        {
            wetClothesBuffManager.BuffAdded += HandleWetClothesBuffChanged;
            wetClothesBuffManager.BuffRemoved += HandleWetClothesBuffChanged;
            wetClothesBuffManager.BuffStacksChanged += HandleWetClothesBuffChanged;
        }

        if (wetClothesEquipment != null)
            wetClothesEquipment.EquipmentChanged += HandleWetClothesEquipmentChanged;

        RefreshWetClothesAudio();
    }

    private void DisposeWetClothesAudio()
    {
        if (wetClothesBuffManager != null)
        {
            wetClothesBuffManager.BuffAdded -= HandleWetClothesBuffChanged;
            wetClothesBuffManager.BuffRemoved -= HandleWetClothesBuffChanged;
            wetClothesBuffManager.BuffStacksChanged -= HandleWetClothesBuffChanged;
        }

        if (wetClothesEquipment != null)
            wetClothesEquipment.EquipmentChanged -= HandleWetClothesEquipmentChanged;

        StopWetClothesAudio();
        wetClothesBuffManager = null;
        wetClothesEquipment = null;
    }

    private void HandleWetClothesEquipmentChanged()
    {
        RefreshWetClothesAudio();
    }

    private void HandleWetClothesBuffChanged(BuffInstance runtime)
    {
        if (runtime == null ||
            !string.Equals(runtime.DefinitionId, WetBuffIds.Wet, StringComparison.OrdinalIgnoreCase))
            return;

        RefreshWetClothesAudio();
    }

    private void RefreshWetClothesAudio()
    {
        int wetStacks = ResolveWetClothesStacks(out int maxStacks);
        bool shouldPlay = IsMoving &&
                          wetStacks > 0 &&
                          wetClothesEquipment != null &&
                          wetClothesEquipment.HasAnyEquippedItem();

        if (!shouldPlay)
        {
            StopWetClothesAudio();
            return;
        }

        if (wetClothesAudioHandle.IsPlaying && wetClothesAppliedStacks == wetStacks)
            return;

        StopWetClothesAudio();

        float wetness = maxStacks <= 1
            ? 1f
            : Mathf.InverseLerp(1f, maxStacks, wetStacks);
        float volumeScale = Mathf.Lerp(0.48f, 1.12f, wetness);
        float pitchScale = Mathf.Lerp(1.03f, 0.84f, wetness);
        AudioPlayOptions options = AudioPlayOptions.Attached(item.transform, volumeScale, pitchScale);
        options.OverrideLoop = true;
        options.Loop = true;
        options.FadeIn = WetClothesAudioFadeSeconds;

        wetClothesAudioHandle = AudioService.Instance.Play(
            AudioEventIds.PlayerWetClothesMoveLoop,
            options);
        wetClothesAppliedStacks = wetClothesAudioHandle.IsValid ? wetStacks : 0;
    }

    private int ResolveWetClothesStacks(out int maxStacks)
    {
        maxStacks = WetClothesFallbackMaxStacks;
        if (wetClothesBuffManager == null ||
            !wetClothesBuffManager.TryGetBuff(WetBuffIds.Wet, out BuffInstance wetBuff) ||
            wetBuff == null)
            return 0;

        maxStacks = Mathf.Max(1, wetBuff.Definition?.MaxStacks ?? WetClothesFallbackMaxStacks);
        return Mathf.Clamp(wetBuff.StackCount, 0, maxStacks);
    }

    private void StopWetClothesAudio()
    {
        if (wetClothesAudioHandle.IsValid)
            wetClothesAudioHandle.Stop(WetClothesAudioFadeSeconds);

        wetClothesAudioHandle = AudioHandle.Invalid;
        wetClothesAppliedStacks = 0;
    }

    #endregion
}
