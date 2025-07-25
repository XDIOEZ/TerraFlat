using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements.Experimental;

public partial class FaceMouse : Module
{
    public FaceMouseData Data = new FaceMouseData();
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }
    public PlayerController PlayerController;

    public override void Awake()
    {
        if(_Data.Name == "")
        {
            _Data.Name = ModText.FaceMouse;
        }
    }
    public override void Load()
    {
        ModData.ReadData(ref Data);
        //获取PlayerController
        PlayerController = item.Mods[ModText.Controller].GetComponent<PlayerController>();
    }
    public virtual void Update()
    {
        PlayerTakeItem_FaceMouse();
    }
    public void PlayerTakeItem_FaceMouse()
    {
        if (PlayerController == null)
        {
            return;
        }
        //获取鼠标
        Data.TargetPosition = PlayerController.GetMouseWorldPosition();
        FaceToMouse(PlayerController.GetMouseWorldPosition());
    }
    public void FaceToMouse(Vector3 targetPosition)
    {
        Vector2 direction = targetPosition - transform.parent.position;
        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        Transform grandParent = transform.parent.parent;
        if (grandParent != null && Mathf.Abs(grandParent.localEulerAngles.y - 180f) < 90f)
        {
            targetAngle = 180f - targetAngle;
        }

        float currentAngle = transform.parent.localEulerAngles.z;

        // RotationSpeed in degrees per second
        float rotationSpeed = 180f / Mathf.Max(0.01f, Data.RotationDuration); // Prevent division by zero

        float smoothedAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);

        transform.parent.localRotation = Quaternion.Euler(0, 0, smoothedAngle);
    }


    public override void Save()
    {
        ModData.WriteData( Data);
    }
    [System.Serializable]
    [MemoryPackable]
    public partial class FaceMouseData
    {
        //旋转180度所需时间
        public float RotationDuration = 1;
        //目标位置
        public Vector3 TargetPosition = Vector3.zero;
    }

}
