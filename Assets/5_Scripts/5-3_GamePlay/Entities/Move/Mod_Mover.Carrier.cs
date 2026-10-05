using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>玩家移动的承载仲裁；临时禁止刚体积分并保留原父级、刚体状态和登船位置租约。</summary>
public partial class Mod_Mover
{
    #region 承载租约
    public ICarrierMotionSource CarrierSource { get; private set; } // 唯一移动权威，不保存引用。
    private Mod_TileEffectReceiver carrierTileEffects;
    private Mod_DamageReceiver carrierHealth;
    private GameManager carrierGame;
    private Vector2 carrierSafePosition, carrierLastPosition;
    private RigidbodyType2D carrierBodyType;
    private bool carrierSimulated;
    private ChunkLease carrierSafeChunkLease; // 保留安全登船地块的数据，不触发生成。

    /// <summary>只有本地且拥有状态权威的玩家可取得座位，远程玩家不能读取本机输入。</summary>
    public bool TryAttachCarrier(ICarrierMotionSource source, Vector2 safePosition)
    {
        if (CarrierSource != null || source == null || !source.IsAvailable || rb == null ||
            !GameNetwork.HasStateAuthority || item is not Player player || !player.IsLocalProfile)
            return false;
        carrierTileEffects = item.itemMods.GetMod_ByID<Mod_TileEffectReceiver>(ModText.Mod_TileEffectReceiver);
        carrierHealth = item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (carrierTileEffects == null || carrierHealth == null || carrierHealth.Hp <= 0f) return false;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(safePosition, out _)) return false;
        // 水中同样允许登船；靠岸时选择附近陆地作为读档点，远海则保留真实登船位置而不凭空传送。
        if (!manager.IsRuntimeWalkableLand(safePosition) && source is Mod_Carrier carrier &&
            carrier.TryFindDismount(out Vector2 shorePosition)) safePosition = shorePosition;
        carrierSafeChunkLease = manager.AcquireChunkLease(manager.ResolveWorldAddress(safePosition), ChunkLeaseKind.Simulation);
        carrierSafePosition = WorldTopologyRuntime.NormalizePosition(safePosition);
        carrierBodyType = rb.bodyType;
        carrierSimulated = rb.simulated;
        DrivenVelocity = ExternalVelocity = RequestedMoveInput = Vector2.zero;
        CarrierSource = source;
        carrierTileEffects.SetEffectsSuppressed(this, true);
        carrierHealth.DeathStarted += HandleCarrierRiderDeath;
        carrierGame = GameManager.Instance;
        if (carrierGame != null) carrierGame.Event_GameWorldExit += HandleCarrierWorldExit;
        rb.velocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.simulated = false;
        UpdateMovementState();
        ApplyCarrierPosition(source.SeatPosition);
        return true;
    }

    /// <summary>普通移动写刚体之前仲裁，输入锁只制动，不释放座位或地块保护。</summary>
    private bool UpdateCarrierMotion(float deltaTime)
    {
        if (CarrierSource == null) return false;
        if (CarrierSource.SourceComponent != null && CarrierSource.SourceComponent.gameObject.scene != item.gameObject.scene)
        {
            DetachCarrier(null);
            return false;
        }
        if (!ValidateCarrierLease()) return true;
        bool locked = inputController == null || inputController.IsGameplayInputLocked || IsLock;
        Vector2 input = locked ? Vector2.zero : inputController.ReadMoveInput(moveAction);
        bool hasMoveInput = input.sqrMagnitude > InputMoveThresholdSqr;
        // 载具消费玩家已经仲裁过的长按奔跑语义，避免自己再次轮询 InputAction 导致 Shift 状态丢失。
        bool boostRequested = !locked && hasMoveInput && holdRunInputActive;
        bool boostActive = boostRequested && stamina != null && stamina.CurrentValue >= RunStaminaThreshold;

        if (boostActive)
        {
            stamina.ConsumeStaminaPerSecond(
                StaminaConsumptionSources.CarrierBoost,
                RunStaminaConsume,
                deltaTime);

            // 与奔跑规则一致：本帧完成消费后跌破阈值，立即退出奔跑/加速状态。
            if (stamina.CurrentValue < RunStaminaThreshold)
            {
                boostActive = false;
                SetRunState(false);
            }
        }

        CarrierSource.AdvanceMotion(this, input, deltaTime, locked, boostActive);
        if (CarrierSource != null)
        {
            ExternalVelocity = CarrierSource.CurrentVelocity;
            ApplyCarrierPosition(CarrierSource.SeatPosition);
        }
        hungerActionInstance?.SetMovementState(false, false);
        return true;
    }

    /// <summary>传送改变位置后先释放，不把角色拉回旧船；原父级始终不变。</summary>
    private bool ValidateCarrierLease()
    {
        if (CarrierSource == null) return false;
        if (WorldTopologyRuntime.Distance(item.transform.position, carrierLastPosition) > 0.05f)
        {
            DetachCarrier(null);
            return false;
        }
        if (!GameNetwork.HasStateAuthority)
        {
            DetachCarrier(null);
            return false;
        }
        if (CarrierSource.SourceComponent == null ||
            !CarrierSource.IsAvailable || item.DestructionHandled || carrierHealth == null || carrierHealth.Hp <= 0f)
        {
            DetachCarrier(carrierSafePosition);
            return false;
        }
        return true;
    }

    /// <summary>源提供世界位置；关闭物理积分后刚体与 Transform 只写同一份位置。</summary>
    private void ApplyCarrierPosition(Vector2 position)
    {
        Vector2 logicalPosition = WorldTopologyRuntime.NormalizePosition(position);
        Vector2 presentationPosition = WorldLocalPresentation.ProjectPosition(logicalPosition);
        carrierLastPosition = presentationPosition;
        rb.position = presentationPosition;
        item.transform.position = new Vector3(presentationPosition.x, presentationPosition.y, item.transform.position.z);
        if (item.itemData?.transform != null)
            item.itemData.transform.position = new Vector3(logicalPosition.x, logicalPosition.y, item.transform.position.z);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

    /// <summary>释放源、事件和环境租约并恢复刚体；null 保留外部传送目的地。</summary>
    public void DetachCarrier(Vector2? destination)
    {
        ICarrierMotionSource source = CarrierSource;
        if (source == null) return;
        CarrierSource = null;
        source.ReleaseRider(this);
        if (carrierHealth != null) carrierHealth.DeathStarted -= HandleCarrierRiderDeath;
        if (carrierGame != null) carrierGame.Event_GameWorldExit -= HandleCarrierWorldExit;
        if (rb != null)
        {
            if (destination.HasValue) ApplyCarrierPosition(destination.Value);
            rb.bodyType = carrierBodyType;
            // 上船前的速度不是下船冲量，不能在长时间乘船后重新施加旧速度。
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = carrierSimulated;
        }
        if (carrierTileEffects != null) carrierTileEffects.SetEffectsSuppressed(this, false);
        carrierTileEffects = null;
        carrierHealth = null;
        carrierGame = null;
        carrierSafeChunkLease?.Dispose();
        carrierSafeChunkLease = null;
        DrivenVelocity = ExternalVelocity = RequestedMoveInput = Vector2.zero;
        UpdateMovementState();
    }

    /// <summary>生命周期释放回登船陆地，不覆盖已经发生的传送。</summary>
    public void ReleaseCarrierLease()
    {
        if (CarrierSource == null) return;
        bool moved = item != null && WorldTopologyRuntime.Distance(item.transform.position, carrierLastPosition) > 0.05f;
        DetachCarrier(moved || !GameNetwork.HasStateAuthority ? (Vector2?)null : carrierSafePosition);
    }

    /// <summary>快照落在靠岸陆地或远海登船位置，读档不恢复瞬时座位引用。</summary>
    private void SaveCarrierSafePosition()
    {
        if (CarrierSource != null && Item_Data != null)
            Item_Data.transform.position = new Vector3(carrierSafePosition.x, carrierSafePosition.y, item.transform.position.z);
    }
    private void HandleCarrierRiderDeath(Mod_DamageReceiver receiver) => ReleaseCarrierLease();
    private void HandleCarrierWorldExit() => ReleaseCarrierLease();
    /// <summary>载具完成本帧积分后再同步座位，避免乘员画面滞后一帧。</summary>
    private void LateUpdate()
    {
        if (CarrierSource == null || !ValidateCarrierLease()) return;
        ExternalVelocity = CarrierSource.CurrentVelocity;
        ApplyCarrierPosition(CarrierSource.SeatPosition);
    }
    private void OnDisable() => ReleaseCarrierLease();
    public override void Unload() => ReleaseCarrierLease();
    #endregion
}
