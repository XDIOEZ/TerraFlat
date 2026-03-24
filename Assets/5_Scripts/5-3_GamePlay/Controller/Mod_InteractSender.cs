using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Mod_InteractSender : Module
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
    public List<Mod_InteractReciver> receiversInRange = new List<Mod_InteractReciver>();
    public Mod_InteractReciver currentReceiver;
    public float maxInteractDistance = 2f;
    private bool shouldDisableColliderAfterInteract;

    public override void Load()
    {
        ModSaveData.ReadData(ref RawData);
        interactCollider = GetComponent<Collider2D>();
        gameController = item.GetComponentInChildren<GameController>();

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
        if (shouldDisableColliderAfterInteract)
        {
            DisableInteractCollider();
            shouldDisableColliderAfterInteract = false;
        }

        ValidateCurrentInteractionDistance();
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
    }

    private void UnbindInput()
    {
        if (gameController == null || gameController._inputActions == null)
            return;

        var action = gameController._inputActions.Win10.E;
        action.performed -= OnInteractPressed;
        action.canceled -= OnInteractReleased;
    }

    private void OnInteractPressed(InputAction.CallbackContext ctx)
    {
        if (interactCollider == null)
            return;

        // 每次按下交互键时刷新范围缓存，避免使用过期触发器列表。
        receiversInRange.Clear();
        interactCollider.enabled = true;
    }

    private void OnInteractReleased(InputAction.CallbackContext ctx)
    {
        if (interactCollider == null)
            return;

        DisableInteractCollider();
    }

    private void OnDisable()
    {
        UnbindInput();
        StopCurrentInteraction();
        receiversInRange.Clear();

        if (interactCollider != null)
            interactCollider.enabled = false;

        shouldDisableColliderAfterInteract = false;
    }

    private void OnDestroy()
    {
        UnbindInput();
    }
    #endregion

    #region 交互流程

    private void StartInteraction(Mod_InteractReciver receiver)
    {
        if (receiver == null)
            return;

        if (currentReceiver != null && currentReceiver != receiver)
            currentReceiver.Interact_Cancel(item);

        currentReceiver = receiver;
        currentReceiver.Interact_Start(item);

        // 交互建立后失活触发器，后续交互维持仅依赖距离检测。
        shouldDisableColliderAfterInteract = true;
    }

    private void StopCurrentInteraction()
    {
        if (currentReceiver == null)
            return;

        currentReceiver.Interact_Cancel(item);
        currentReceiver = null;
    }

    private void ValidateCurrentInteractionDistance()
    {
        if (currentReceiver == null)
            return;

        if (currentReceiver.item == null)
        {
            receiversInRange.Remove(currentReceiver);
            StopCurrentInteraction();
            DisableInteractCollider();
            return;
        }

        float currentDistance = Vector2.Distance(item.transform.position, currentReceiver.item.transform.position);
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

    #endregion




    private void OnTriggerEnter2D(Collider2D other)
    {
        if (interactCollider == null || !interactCollider.enabled)
            return;

        var receiver = other.GetComponent<Mod_InteractReciver>();
        if (receiver == null)
            return;

        if (!receiversInRange.Contains(receiver))
            receiversInRange.Add(receiver);

        StartInteraction(receiver);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (interactCollider == null || !interactCollider.enabled)
            return;

        var receiver = other.GetComponent<Mod_InteractReciver>();
        if (receiver == null)
            return;

        if (!receiversInRange.Contains(receiver))
            receiversInRange.Add(receiver);

        if (currentReceiver != receiver)
            StartInteraction(receiver);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var receiver = other.GetComponent<Mod_InteractReciver>();
        if (receiver == null)
            return;

        receiversInRange.Remove(receiver);
    }
}
