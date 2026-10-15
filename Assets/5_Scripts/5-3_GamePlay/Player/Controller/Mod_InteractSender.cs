using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Profiling;

public partial class Mod_InteractSender : Module,IFocusPoint,ITrunDirection
{
    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }
    #endregion


    #region 模组参数

    [SerializeReference]
    public List<string> RawData = new List<string>();
    public Mod_GameController gameController;
    [ShowInInspector]
    public List<IInteractable> receiversInRange = new List<IInteractable>();
    public IInteractable currentReceiver;
    private Component currentReceiverComponent;
    private IInteractable previewReceiver;
    private Component previewReceiverComponent;
    private InteractionTargetOutline previewOutline;
    private IInteractable heldReceiver; // 仅记录当前由交互键按住产生的持续交互目标。
    public bool HasHeldInteraction => IsInteractionReceiverAlive(heldReceiver) && currentReceiver == heldReceiver; // 只读诊断：当前是否正在持续世界交互。
    public string CurrentInteractionType => IsInteractionReceiverAlive(currentReceiver) ? currentReceiver.GetType().Name : string.Empty; // 只读诊断：当前交互目标类型。
    public string HeldInteractionType => IsInteractionReceiverAlive(heldReceiver) ? heldReceiver.GetType().Name : string.Empty; // 只读诊断：按住交互目标类型。
    public int CurrentInteractionTargetGuid => ResolveInteractionTargetGuid(currentReceiver); // 只读诊断：当前目标所属 Item Guid。
    public int HeldInteractionTargetGuid => ResolveInteractionTargetGuid(heldReceiver); // 只读诊断：按住目标所属 Item Guid。
    public const float DefaultMaxInteractDistance = 2f;
    // 交互距离默认值；建筑放置距离运行时复用该值。
    public float maxInteractDistance = DefaultMaxInteractDistance;
    // 交互是纯查询通道，不再创建或启用任何 Trigger；该缓冲区只服务 Physics2D Overlap 查询。
    private readonly Collider2D[] interactionOverlapBuffer = new Collider2D[32];
    private readonly List<IInteractable> spatialCandidates = new(); // 纯空间目标共用选择规则。
    private static readonly ProfilerMarker PointerQueryMarker = new("FlatWorld.Interaction.PointerQuery");
    private static readonly ProfilerMarker RadiusQueryMarker = new("FlatWorld.Interaction.RadiusQuery");
    private Mod_HotBar hotBar;

    protected override void OnLoad()
    {
        ModSaveData.ReadData(ref RawData);
        gameController = item != null ? item.GetComponentInChildren<Mod_GameController>() : null;
        hotBar = item != null ? item.GetComponentInChildren<Mod_HotBar>(true) : null;
        BindInput();
    }

    protected override void OnSave()
    {
        ModSaveData.WriteData(RawData);
    }

    public override void ModUpdate(float deltaTime)
    {
        PruneDestroyedInteractionTargets();
        if (!IsLocalInteractionOwner())
        {
            ClearInteractionPreview();
            return;
        }

        if (IsGameplayInputLocked())
        {
            EndEnvironmentActionHold();
            CancelCurrentInteraction();
            return;
        }

        ValidateCurrentInteractionDistance();
        TickHeldInteraction();
        RefreshInteractionPreview();
        TickEnvironmentInteraction(deltaTime);
    }
    #endregion

    #region 输入绑定
    private void BindInput()
    {
        if (gameController == null || gameController._inputActions == null)
            return;

        var action = gameController._inputActions.Win10.E;
        action.performed -= OnInteractPressed;
        action.canceled -= OnInteractReleased;
        action.performed += OnInteractPressed;
        action.canceled += OnInteractReleased;

        // 左键只接收目标明确允许的采集动作，建筑和设备统一由交互键操作。
        gameController.LeftClick -= OnPointerClick;
        gameController.LeftClick += OnPointerClick;
    }

    private void UnbindInput()
    {
        if (gameController == null || gameController._inputActions == null)
            return;

        var action = gameController._inputActions.Win10.E;
        action.performed -= OnInteractPressed;
        action.canceled -= OnInteractReleased;
        gameController.LeftClick -= OnPointerClick;
    }

    private void OnInteractPressed(InputAction.CallbackContext ctx)
    {
        if (gameController != null && !gameController.IsGameplayInputAllowed(ctx))
            return;

        // 交互键只操作世界目标，手持建筑的放置状态不占用这条通道。
        bool interacted = TryBeginHeldInteraction();
        if (!interacted)
            BeginEnvironmentActionHold();
    }

    /// <summary>
    /// 主动扫描并触发当前最近目标；同一目标允许每次按键重新执行，
    /// 避免目标曾因维度加载等瞬态条件拒绝后被永久缓存。
    /// </summary>
    public bool TryInteractAtCurrentPosition()
    {
        if (!TryStartInteractionAtCurrentPosition())
            return false;

        CompleteSingleInteraction(currentReceiver);
        return true;
    }

    /// <summary>只执行交互开始，用于把物理按住状态交给持续交互通道。</summary>
    private bool TryStartInteractionAtCurrentPosition()
    {
        if (!IsLocalInteractionOwner() || IsGameplayInputLocked())
            return false;

        // 每次按下交互键都做一次纯查询，交互链不再启用任何 Physics2D Trigger。
        receiversInRange.Clear();
        return RefreshReceiversAtCurrentPosition();
    }

    /// <summary>通过正式交互距离与目标规则尝试和指定对象交互，供非物理输入控制源复用。</summary>
    public bool TryInteractTarget(IInteractable receiver)
    {
        if (!CanInteractTarget(receiver))
            return false;

        if (!StartInteraction(receiver))
            return false;

        CompleteSingleInteraction(receiver);
        return true;
    }

    /// <summary>开始一次持续交互；不指定目标时沿用玩家当前最近目标选择规则。</summary>
    public bool TryBeginHeldInteraction(IInteractable receiver = null)
    {
        bool interacted = receiver != null
            ? CanInteractTarget(receiver) && StartInteraction(receiver)
            : TryStartInteractionAtCurrentPosition();
        heldReceiver = interacted && IsInteractionReceiverAlive(currentReceiver) ? currentReceiver : null;
        return interacted;
    }

    /// <summary>结束持续交互状态；目标本身仍按原交互生命周期保留到切换或取消。</summary>
    public void EndHeldInteraction()
    {
        IInteractable releasedReceiver = heldReceiver;
        heldReceiver = null;
        if (IsInteractionReceiverAlive(releasedReceiver) && releasedReceiver == currentReceiver)
            releasedReceiver.OnInteractEnd(item);
    }

    /// <summary>复用正式交互发送器的全部准入规则，供结构化观察和非物理输入源判断目标当前是否可交互。</summary>
    public bool CanInteractTarget(IInteractable receiver)
    {
        if (!IsLocalInteractionOwner() || IsGameplayInputLocked())
            return false;

        Component receiverComponent = receiver as Component;
        if (!IsInteractionCandidate(receiver, receiverComponent))
            return false;

        if (!TryGetInteractionPosition(receiver, out Vector3 targetPosition))
            return false;
        float distance = WorldTopologyRuntime.Distance(item.transform.position, targetPosition);
        return distance <= maxInteractDistance;
    }

    private void OnInteractReleased(InputAction.CallbackContext ctx)
    {
        EndHeldInteraction();
        if (gameController != null && !gameController.IsGameplayInputAllowed(ctx))
            return;

        EndEnvironmentActionHold();
    }

    /// <summary>把左键落点交给明确允许点选的采集目标。</summary>
    private void OnPointerClick()
    {
        if (!IsLocalInteractionOwner() || IsGameplayInputLocked() ||
            IsHeldBuildingPlacementActive() || gameController == null || item == null)
            return;

        try
        {
            if (!gameController.TryGetMouseWorldPosition(out Vector3 pointerWorld))
                return;

            IInteractable selectedReceiver = FindReceiverAtPointer(pointerWorld, requirePointerPermission: true);
            if (selectedReceiver != null)
            {
                if (StartInteraction(selectedReceiver))
                    CompleteSingleInteraction(selectedReceiver);
            }
        }
        catch (MissingReferenceException)
        {
            return;
        }
    }

    /// <summary>正式交互按光标落点查询全部目标，包括纯数据机械。</summary>
    private IInteractable FindReceiverAtPointer(Vector2 pointerWorld, bool requirePointerPermission = false)
        => FindReceiverAtPointerCore(pointerWorld, requirePointerPermission, previewOnly: false);

    /// <summary>描边只查已接入表现的目标，机器通过轻量视觉注册而不轮询机械图。</summary>
    private IInteractable FindPreviewReceiverAtPointer(Vector2 pointerWorld)
        => FindReceiverAtPointerCore(pointerWorld, requirePointerPermission: false, previewOnly: true);

    /// <summary>按用途选择空间目标，物理落点和距离校验保持一致。</summary>
    private IInteractable FindReceiverAtPointerCore(Vector2 pointerWorld, bool requirePointerPermission, bool previewOnly)
    {
        using var queryScope = PointerQueryMarker.Auto();
        Physics2D.SyncTransforms();
        int count = Physics2D.OverlapPointNonAlloc(pointerWorld, interactionOverlapBuffer, InteractionQueryLayerMask);
        Vector3 origin = item.transform.position;
        IInteractable selectedReceiver = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (IsCombatOnlyCollider(interactionOverlapBuffer[i]))
                continue;

            IInteractable receiver = GameplayPhysics2D.ResolveComponent<IInteractable>(interactionOverlapBuffer[i]);
            Component receiverComponent = receiver as Component;
            if (!IsInteractionCandidate(receiver, receiverComponent) ||
                (requirePointerPermission && !receiver.CanPointerInteract(item)))
                continue;

            if (!TryGetInteractionPosition(receiver, out Vector3 targetPosition))
                continue;
            float distance = WorldTopologyRuntime.Distance(origin, targetPosition);
            if (distance > maxInteractDistance || distance >= closestDistance)
                continue;

            closestDistance = distance;
            selectedReceiver = receiver;
        }

        spatialCandidates.Clear();
        if (previewOnly)
            SpatialInteractionRegistry.QueryPreview(item, maxInteractDistance, pointerWorld, spatialCandidates);
        else
            SpatialInteractionRegistry.Query(item, maxInteractDistance, pointerWorld, spatialCandidates);
        foreach (IInteractable candidate in spatialCandidates)
        {
            if (!IsInteractionCandidate(candidate, candidate as Component) ||
                !TryGetInteractionPosition(candidate, out Vector3 targetPosition)) continue;
            if (requirePointerPermission && !candidate.CanPointerInteract(item)) continue;
            float distance = WorldTopologyRuntime.Distance(origin, targetPosition);
            if (distance >= closestDistance) continue;
            closestDistance = distance;
            selectedReceiver = candidate;
        }
        return selectedReceiver;
    }

    /// <summary>
    /// 扫描玩家当前交互半径内的目标，补偿动态区块/自然物在玩家之后完成绑定的时序。
    /// </summary>
    private bool RefreshReceiversAtCurrentPosition()
    {
        IInteractable closestReceiver = FindClosestReceiverAtCurrentPosition(collectReceivers: true);

        // 这是一次明确的新交互请求；即使目标没变也必须重试。
        return closestReceiver != null && StartInteraction(closestReceiver);
    }

    /// <summary>每帧只寻找已接入描边的目标，不为预览扫描整张机械图。</summary>
    private void RefreshInteractionPreview()
    {
        if (!IsLocalInteractionOwner() ||
            item == null || !item.gameObject.activeInHierarchy)
        {
            ClearInteractionPreview();
            return;
        }

        SetInteractionPreview(FindClosestReceiverAtCurrentPosition(collectReceivers: false));
    }

    /// <summary>统一处理半径查询、按键和鼠标点选识别到的目标高亮。</summary>
    private void SetInteractionPreview(IInteractable receiver)
    {
        Component receiverComponent = receiver as Component;
        if (!IsInteractionCandidate(receiver, receiverComponent))
        {
            ClearInteractionPreview();
            return;
        }

        if (receiver is IWorldInteractionPreview entityPreview)
        {
            if (previewReceiver == receiver) return;
            ClearInteractionPreview();
            previewReceiver = receiver;
            entityPreview.SetInteractionHighlighted(true);
            return;
        }

        if (receiver is IWorldInteractionTarget)
        {
            ClearInteractionPreview();
            return;
        }

        if (previewReceiver == receiver &&
            previewReceiverComponent == receiverComponent &&
            previewOutline != null)
        {
            previewOutline.SetHighlighted(true);
            return;
        }

        ClearInteractionPreview();
        previewReceiver = receiver;
        previewReceiverComponent = receiverComponent;
        previewOutline = InteractionTargetOutline.GetOrCreate(receiverComponent);
        if (previewOutline != null)
            previewOutline.SetHighlighted(true);
    }

    /// <summary>扫描交互半径，按当前指向优先、距离规则兜底。</summary>
    private IInteractable FindClosestReceiverAtCurrentPosition(bool collectReceivers)
    {
        if (item == null || !item.gameObject.activeInHierarchy)
            return null;

        bool syncedByPointerQuery = false;
        if (TryGetInteractionPointer(out Vector2 pointer))
        {
            IInteractable pointedReceiver = collectReceivers
                ? FindReceiverAtPointer(pointer)
                : FindPreviewReceiverAtPointer(pointer);
            syncedByPointerQuery = true;
            if (pointedReceiver != null) return pointedReceiver;
            // 水面交互严格服从光标落点；未命中木筏时交给喝水等环境动作。
            if (IsPointerOverWater(pointer)) return null;
        }

        // 精确交互只认光标落点；默认模式保留朝向和距离兜底，方便手机操作。
        if (InteractionUserSettings.PreciseInteraction)
            return null;

        using var queryScope = RadiusQueryMarker.Auto();
        // 光标命中查询已经同步过本帧物理变换，半径兜底直接复用。
        if (!syncedByPointerQuery)
            Physics2D.SyncTransforms();
        int count = Physics2D.OverlapCircleNonAlloc(
            item.transform.position,
            Mathf.Max(0.01f, maxInteractDistance),
            interactionOverlapBuffer,
            InteractionQueryLayerMask);
        IInteractable closestReceiver = null;
        float closestDistance = float.MaxValue;
        IInteractable directionalReceiver = null;
        float directionalAlignment = 0f;
        float directionalDistance = float.MaxValue;
        bool hasInteractionDirection = TryGetInteractionDirection(out Vector2 interactionDirection);

        spatialCandidates.Clear();
        if (collectReceivers)
            SpatialInteractionRegistry.Query(item, maxInteractDistance, null, spatialCandidates);
        else
            SpatialInteractionRegistry.QueryPreview(item, maxInteractDistance, null, spatialCandidates);
        for (int i = 0; i < count; i++)
        {
            Collider2D overlap = interactionOverlapBuffer[i];
            if (IsCombatOnlyCollider(overlap))
                continue;
            IInteractable receiver = GameplayPhysics2D.ResolveComponent<IInteractable>(overlap);
            if (receiver != null && !spatialCandidates.Contains(receiver)) spatialCandidates.Add(receiver);
        }
        foreach (IInteractable receiver in spatialCandidates)
        {
            // 载具必须真正被光标命中；普通设施仍保留原有近距离按键交互。
            if (receiver is ICarrierMotionSource) continue;
            Component receiverComponent = receiver as Component;
            if (!IsInteractionCandidate(receiver, receiverComponent))
                continue;

            if (!TryGetInteractionPosition(receiver, out Vector3 targetPosition))
                continue;
            float distance = WorldTopologyRuntime.Distance(item.transform.position, targetPosition);
            if (distance > maxInteractDistance)
                continue;

            if (collectReceivers && !receiversInRange.Contains(receiver))
                receiversInRange.Add(receiver);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestReceiver = receiver;
            }

            if (!hasInteractionDirection)
                continue;

            Vector2 receiverOffset = WorldTopologyRuntime.ShortestDelta(
                item.transform.position, targetPosition);
            if (receiverOffset.sqrMagnitude < 0.0001f)
                continue;

            float alignment = Vector2.Dot(interactionDirection, receiverOffset.normalized);
            if (alignment < 0f ||
                alignment < directionalAlignment ||
                (Mathf.Approximately(alignment, directionalAlignment) && distance >= directionalDistance))
            {
                continue;
            }

            directionalAlignment = alignment;
            directionalDistance = distance;
            directionalReceiver = receiver;
        }

        return directionalReceiver ?? closestReceiver;
    }

    /// <summary>只从正式控制器读取世界光标，兼容鼠标、手柄准线与外部输入租约。</summary>
    private bool TryGetInteractionPointer(out Vector2 pointer)
    {
        pointer = default;
        if (gameController == null) return false;
        try
        {
            if (!gameController.TryGetMouseWorldPosition(out Vector3 worldPosition))
                return false;

            pointer = worldPosition;
            return true;
        }
        catch (MissingReferenceException) { return false; }
    }

    /// <summary>只读有效水面，不会为光标触发区块生成。</summary>
    private static bool IsPointerOverWater(Vector2 pointer)
        => ChunkMgr.ExistingInstance != null &&
           ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(pointer, out RuntimeTerrainTileSample tile) &&
           tile.LiquidDepth > 0f;

    /// <summary>喝水必须指向可触及的水面，且该落点没有更优先的实体交互。</summary>
    private bool CanPointAtEnvironmentWater()
    {
        if (!TryGetInteractionPointer(out Vector2 pointer) || item == null ||
            WorldTopologyRuntime.Distance(item.transform.position, pointer) > maxInteractDistance ||
            !IsPointerOverWater(pointer)) return false;
        return FindReceiverAtPointer(pointer) == null;
    }

    private bool TryGetInteractionDirection(out Vector2 direction)
    {
        direction = default;
        if (gameController == null || item == null ||
            (!gameController.IsUsingMobile && !gameController.IsUsingGamepad))
        {
            return false;
        }

        try
        {
            if (!gameController.TryGetMouseWorldPosition(out Vector3 pointerWorld))
                return false;

            Vector2 pointerOffset = WorldTopologyRuntime.ShortestDelta(
                item.transform.position, pointerWorld);
            if (pointerOffset.sqrMagnitude < 0.0001f)
                return false;

            direction = pointerOffset.normalized;
            return true;
        }
        catch (MissingReferenceException)
        {
            return false;
        }
    }

    private static int InteractionQueryLayerMask
    {
        get
        {
            int mask = Physics2D.DefaultRaycastLayers;
            int senderLayer = CombatPhysicsChannels.DamageSenderLayer;
            int receiverLayer = CombatPhysicsChannels.DamageReceiverLayer;
            if (senderLayer >= 0)
                mask &= ~(1 << senderLayer);
            if (receiverLayer >= 0)
                mask &= ~(1 << receiverLayer);
            return mask;
        }
    }

    /// <summary>伤害专用碰撞体不参与任何交互查询，避免交互链解析到 DamageSender / Mod_DamageReceiver。</summary>
    private static bool IsCombatOnlyCollider(Collider2D collider)
    {
        if (collider == null)
            return true;

        int layer = collider.gameObject.layer;
        return layer == CombatPhysicsChannels.DamageSenderLayer ||
               layer == CombatPhysicsChannels.DamageReceiverLayer;
    }

    /// <summary>接口引用先按 Unity 原生生命周期判空，纯数据目标仍沿用普通引用生命周期。</summary>
    private static bool IsInteractionReceiverAlive(IInteractable receiver)
        => receiver != null && (receiver is not UnityEngine.Object unityObject || unityObject != null);

    /// <summary>清除销毁目标的缓存，不向已经失效的对象发送交互或描边回调。</summary>
    private void PruneDestroyedInteractionTargets()
    {
        if (!IsInteractionReceiverAlive(currentReceiver))
            StopCurrentInteraction();
        if (!IsInteractionReceiverAlive(heldReceiver))
            heldReceiver = null;
        if (!IsInteractionReceiverAlive(previewReceiver))
            ClearInteractionPreview();
        for (int i = receiversInRange.Count - 1; i >= 0; i--)
            if (!IsInteractionReceiverAlive(receiversInRange[i]))
                receiversInRange.RemoveAt(i);
        for (int i = spatialCandidates.Count - 1; i >= 0; i--)
            if (!IsInteractionReceiverAlive(spatialCandidates[i]))
                spatialCandidates.RemoveAt(i);
    }

    /// <summary>过滤自身及失效组件，保证描边目标与交互目标来源一致。</summary>
    private bool IsInteractionCandidate(IInteractable receiver, Component receiverComponent)
    {
        if (!IsInteractionReceiverAlive(receiver))
        {
            return false;
        }

        // 带世界目标接口的组件也须先排除手持物，避免绕过世界交互边界。
        if (receiverComponent != null)
        {
            if (!receiverComponent.gameObject.activeInHierarchy)
                return false;
            Item receiverItem = receiverComponent.GetComponentInParent<Item>();
            if (receiverItem == item || (receiverItem != null && receiverItem.InHand))
                return false;
        }
        if (receiver is IWorldInteractionTarget worldTarget)
            return worldTarget.IsValid && receiver.CanInteract(item);
        if (receiverComponent == null)
            return false;

        // 只有按下交互键确实会触发打开、采集或其他玩法结果的目标才显示描边。
        return receiver.CanInteract(item);
    }

    /// <summary>实体与纯数据机械共用世界坐标查询入口。</summary>
    private static bool TryGetInteractionPosition(IInteractable receiver, out Vector3 position)
    {
        position = default;
        if (!IsInteractionReceiverAlive(receiver))
            return false;
        if (receiver is IWorldInteractionTarget worldTarget)
        {
            if (!worldTarget.IsValid)
                return false;
            position = worldTarget.WorldPosition;
            return true;
        }
        if (receiver is Component component && component != null)
        {
            position = component.transform.position;
            return true;
        }
        return false;
    }

    /// <summary>仅本机拥有的玩家手部模块可以驱动交互描边。</summary>
    private bool IsLocalInteractionOwner()
    {
        Player ownerPlayer = item as Player ?? item?.Owner as Player ??
            item?.GetComponentInParent<Player>();
        return ownerPlayer != null && ownerPlayer.IsLocalProfile;
    }

    /// <summary>停止当前本地目标的描边，避免对象回收或切换目标后残留。</summary>
    private void ClearInteractionPreview()
    {
        IInteractable clearedReceiver = previewReceiver;
        InteractionTargetOutline clearedOutline = previewOutline;
        previewReceiver = null;
        previewReceiverComponent = null;
        previewOutline = null;
        if (IsInteractionReceiverAlive(clearedReceiver) && clearedReceiver is IWorldInteractionPreview entityPreview)
            entityPreview.SetInteractionHighlighted(false);
        if (clearedOutline != null)
            clearedOutline.SetHighlighted(false);
    }

    private void OnDisable()
    {
        UnbindInput();
        EndEnvironmentActionHold();
        CancelCurrentInteraction();
    }

    private void OnDestroy()
    {
        UnbindInput();
        ClearInteractionPreview();
    }
    #endregion

    #region 交互流程

    private bool StartInteraction(IInteractable receiver)
    {
        if (!IsInteractionReceiverAlive(receiver))
            return false;

        var receiverComponent = receiver as Component;
        if (!IsInteractionCandidate(receiver, receiverComponent)) return false;

        SetInteractionPreview(receiver);
        EndEnvironmentActionHold();

        // 切换目标前先完整结束旧交互，避免 UI/占用状态残留。
        if (currentReceiver != null && currentReceiver != receiver)
            StopCurrentInteraction();

        if (!IsInteractionReceiverAlive(receiver))
            return false;
        currentReceiver = receiver;
        currentReceiverComponent = receiverComponent;
        currentReceiver.OnInteractStart(item);
        PruneDestroyedInteractionTargets();
        return true;
    }

    /// <summary>单次按键、点选或外部触发完成时发送释放沿，不进入持续更新通道。</summary>
    private void CompleteSingleInteraction(IInteractable receiver)
    {
        if (IsInteractionReceiverAlive(receiver))
            receiver.OnInteractEnd(item);
    }

    /// <summary>结束当前交互并清理一次性探测状态。</summary>
    public void CancelCurrentInteraction()
    {
        ClearInteractionPreview();
        StopCurrentInteraction();
        receiversInRange.Clear();
        spatialCandidates.Clear();
    }

    private void StopCurrentInteraction()
    {
        IInteractable stoppedReceiver = currentReceiver;
        if (heldReceiver == stoppedReceiver)
            heldReceiver = null;
        currentReceiver = null;
        currentReceiverComponent = null;
        if (IsInteractionReceiverAlive(stoppedReceiver))
            stoppedReceiver.OnInteractCancel(item);
    }

    /// <summary>交互键保持期间才转发持续交互；鼠标单击和外部单次交互不会自动进入该通道。</summary>
    private void TickHeldInteraction()
    {
        if (!IsInteractionReceiverAlive(heldReceiver) || currentReceiver != heldReceiver)
        {
            heldReceiver = null;
            return;
        }

        currentReceiver.OnInteractUpdate(item);
    }

    /// <summary>把交互接口归属到 Item，供结构化调试读取，不改变交互选择规则。</summary>
    private static int ResolveInteractionTargetGuid(IInteractable receiver)
    {
        if (!IsInteractionReceiverAlive(receiver)) return 0;
        if (receiver is IWorldInteractionTarget worldTarget) return worldTarget.TargetGuid;
        Component component = receiver as Component;
        Item target = component != null ? component.GetComponentInParent<Item>() : null;
        return target?.itemData?.Guid ?? 0;
    }

    private void ValidateCurrentInteractionDistance()
    {
        if (currentReceiver == null)
            return;

        if (!TryGetInteractionPosition(currentReceiver, out Vector3 targetPosition))
        {
            receiversInRange.Remove(currentReceiver);
            StopCurrentInteraction();
            return;
        }

        float currentDistance = WorldTopologyRuntime.Distance(item.transform.position, targetPosition);
        if (currentDistance <= maxInteractDistance)
            return;

        receiversInRange.Remove(currentReceiver);
        StopCurrentInteraction();
    }

    private bool IsGameplayInputLocked()
    {
        return gameController != null && gameController.IsGameplayInputLocked;
    }

    /// <summary>放置预览期间停用左键采集；交互键仍可独立操作世界设施。</summary>
    private bool IsHeldBuildingPlacementActive()
    {
        if (item == null)
            return false;

        hotBar ??= item.GetComponentInChildren<Mod_HotBar>(true);
        Item heldItem = hotBar?.CurentSelectItem;
        Mod_Building building = heldItem?.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
        return building != null && building.IsItemInInventory && building.IsPlacementModeActive;
    }

    #endregion
}

internal interface IFocusPoint
{
}
