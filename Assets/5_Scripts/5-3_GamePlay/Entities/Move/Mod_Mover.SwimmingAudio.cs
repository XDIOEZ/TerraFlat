using FlatWorld.Audio;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>本机角色复用真实液体接触和主动位移，播放入水、划水与出水短音。</summary>
public partial class Mod_Mover
{
    #region 游泳音效

    private const float SwimmingContactDepth = 0.03f;
    private const float SwimmingStrokeDistance = 1.4f;
    private const float SwimmingStrokeInterval = 0.5f;
    private const float SwimmingSubmergedImmersion = 0.95f;
    private const float SwimmingAudioFadeSeconds = 0.06f;

    private bool swimmingAudioEnabled;
    private Vector2 swimmingAudioPreviousPosition;
    private int swimmingAudioSceneHandle;
    private uint swimmingAudioContactGeneration;
    private Mod_TileEffectReceiver swimmingAudioTileEffects;
    private Mod_DamageReceiver swimmingAudioHealth;
    private SwimmingAudioState swimmingAudioState;
    private AudioHandle swimmingStrokeHandle = AudioHandle.Invalid;
    private AudioHandle swimmingTransitionHandle = AudioHandle.Invalid;

    /// <summary>状态只属于本角色；接触身份变化和划水节奏共用生产入口。</summary>
    internal struct SwimmingAudioState
    {
        public bool HasBaseline { get; private set; }
        public bool InWater { get; private set; }
        public float StrokeDistance { get; private set; }
        public float StrokeElapsed { get; private set; }

        public string Advance(float liquidDepth, float effectiveImmersion, bool waterContact,
            bool activelyMoving, float travelledDistance, float deltaTime)
        {
            bool inWater = waterContact && float.IsFinite(liquidDepth) && liquidDepth > SwimmingContactDepth;
            bool moving = activelyMoving && float.IsFinite(travelledDistance) && travelledDistance > 0f;
            if (!HasBaseline)
            {
                HasBaseline = true;
                InWater = inWater;
                return null;
            }

            if (InWater != inWater)
            {
                InWater = inWater;
                ResetStroke();
                return moving ? (inWater ? AudioEventIds.SwimEnter : AudioEventIds.SwimExit) : null;
            }

            if (!inWater || liquidDepth <= WaterEnvironmentRules.SwimmingDepthThreshold ||
                !float.IsFinite(effectiveImmersion) || effectiveImmersion >= SwimmingSubmergedImmersion || !moving)
            {
                ResetStroke();
                return null;
            }

            StrokeDistance += travelledDistance;
            StrokeElapsed += float.IsFinite(deltaTime) ? Mathf.Max(0f, deltaTime) : 0f;
            if (StrokeDistance < SwimmingStrokeDistance || StrokeElapsed < SwimmingStrokeInterval)
                return null;

            // 每次最多发一划，避免高速或卡帧后挤出连续水声。
            StrokeDistance = Mathf.Min(StrokeDistance - SwimmingStrokeDistance, SwimmingStrokeDistance);
            StrokeElapsed = 0f;
            return AudioEventIds.SwimStroke;
        }

        public void Reset() => this = default;

        private void ResetStroke()
        {
            StrokeDistance = 0f;
            StrokeElapsed = 0f;
        }
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

        bool canStroke = waterContact && liquidDepth > WaterEnvironmentRules.SwimmingDepthThreshold &&
                         swimmingAudioTileEffects.CurrentWaterImmersion < SwimmingSubmergedImmersion &&
                         activelyMoving && drivenDistance > 0f;
        if (!canStroke)
            StopSwimmingStroke();
        if (cueId == null)
            return;

        if (cueId == AudioEventIds.SwimStroke)
        {
            StopSwimmingStroke();
            swimmingStrokeHandle = AudioService.Instance.PlayAttached(cueId, item.transform, 0.5f);
            return;
        }

        StopSwimmingStroke();
        swimmingTransitionHandle.Stop(SwimmingAudioFadeSeconds);
        swimmingTransitionHandle = AudioService.Instance.PlayAttached(cueId, item.transform, 0.55f);
    }

    /// <summary>传送、换世界与承载切换静默清基线，不补播旧位置的入水或出水。</summary>
    internal void ResetSwimmingAudio()
    {
        swimmingAudioState.Reset();
        StopSwimmingStroke();
        swimmingTransitionHandle.Stop(SwimmingAudioFadeSeconds);
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

    private void StopSwimmingStroke()
    {
        swimmingStrokeHandle.Stop(SwimmingAudioFadeSeconds);
        swimmingStrokeHandle = AudioHandle.Invalid;
    }

    #endregion
}
