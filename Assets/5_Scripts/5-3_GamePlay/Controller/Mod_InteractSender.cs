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

        interactCollider.enabled = true;
    }

    private void OnInteractReleased(InputAction.CallbackContext ctx)
    {
        if (interactCollider == null)
            return;

        interactCollider.enabled = false;
    }

    private void OnDisable()
    {
        UnbindInput();

        if (interactCollider != null)
            interactCollider.enabled = false;
    }

    private void OnDestroy()
    {
        UnbindInput();
    }
    #endregion




    private void OnTriggerEnter2D(Collider2D other)
    {
        if (interactCollider == null || !interactCollider.enabled)
            return;

        var receiver = other.GetComponent<Mod_InteractReciver>();
        if (receiver != null)
        {
            receiver.Interact_Start(item);
        }
    }
}
