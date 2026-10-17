using FlatWorld.Audio;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>本机角色复用真实液体接触和主动位移，播放入出水短音与连续游泳水声。</summary>
public partial class Mod_Mover
{
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

    private void TickSwimmingAudio()
    {
        if (!swimmingAudioEnabled || rb == null)
            return;

        Vector2 currentPosition = WorldTopologyRuntime.NormalizePosition(rb.position);
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(swimmingAudioPreviousPosition, currentPosition);
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
}
