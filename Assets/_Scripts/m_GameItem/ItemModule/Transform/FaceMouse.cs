using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class FaceMouse : Module
{
    public FaceMouseData Data = new FaceMouseData();
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    public PlayerController PlayerController;

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.FaceMouse;
        }
    }

    public override void Load()
    {
        ModData.ReadData(ref Data);
        PlayerController = item.itemMods.GetMod_ByID(ModText.Controller).GetComponent<PlayerController>();
    }

    public override void Action(float deltaTime)
    {
        PlayerTakeItem_FaceMouse(deltaTime);
    }

    public void PlayerTakeItem_FaceMouse(float deltaTime)
    {
        if (PlayerController == null)
        {
            Debug.LogWarning("PlayerController 获取失败：FaceMouse 无法运行");
            return;
        }

        Data.FocusPoint = PlayerController.GetMouseWorldPosition();

        // ✅ 只有在启用旋转时才执行朝向逻辑
        if (Data.ActivateRotation)
        {
            FaceToMouse(Data.FocusPoint, deltaTime);
        }
    }


    public void FaceToMouse(Vector3 targetPosition, float deltaTime)
    {
        if (transform.parent == null) return;

        Vector2 direction = targetPosition - transform.parent.position;
        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // ✅ 判断角色是否朝左
        Transform grandParent = transform.parent.parent;
        if (grandParent != null && grandParent.lossyScale.x < 0)
        {
            targetAngle = 180f - targetAngle;
        }

        float currentAngle = transform.parent.localEulerAngles.z;
        float smoothedAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, Data.RotationSpeed * deltaTime);

        transform.parent.localRotation = Quaternion.Euler(0, 0, smoothedAngle);
    }

    public override void Save()
    {
        ModData.WriteData(Data);
    }

    [System.Serializable]
    [MemoryPackable]
    public partial class FaceMouseData
    {
        // ✅ 旋转速度（单位：度/秒），默认 180
        public float RotationSpeed = 180f;

        // 鼠标目标位置
        public Vector2 FocusPoint = Vector2.zero;

        public bool ActivateRotation = true;
    }
}
