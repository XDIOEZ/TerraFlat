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
    public Collider2D interactCollider;
    public GameController gameController;
    [ShowInInspector]
    public List<IInteractable> receiversInRange = new List<IInteractable>();
    public IInteractable currentReceiver;
    private Component currentReceiverComponent;
    private IInteractable previewReceiver;
    private Component previewReceiverComponent;
    private InteractionTargetOutline previewOutline;
    public float maxInteractDistance = 2f;
    private bool shouldDisableColliderAfterInteract;
    // 出口可能在玩家已经到位后才生成；按键时主动扫描，避免只依赖首次物理触发回调。
    private readonly Collider2D[] interactionOverlapBuffer = new Collider2D[32];

    public override void Load()
    {
        ModSaveData.ReadData(ref RawData);
        interactCollider = GetComponent<Collider2D>();
        gameController = item != null ? item.GetComponentInChildren<GameController>() : null;

        if (interactCollider != null)
            interactCollider.enabled = false;

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

        if (shouldDisableColliderAfterInteract)
        {
            DisableInteractCollider();
            shouldDisableColliderAfterInteract = false;
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
        if (!IsLocalInteractionOwner() || IsGameplayInputLocked() || interactCollider == null)
            return false;

        // 每次按下交互键时刷新范围缓存，避免使用过期触发器列表。
        receiversInRange.Clear();
        interactCollider.enabled = true;
        return RefreshReceiversAtCurrentPosition();
    }

    private void OnInteractReleased(InputAction.CallbackContext ctx)
    {
        EndEnvironmentActionHold();
        if (IsGameplayInputLocked())
        {
            return;
        }

        if (interactCollider == null)
            return;

        DisableInteractCollider();
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
        Collider2D[] colliders = Physics2D.OverlapPointAll(pointerWorld);
        IInteractable selectedReceiver = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < colliders.Length; i++)
        {
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
        if (!IsLocalInteractionOwner() || interactCollider == null ||
            item == null || !item.gameObject.activeInHierarchy)
        {
            ClearInteractionPreview();
            return;
        }

        SetInteractionPreview(FindClosestReceiverAtCurrentPosition(collectReceivers: false));
    }

    /// <summary>统一处理扫描、按键、鼠标和触发器识别到的目标高亮。</summary>
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
        if (item == null || interactCollider == null || !item.gameObject.activeInHierarchy)
            return null;

        Physics2D.SyncTransforms();
        int count = Physics2D.OverlapCircleNonAlloc(
            item.transform.position,
            Mathf.Max(0.01f, maxInteractDistance),
            interactionOverlapBuffer);
        IInteractable closestReceiver = null;
        float closestDistance = float.MaxValue;
        IInteractable directionalReceiver = null;
        float directionalAlignment = 0f;
        float directionalDistance = float.MaxValue;
        bool hasInteractionDirection = TryGetInteractionDirection(out Vector2 interactionDirection);

        for (int i = 0; i < count; i++)
        {
            IInteractable receiver = WorldTopologyColliderProxy.ResolveComponent<IInteractable>(
                interactionOverlapBuffer[i]);
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

        // 交互建立后失活触发器，后续交互维持仅依赖距离检测。
        shouldDisableColliderAfterInteract = true;
        return true;
    }

    /// <summary>结束当前交互并清理一次性探测状态。</summary>
    public void CancelCurrentInteraction()
    {
        ClearInteractionPreview();
        StopCurrentInteraction();
        receiversInRange.Clear();
        DisableInteractCollider();
        shouldDisableColliderAfterInteract = false;
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
            DisableInteractCollider();
            return;
        }

        float currentDistance = WorldTopologyRuntime.Distance(item.transform.position, currentReceiverComponent.transform.position);
        if (currentDistance <= maxInteractDistance)
            return;

        receiversInRange.Remove(currentReceiver);
        StopCurrentInteraction();
        DisableInteractCollider();
    }

    private void DisableInteractCollider()
    {
        if (interactCollider == null)
            return;

        interactCollider.enabled = false;
    }

    private bool IsGameplayInputLocked()
    {
        return gameController != null && gameController.IsGameplayInputLocked;
    }

    #endregion




    private void OnTriggerEnter2D(Collider2D other)
    {
        if (interactCollider == null || !interactCollider.enabled)
            return;

        var receiver = WorldTopologyColliderProxy.ResolveComponent<IInteractable>(other);
        Component receiverComponent = receiver as Component;
        if (!IsInteractionCandidate(receiver, receiverComponent))
            return;

        if (!receiversInRange.Contains(receiver))
            receiversInRange.Add(receiver);

        if (currentReceiver != receiver &&
            (previewReceiver == null || previewReceiver == receiver))
            StartInteraction(receiver);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (interactCollider == null || !interactCollider.enabled)
            return;

        var receiver = WorldTopologyColliderProxy.ResolveComponent<IInteractable>(other);
        Component receiverComponent = receiver as Component;
        if (!IsInteractionCandidate(receiver, receiverComponent))
            return;

        if (!receiversInRange.Contains(receiver))
            receiversInRange.Add(receiver);

        if (currentReceiver != receiver &&
            (previewReceiver == null || previewReceiver == receiver))
            StartInteraction(receiver);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var receiver = WorldTopologyColliderProxy.ResolveComponent<IInteractable>(other);
        Component receiverComponent = receiver as Component;
        if (receiver == null || receiverComponent == null)
            return;

        receiversInRange.Remove(receiver);
        if (previewReceiver == receiver)
            ClearInteractionPreview();
    }
}

internal interface IFocusPoint
{
}
