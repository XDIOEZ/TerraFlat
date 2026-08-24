using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class Mod_InteractSender : Module,IFocusPoint,ITrunDirection
{
    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }
    #endregion


    #region 模组参数

    [SerializeReference]
    public List<string> RawData = new List<string>();
    public GameController gameController;
    [ShowInInspector]
    public List<IInteractable> receiversInRange = new List<IInteractable>();
    public IInteractable currentReceiver;
    private Component currentReceiverComponent;
    private IInteractable previewReceiver;
    private Component previewReceiverComponent;
    private InteractionTargetOutline previewOutline;
    public const float DefaultMaxInteractDistance = 2f;
    // 交互距离默认值；建筑放置距离运行时复用该值。
    public float maxInteractDistance = DefaultMaxInteractDistance;
    // 交互是纯查询通道，不再创建或启用任何 Trigger；该缓冲区只服务 Physics2D Overlap 查询。
    private readonly Collider2D[] interactionOverlapBuffer = new Collider2D[32];

    public override void Load()
    {
        ModSaveData.ReadData(ref RawData);
        gameController = item != null ? item.GetComponentInChildren<GameController>() : null;
        BindInput();
    }

    public override void Save()
    {
        ModSaveData.WriteData(RawData);
    }

    public override void ModUpdate(float deltaTime)
    {
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

        // 世界物体支持左键直接交互；GameController 已过滤 UI 与锁定状态。
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

        bool interacted = TryInteractAtCurrentPosition();
        if (!interacted)
            BeginEnvironmentActionHold();
    }

    /// <summary>
    /// 主动扫描并触发当前最近目标；同一目标允许每次按键重新执行，
    /// 避免目标曾因维度加载等瞬态条件拒绝后被永久缓存。
    /// </summary>
    public bool TryInteractAtCurrentPosition()
    {
        if (!IsLocalInteractionOwner() || IsGameplayInputLocked())
            return false;

        // 每次按下交互键都做一次纯查询，交互链不再启用任何 Physics2D Trigger。
        receiversInRange.Clear();
        return RefreshReceiversAtCurrentPosition();
    }

    private void OnInteractReleased(InputAction.CallbackContext ctx)
    {
        if (gameController != null && !gameController.IsGameplayInputAllowed(ctx))
            return;

        EndEnvironmentActionHold();
    }

    /// <summary>把左键落点转换为世界交互，兼容石门等没有手持物品的 IInteractable。</summary>
    private void OnPointerClick()
    {
        if (!IsLocalInteractionOwner() || IsGameplayInputLocked() ||
            gameController == null || item == null)
            return;

        Vector3 pointerWorld;
        try
        {
            pointerWorld = gameController.GetMouseWorldPosition();
        }
        catch (MissingReferenceException)
        {
            return;
        }

        Physics2D.SyncTransforms();
        Collider2D[] colliders = Physics2D.OverlapPointAll(pointerWorld, InteractionQueryLayerMask);
        IInteractable selectedReceiver = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (IsCombatOnlyCollider(colliders[i]))
                continue;

            IInteractable receiver = WorldTopologyColliderProxy.ResolveComponent<IInteractable>(colliders[i]);
            Component receiverComponent = receiver as Component;
            if (!IsInteractionCandidate(receiver, receiverComponent))
                continue;

            float distance = WorldTopologyRuntime.Distance(
                item.transform.position, receiverComponent.transform.position);
            if (distance > maxInteractDistance || distance >= closestDistance)
                continue;

            closestDistance = distance;
            selectedReceiver = receiver;
        }

        if (selectedReceiver != null)
            StartInteraction(selectedReceiver);
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

    /// <summary>持续寻找当前真正会被交互键选中的目标，只更新本地视觉提示。</summary>
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
        previewOutline?.SetHighlighted(true);
    }

    /// <summary>扫描交互半径，按当前指向优先、距离规则兜底。</summary>
    private IInteractable FindClosestReceiverAtCurrentPosition(bool collectReceivers)
    {
        if (item == null || !item.gameObject.activeInHierarchy)
            return null;

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

        for (int i = 0; i < count; i++)
        {
            Collider2D overlap = interactionOverlapBuffer[i];
            if (IsCombatOnlyCollider(overlap))
                continue;

            IInteractable receiver = WorldTopologyColliderProxy.ResolveComponent<IInteractable>(overlap);
            Component receiverComponent = receiver as Component;
            if (!IsInteractionCandidate(receiver, receiverComponent))
                continue;

            float distance = WorldTopologyRuntime.Distance(
                item.transform.position, receiverComponent.transform.position);
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
                item.transform.position, receiverComponent.transform.position);
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
            Vector2 pointerOffset = WorldTopologyRuntime.ShortestDelta(
                item.transform.position, gameController.GetMouseWorldPosition());
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

    /// <summary>伤害专用碰撞体不参与任何交互查询，避免交互链解析到 DamageSender / DamageReceiver。</summary>
    private static bool IsCombatOnlyCollider(Collider2D collider)
    {
        if (collider == null)
            return true;

        int layer = collider.gameObject.layer;
        return layer == CombatPhysicsChannels.DamageSenderLayer ||
               layer == CombatPhysicsChannels.DamageReceiverLayer;
    }

    /// <summary>过滤自身及失效组件，保证描边目标与交互目标来源一致。</summary>
    private bool IsInteractionCandidate(IInteractable receiver, Component receiverComponent)
    {
        if (receiver == null || receiverComponent == null ||
            !receiverComponent.gameObject.activeInHierarchy)
        {
            return false;
        }

        Item receiverItem = receiverComponent.GetComponentInParent<Item>();
        if (receiverItem == item)
            return false;

        // 只有按下交互键确实会触发打开、采集或其他玩法结果的目标才显示描边。
        return receiver.CanInteract(item);
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
        previewOutline?.SetHighlighted(false);
        previewReceiver = null;
        previewReceiverComponent = null;
        previewOutline = null;
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
        if (receiver == null)
            return false;

        var receiverComponent = receiver as Component;
        if (receiverComponent == null)
        {
            Debug.LogError("IInteractable 必须由 Component/MonoBehaviour 实现");
            return false;
        }

        SetInteractionPreview(receiver);

        // 切换目标前先完整结束旧交互，避免 UI/占用状态残留。
        if (currentReceiver != null && currentReceiver != receiver)
            StopCurrentInteraction();

        currentReceiver = receiver;
        currentReceiverComponent = receiverComponent;
        currentReceiver.OnInteractStart(item);
        return true;
    }

    /// <summary>结束当前交互并清理一次性探测状态。</summary>
    public void CancelCurrentInteraction()
    {
        ClearInteractionPreview();
        StopCurrentInteraction();
        receiversInRange.Clear();
    }

    private void StopCurrentInteraction()
    {
        if (currentReceiver == null)
            return;

        currentReceiver.OnInteractCancel(item);
        currentReceiver = null;
        currentReceiverComponent = null;
    }

    private void ValidateCurrentInteractionDistance()
    {
        if (currentReceiver == null)
            return;

        if (currentReceiverComponent == null)
        {
            receiversInRange.Remove(currentReceiver);
            StopCurrentInteraction();
            return;
        }

        float currentDistance = WorldTopologyRuntime.Distance(item.transform.position, currentReceiverComponent.transform.position);
        if (currentDistance <= maxInteractDistance)
            return;

        receiversInRange.Remove(currentReceiver);
        StopCurrentInteraction();
    }

    private bool IsGameplayInputLocked()
    {
        return gameController != null && gameController.IsGameplayInputLocked;
    }

    #endregion
}

internal interface IFocusPoint
{
}
