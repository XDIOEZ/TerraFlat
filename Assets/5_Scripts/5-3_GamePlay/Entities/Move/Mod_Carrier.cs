using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 可配置单座承载源：默认速度 5 格/秒、Shift 加速倍率 1.5、加速 3 格/秒²、制动 6 格/秒²、海上航向转速 30°/秒、陆地推动转速 15°/秒、质量 60。
/// 船与未来车辆使用同一输入/力溯源契约；不使用 Collider 推人，不改变乘员层级。
/// 座位和速度是会话租约，不写存档；位置由 Item 快照保存，读档为空船且静止。
/// </summary>
public sealed class Mod_Carrier : Module, ICarrierMotionSource, IWorldPushTarget, IInteractable, ISpatialInteractionShape, IItemModuleDependencyBinder, IBuildingPlacementCommitted
{
    #region 配置和状态
    public const string ModuleId = "Mod_Carrier";
    public Ex_ModData Data = new(); // 标准 JSON 模块数据。
    [Min(0.1f)] public float MaxSpeed = 5f; // 满幅速度。
    [Min(1f)] public float BoostSpeedMultiplier = 1.5f; // 乘员主动加速时的最高速度倍率。
    [Min(0.1f)] public float Acceleration = 3f; // 加速度。
    [Min(0.1f)] public float Braking = 6f; // 松杆减速度。
    [Min(1f)] public float FacingTurnSpeed = 30f; // 海上及普通运动来源合成后的最大航向转速（度/秒）。
    [Min(1f)] public float LandPushTurnSpeed = 15f; // 木筏位于陆地且由玩家推动时的最大航向转速（度/秒）。
    [Min(0.1f)] public float Mass = 60f; // 力计算质量。
    [Min(0.05f)] public float Radius = 0.35f; // 地形扫掠半径。
    [Range(0.01f, 1f)] public float LandSpeedMultiplier = 0.1f; // 陆地最高速度相对水面的倍率。
    [Range(0.01f, 1f)] public float LandPushSpeedMultiplier = 0.2f; // 陆地推动按推动者当前速度的五分之一。
    [Min(0f)] public float WaterCurrentSpeed = 0.18f; // 水流经载具继续传递到乘员。
    public Vector2 HullSize = new(1.5f, 1f); // 作者定义的船体扫掠与推动占地。
    public Vector2 SeatOffset = new(0, 0.25f); // 座位相对船中心。
    public bool AllowsWater = true; // 水陆通行能力，不依赖具体车型继承。
    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public Component SourceComponent => this;
    public Vector2 SeatPosition => (Vector2)item.transform.position + SeatOffset;
    public Vector2 CurrentVelocity { get; private set; }
    public Vector2 CurrentForce { get; private set; }
    public Vector2 DrivenVelocity { get; private set; } // 划船或主动推动的速度。
    public Vector2 ExternalVelocity { get; private set; } // 环境传入的被动速度。
    public Mover PushSource { get; private set; } // 当前推动来源，瞬时关系不入存档。
    public Component MotionComponent => this;
    public Vector2 MotionPosition => item.transform.position;
    public Vector2 MotionVelocity => CurrentVelocity;
    public Vector2 PushHalfExtents => Vector2.Scale(HullSize * 0.5f,
        new Vector2(Mathf.Abs(item.transform.lossyScale.x), Mathf.Abs(item.transform.lossyScale.y)));
    public bool IsAvailable => loaded && isActiveAndEnabled && item != null && !item.DestructionHandled &&
        health != null && health.Hp > 0f && building != null && building.IsInstalled();
    public Mover Rider { get; private set; } // 源持有的唯一乘员。
    private Mod_Building building;
    private DamageReceiver health;
    private Rigidbody2D body;
    private BoxCollider2D physicalCollider;
    private Transform visualTransform;
    private SpriteRenderer visualRenderer;
    private Quaternion visualBaseLocalRotation;
    private bool visualBaseRotationCaptured;
    private Vector2 requestedInput;
    private bool requestedControlsLocked;
    private bool requestedBoost;
    private Vector2 lastPhysicsPosition;
    private bool loaded, activated;
    private Vector2 pushedVelocity;
    private Vector2 motionHeading = Vector2.up; // 唯一船头航向；停止时保留，重新受力也不能瞬间掉头。
    private float pushValidUntil;
    private CarrierWaterWake waterWake;
    #endregion

    #region 生命周期
    /// <summary>通过正式模块注册表解析安装状态与生命权威。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        building = modules.RequireSingleModById<Mod_Building>(ModText.Building);
        health = modules.RequireSingleModById<DamageReceiver>(ModText.Hp);
    }

    /// <summary>只恢复空座位，不迁移任何启动资产或旧存档。</summary>
    public override void Load()
    {
        if (!(MaxSpeed > 0 && BoostSpeedMultiplier >= 1f && Acceleration > 0 && Braking > 0 && FacingTurnSpeed > 0 && LandPushTurnSpeed > 0 && Mass > 0 && Radius > 0) ||
            !(LandSpeedMultiplier > 0f && LandSpeedMultiplier <= 1f) ||
            !(LandPushSpeedMultiplier > 0f && LandPushSpeedMultiplier <= 1f) ||
            !(HullSize.x > 0f && HullSize.y > 0f) ||
            float.IsInfinity(MaxSpeed + BoostSpeedMultiplier + Acceleration + Braking + FacingTurnSpeed + LandPushTurnSpeed + Mass + Radius) ||
            float.IsNaN(SeatOffset.sqrMagnitude) || float.IsInfinity(SeatOffset.sqrMagnitude))
            throw new InvalidOperationException("载具速度、加速倍率、加速度、制动、海上转向速度、陆地推动转向速度、质量、半径和陆地速度倍率必须有效。");
        CarrierSaveState state = Data.GetData<CarrierSaveState>();
        if (state == null || state.Version != 1)
            throw new InvalidOperationException("载具模块要求 Version=1 的显式快照。");
        loaded = true;
        activated = false;
        CurrentVelocity = CurrentForce = DrivenVelocity = ExternalVelocity = Vector2.zero;
        motionHeading = Vector2.up;
        PushSource = null;
        body = item.GetComponent<Rigidbody2D>();
        physicalCollider = item.GetComponent<BoxCollider2D>();
        if (body == null || physicalCollider == null)
            throw new InvalidOperationException("载具外壳必须提供根 Rigidbody2D 与禁用的查询 Collider；移动由游戏推动系统结算。");
        requestedInput = Vector2.zero;
        requestedControlsLocked = true;
        requestedBoost = false;
        lastPhysicsPosition = body.position;
        BindVisualTransform();
        waterWake = item.GetComponent<CarrierWaterWake>() ?? item.gameObject.AddComponent<CarrierWaterWake>();
        waterWake.Bind(this, visualRenderer);
        BindRuntimeSources();
        ActivateInstalledCarrier();
    }

    /// <summary>保存版本标记，不保存乘员引用、油门或瞬时速度。</summary>
    public override void Save() => Data.WriteData(new CarrierSaveState());

    /// <summary>源回收前释放乘员的刚体与环境租约。</summary>
    public override void Unload()
    {
        SuspendRuntimeSources();
        loaded = false;
    }

    private void SuspendRuntimeSources()
    {
        Rider?.ReleaseCarrierLease();
        StopMotion();
        if (health != null) health.DeathStarted -= HandleSourceDeath;
        SpatialInteractionRegistry.Unregister(this);
        WorldMotionSystem.Unregister(this);
        waterWake?.Clear();
        activated = false;
    }

    private void BindRuntimeSources()
    {
        if (health != null)
        {
            health.DeathStarted -= HandleSourceDeath;
            health.DeathStarted += HandleSourceDeath;
        }
        SpatialInteractionRegistry.Register(this, Mathf.Max(0.7f, Radius));
        WorldMotionSystem.Register(this);
    }

    private void OnEnable()
    {
        if (!loaded) return;
        BindRuntimeSources();
        ActivateInstalledCarrier();
    }
    private void OnDisable() => SuspendRuntimeSources();
    private void OnDestroy() => Unload();

    /// <summary>载具击沉时让乘员留在当前世界位置并恢复自身物理/水体效果，禁止传回登船点。</summary>
    private void HandleSourceDeath(DamageReceiver receiver)
    {
        Rider?.DetachCarrier(null);
        Unload();
    }

    /// <summary>完成安装后接入玩法移动；战斗受击 Trigger 与推动系统保持独立。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (!IsAvailable)
        {
            Rider?.ReleaseCarrierLease();
            StopMotion();
            return;
        }
        ActivateInstalledCarrier();
        if (Rider != null && (!Rider.isActiveAndEnabled || !GameNetwork.HasStateAuthority)) Rider.ReleaseCarrierLease();
    }

    /// <summary>安装事务提交即关闭碰撞，不等下一次物理帧。</summary>
    public void OnBuildingPlacementCommitted() => ActivateInstalledCarrier();

    private void ActivateInstalledCarrier()
    {
        if (activated || !IsAvailable) return;
        physicalCollider.enabled = false;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.simulated = true;
        body.gravityScale = 0f;
        body.mass = Mass;
        body.interpolation = RigidbodyInterpolation2D.None;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        lastPhysicsPosition = body.position;
        building.ReleasePlacementOccupancy();
        activated = true;
    }
    #endregion

    #region 交互和乘坐
    /// <summary>光标命中可见木筏，而不是依赖不存在的推动碰撞体；支持视觉旋转及世界环绕。</summary>
    public bool ContainsInteractionPoint(Vector2 point)
    {
        SpriteRenderer renderer = visualRenderer;
        if (renderer == null || renderer.sprite == null) return false;
        Vector2 nearbyPoint = (Vector2)item.transform.position + WorldTopologyRuntime.ShortestDelta(item.transform.position, point);
        Vector3 local = renderer.transform.InverseTransformPoint(new Vector3(nearbyPoint.x, nearbyPoint.y, renderer.transform.position.z));
        return renderer.sprite.bounds.Contains(local);
    }

    public bool CanInteract(Item actor)
    {
        Player player = ResolvePlayer(actor);
        if (!IsAvailable || !GameNetwork.HasStateAuthority || player == null || !player.IsLocalProfile)
            return false;
        Mover mover = player.itemMods.GetMod_ByID<Mover>(ModText.Mover);
        return mover != null && (Rider == mover || (Rider == null && mover.CarrierSource == null));
    }

    /// <summary>再次交互下船；优先落到附近安全陆地，远海则落到船体外侧的安全水面。</summary>
    public void OnInteractStart(Item actor)
    {
        if (!CanInteract(actor)) return;
        ActivateInstalledCarrier();
        Player player = ResolvePlayer(actor);
        Mover mover = player.itemMods.GetMod_ByID<Mover>(ModText.Mover);
        if (Rider == mover)
        {
            StopMotion();
            if (TryFindDismount(out Vector2 destination) || TryFindWaterDismount(mover, out destination))
                mover.DetachCarrier(destination);
            return;
        }
        if (mover.TryAttachCarrier(this, player.transform.position)) Rider = mover;
    }

    /// <summary>输入发送者可能是玩家自己的手部 Item，身份沿正式 Owner 链解析。</summary>
    private static Player ResolvePlayer(Item actor)
        => actor as Player ?? actor?.Owner as Player ?? actor?.GetComponentInParent<Player>();

    /// <summary>焦点取消和临时输入锁不是下船指令。</summary>
    public void OnInteractCancel(Item actor) { }

    public void ReleaseRider(Mover rider)
    {
        if (Rider != rider) return;
        Rider = null;
        StopMotion();
    }

    /// <summary>限定两格内陆地，避免在水中误操作掉入水里。</summary>
    public bool TryFindDismount(out Vector2 destination)
    {
        Vector2 origin = item.transform.position;
        for (int ring = 1; ring <= 4; ring++)
        {
            for (int direction = 0; direction < 8; direction++)
            {
                float angle = direction * Mathf.PI / 4f;
                Vector2 candidate = WorldTopologyRuntime.NormalizePosition(origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (ring * 0.5f));
                if (!IsSafeLand(candidate)) continue;
                destination = candidate;
                return true;
            }
        }
        destination = default;
        return false;
    }

    /// <summary>远海没有陆地时，把乘员放到船体外侧的可用水格，避免 E 键被永久困在船上。</summary>
    private bool TryFindWaterDismount(Mover rider, out Vector2 destination)
    {
        Vector2 origin = item.transform.position;
        float riderRadius = rider != null ? Mathf.Max(0.05f, rider.pushContactRadius) : 0.2f;
        float clearance = Mathf.Max(PushHalfExtents.x, PushHalfExtents.y) + riderRadius + 0.15f;
        Vector2 preferredDirection = SeatOffset.sqrMagnitude > 0.0001f ? SeatOffset.normalized : Vector2.up;

        for (int step = 0; step < 8; step++)
        {
            float angle = Mathf.Atan2(preferredDirection.y, preferredDirection.x) + step * Mathf.PI / 4f;
            Vector2 candidate = WorldTopologyRuntime.NormalizePosition(
                origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * clearance);
            if (!IsSafeWater(candidate)) continue;
            destination = candidate;
            return true;
        }

        destination = default;
        return false;
    }

    /// <summary>水中下船点必须是真实液体且没有地形或其它建筑占用。</summary>
    private bool IsSafeWater(Vector2 position)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample tile))
            return false;
        return tile.LiquidDepth > 0f && tile.Cell.BlockingTileId == 0 && tile.Cell.BackTileId == 0 &&
               tile.TopTileId != 0 &&
               (tile.Cell.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) == 0 &&
               !BuildingOccupancyRegistry.IsOccupied(tile.WorldCell, building);
    }
    #endregion

    #region 源拥有的移动和力
    /// <summary>只接收乘员输入；水流、划船与外部推动在同一固定步结算。</summary>
    public void AdvanceMotion(Mover rider, Vector2 input, float deltaTime, bool controlsLocked, bool boostRequested)
    {
        if (rider != Rider || !IsAvailable || !GameNetwork.HasStateAuthority) return;
        requestedControlsLocked = controlsLocked;
        requestedInput = controlsLocked ? Vector2.zero : Vector2.ClampMagnitude(input, 1f);
        requestedBoost = !controlsLocked && boostRequested && requestedInput.sqrMagnitude > 0.001f;
    }

    /// <summary>按来源合成速度，再用地形扫掠积分；刚体仅同步位置，禁止施加碰撞冲量。</summary>
    private void FixedUpdate()
    {
        if (!IsAvailable || body == null || !body.simulated || !GameNetwork.HasStateAuthority)
            return;
        float deltaTime = Time.fixedDeltaTime;
        Vector2 previousVelocity = CurrentVelocity;
        RefreshPushSource();
        Vector2 input = Rider != null && !requestedControlsLocked ? requestedInput : Vector2.zero;
        float speedLimit = ResolveSurfaceSpeedLimit(body.position);
        if (Rider != null && requestedBoost)
            speedLimit *= BoostSpeedMultiplier;
        Vector2 desiredVelocity = input * speedLimit;
        if (PushSource != null)
        {
            // 推动速度直接来自推动者当前环境移速，不再乘木筏自身的海上速度。
            DrivenVelocity = pushedVelocity;
        }
        else
        {
            float rate = desiredVelocity.sqrMagnitude > 0.0001f ? Acceleration : Braking;
            DrivenVelocity = Vector2.MoveTowards(DrivenVelocity, desiredVelocity, rate * deltaTime);
        }
        ExternalVelocity = WorldMotionSystem.SampleWaterVelocity(body.position, WaterCurrentSpeed);
        Vector2 requestedVelocity = DrivenVelocity + ExternalVelocity;
        float turnSpeed = PushSource != null && !IsWaterSurface(body.position)
            ? LandPushTurnSpeed
            : FacingTurnSpeed;
        Vector2 steeredVelocity = ResolveSteeredVelocity(requestedVelocity, deltaTime, true, turnSpeed);
        Vector2 displacement = ResolveAllowedDisplacement(body.position, steeredVelocity * deltaTime);
        CurrentVelocity = displacement / deltaTime;
        CurrentForce = (CurrentVelocity - previousVelocity) * (Mass / deltaTime);
        Vector2 next = WorldTopologyRuntime.NormalizePosition(body.position + displacement);
        body.velocity = Vector2.zero;
        body.position = next;
        item.transform.position = new Vector3(next.x, next.y, item.transform.position.z);
        UpdateVisualFacing();
        TrackPhysicsMovement();
    }

    /// <summary>当前有效玩家输入才可推动；已乘坐同一载具的乘员不能形成循环作用链。</summary>
    public bool CanReceivePush(Mover source)
        => IsAvailable && GameNetwork.HasStateAuthority && source != null && source != Rider &&
           source.CarrierSource == null && source.RequestedMoveInput.sqrMagnitude > 0.001f;

    /// <summary>登记一次有界推动并返回下一步可实现的速度，用于限制推动者穿入船体。</summary>
    public Vector2 RequestPush(Mover source, Vector2 velocity, float deltaTime)
    {
        if (!CanReceivePush(source)) return CurrentVelocity;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        bool inWater = manager != null && manager.TryGetRuntimeTerrainTile(source.rb.position, out RuntimeTerrainTileSample tile) &&
                       tile.LiquidDepth > 0f;
        PushSource = source;
        pushedVelocity = WorldMotionSystem.CalculatePushVelocity(velocity, inWater, LandPushSpeedMultiplier);
        pushValidUntil = Time.time + Mathf.Max(0.1f, deltaTime * 2f);
        Vector2 total = pushedVelocity + WorldMotionSystem.SampleWaterVelocity(body.position, WaterCurrentSpeed);
        float turnSpeed = IsWaterSurface(body.position) ? FacingTurnSpeed : LandPushTurnSpeed;
        Vector2 steeredVelocity = ResolveSteeredVelocity(total, deltaTime, false, turnSpeed);
        return ResolveAllowedDisplacement(body.position, steeredVelocity * deltaTime) / Mathf.Max(0.001f, deltaTime);
    }

    /// <summary>松开输入、换场景、远离或来源回收立即解除推动关系。</summary>
    private void RefreshPushSource()
    {
        if (PushSource == null) return;
        if (!PushSource.isActiveAndEnabled || !CanReceivePush(PushSource) || Time.time > pushValidUntil ||
            PushSource.gameObject.scene != gameObject.scene ||
            WorldTopologyRuntime.Distance(PushSource.rb.position, body.position) > PushHalfExtents.magnitude + PushSource.pushContactRadius + 0.25f)
        {
            PushSource = null;
            pushedVelocity = Vector2.zero;
        }
    }

    /// <summary>最多每 0.1 格检查完整船体，阻挡时保留合法的单轴滑动，防止高速穿墙或角落越界。</summary>
    private Vector2 ResolveAllowedDisplacement(Vector2 origin, Vector2 desired)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(desired.magnitude / 0.1f));
        Vector2 increment = desired / steps;
        Vector2 position = origin;
        for (int index = 0; index < steps; index++)
        {
            Vector2 candidate = WorldTopologyRuntime.NormalizePosition(position + increment);
            if (CanOccupyFootprint(candidate)) { position = candidate; continue; }
            Vector2 x = WorldTopologyRuntime.NormalizePosition(position + new Vector2(increment.x, 0f));
            if (CanOccupyFootprint(x)) position = x;
            Vector2 y = WorldTopologyRuntime.NormalizePosition(position + new Vector2(0f, increment.y));
            if (CanOccupyFootprint(y)) position = y;
        }
        return WorldTopologyRuntime.ShortestDelta(origin, position);
    }

    /// <summary>物理位移后刷新 Item 空间索引；跨区块时同时标记新位置的建筑存档。</summary>
    private void TrackPhysicsMovement()
    {
        Vector2 currentPosition = body.position;
        if ((currentPosition - lastPhysicsPosition).sqrMagnitude <= 0.000001f)
            return;

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager != null &&
            manager.ResolveRuntimeChunkOrigin(lastPhysicsPosition) != manager.ResolveRuntimeChunkOrigin(currentPosition))
        {
            SaveDataMgr.Instance?.RecordRuntimeBuildingChangeAtPosition(item, lastPhysicsPosition);
            SaveDataMgr.Instance?.RecordRuntimeBuildingChange(item);
        }

        lastPhysicsPosition = currentPosition;
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

    public void StopMotion()
    {
        requestedInput = Vector2.zero;
        requestedControlsLocked = true;
        requestedBoost = false;
        CurrentForce = Vector2.zero;
        PushSource = null;
        pushedVelocity = DrivenVelocity = ExternalVelocity = Vector2.zero;
        if (body != null && body.simulated)
            body.velocity = Vector2.zero;
        CurrentVelocity = Vector2.zero;
    }

    /// <summary>木筏原图默认朝上；只旋转视觉子节点，禁止旋转载具根节点和座位坐标系。</summary>
    private void BindVisualTransform()
    {
        SpriteRenderer renderer = item.Sprite != null ? item.Sprite : item.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null || renderer.transform == item.transform)
            throw new InvalidOperationException("载具必须使用独立的 SpriteRenderer 视觉子节点。");
        visualTransform = renderer.transform;
        visualRenderer = renderer;
        if (!visualBaseRotationCaptured)
        {
            visualBaseLocalRotation = visualTransform.localRotation;
            visualBaseRotationCaptured = true;
        }
        else
        {
            visualTransform.localRotation = visualBaseLocalRotation;
        }
    }

    /// <summary>所有主动/被动力先合成，再统一限制船头航向变化；速度大小不受转向器额外衰减。</summary>
    private Vector2 ResolveSteeredVelocity(Vector2 requestedVelocity, float deltaTime, bool commitHeading, float turnSpeed)
    {
        if (requestedVelocity.sqrMagnitude <= 0.0001f)
            return Vector2.zero;

        Vector2 targetHeading = requestedVelocity.normalized;
        Vector2 currentHeading = motionHeading.sqrMagnitude > 0.0001f ? motionHeading.normalized : Vector2.up;
        float maxStep = turnSpeed * Mathf.Max(0f, deltaTime);
        float signedAngle = Vector2.SignedAngle(currentHeading, targetHeading);
        float step = Mathf.Clamp(signedAngle, -maxStep, maxStep);
        Vector3 rotatedHeading = Quaternion.Euler(0f, 0f, step) * (Vector3)currentHeading;
        Vector2 nextHeading = new(rotatedHeading.x, rotatedHeading.y);
        nextHeading.Normalize();
        if (commitHeading)
            motionHeading = nextHeading;
        return nextHeading * requestedVelocity.magnitude;
    }

    /// <summary>视觉严格跟随已经受限的统一船头航向，不再对划船、推动或海流分别处理。</summary>
    private void UpdateVisualFacing()
    {
        if (visualTransform == null || motionHeading.sqrMagnitude <= 0.0001f) return;
        float angle = Mathf.Atan2(motionHeading.y, motionHeading.x) * Mathf.Rad2Deg - 90f;
        visualTransform.localRotation = visualBaseLocalRotation * Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>水面使用完整 MaxSpeed；非水面统一降为水面速度的指定倍率。</summary>
    private float ResolveSurfaceSpeedLimit(Vector2 position)
    {
        if (IsWaterSurface(position))
            return MaxSpeed;
        return MaxSpeed * LandSpeedMultiplier;
    }

    /// <summary>统一读取载具当前位置是否处于真实水面，用于速度与转向上下文判定。</summary>
    private static bool IsWaterSurface(Vector2 position)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        return manager != null && manager.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample tile) &&
               tile.LiquidDepth > 0f;
    }

    /// <summary>覆盖整个船体包围范围，不能只测中心或四个方向而漏过墙角。</summary>
    private bool CanOccupyFootprint(Vector2 position)
    {
        Vector2 half = PushHalfExtents - Vector2.one * 0.001f;
        for (int cellX = Mathf.FloorToInt(position.x - half.x); cellX <= Mathf.FloorToInt(position.x + half.x); cellX++)
            for (int cellY = Mathf.FloorToInt(position.y - half.y); cellY <= Mathf.FloorToInt(position.y + half.y); cellY++)
                if (!CanOccupy(new Vector2(cellX + 0.5f, cellY + 0.5f))) return false;
        return true;
    }

    private bool CanOccupy(Vector2 position)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample tile)) return false;
        bool water = tile.LiquidDepth > 0f;
        return tile.Cell.BlockingTileId == 0 && tile.Cell.BackTileId == 0 && tile.TopTileId != 0 &&
            (tile.Cell.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) == 0 &&
            !BuildingOccupancyRegistry.IsOccupied(tile.WorldCell, building) &&
            (water ? AllowsWater : tile.Terrain.IsWalkable(tile.LocalCell.x, tile.LocalCell.y));
    }

    private bool IsSafeLand(Vector2 position)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !CanOccupyFootprint(position)) return false;
        for (int cellX = Mathf.FloorToInt(position.x - Radius); cellX <= Mathf.FloorToInt(position.x + Radius); cellX++)
            for (int cellY = Mathf.FloorToInt(position.y - Radius); cellY <= Mathf.FloorToInt(position.y + Radius); cellY++)
                if (!manager.IsRuntimeWalkableLand(new Vector2(cellX + 0.5f, cellY + 0.5f))) return false;
        return true;
    }
    #endregion
}
