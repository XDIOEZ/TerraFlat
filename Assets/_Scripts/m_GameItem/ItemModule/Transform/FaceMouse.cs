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
    public TurnBody turnBody; // 添加TurnBody引用

    // 需要跟随鼠标旋转的对象列表
    [Tooltip("需要跟随鼠标旋转的对象列表，列表为空时脚本不执行任何操作")]
    public List<Transform> targetRotationTransforms = new List<Transform>();

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

        // 优先从物品所有者获取Controller
        PlayerController = item.Owner != null
            ? item.Owner.itemMods.GetMod_ByID(ModText.Controller).GetComponent<PlayerController>()
            : item.itemMods.GetMod_ByID(ModText.Controller).GetComponent<PlayerController>();

        // 获取TurnBody组件
        turnBody = item.Owner != null
            ? item.Owner.itemMods.GetMod_ByID(ModText.TrunBody) as TurnBody
            : item.itemMods.GetMod_ByID(ModText.TrunBody) as TurnBody;

        // 如果列表为空 → 自动装填父对象
        if (targetRotationTransforms.Count == 0 )
        {
            targetRotationTransforms.Add(transform);
            Debug.Log($"[FaceMouse] 自动添加对象 {transform.name} 到旋转列表", this);
        }
    }


    public override void ModUpdate(float deltaTime)
    {
        // 列表为空时直接返回，不执行任何操作
        if (targetRotationTransforms.Count == 0) return;

        PlayerTakeItem_FaceMouse(deltaTime);
    }

    public void PlayerTakeItem_FaceMouse(float deltaTime)
    {
        if (PlayerController == null)
        {
            Debug.LogWarning("PlayerController 获取失败：FaceMouse 无法运行");
            return;
        }

        // 更新鼠标世界位置（供外部脚本调用）
        Data.FocusPoint = PlayerController.GetMouseWorldPosition();

        // 仅在启用旋转且列表有对象时执行逻辑
        if (Data.ActivateRotation)
        {
            FaceToMouse(Data.FocusPoint, deltaTime);
        }
    }

    /// <summary>
    /// 让旋转列表中的所有有效对象同步朝向鼠标，近距离时停止旋转
    /// </summary>
    public void FaceToMouse(Vector3 targetPosition, float deltaTime)
    {
        // 获取有效旋转目标（过滤空对象）
        List<Transform> validTargets = GetValidRotationTargets();
        if (validTargets.Count == 0) return;

        // 获取玩家当前朝向
        float playerFacingDirection = 1f; // 1表示朝右，-1表示朝左
        if (turnBody != null)
        {
            playerFacingDirection = turnBody.currentDirection.x;
        }

        // 遍历所有有效目标执行旋转
        foreach (var targetTrans in validTargets)
        {
            // 计算目标对象到鼠标位置的距离
            float distanceToMouse = Vector2.Distance(targetTrans.position, targetPosition);

            // 如果距离小于阈值，则停止旋转，保留当前角度
            if (distanceToMouse <= Data.StopRotationDistance)
                continue;

            // 计算目标对象到鼠标位置的方向
            Vector2 direction = targetPosition - targetTrans.position;
            
            // 根据玩家朝向调整方向
            if (playerFacingDirection < 0) // 玩家朝左
            {
                // 镜像X轴方向
                direction.x = -direction.x;
            }
            
            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            // 平滑旋转到目标角度
            float currentAngle = targetTrans.localEulerAngles.z;
            float smoothedAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, Data.RotationSpeed * deltaTime);

            // 应用旋转（仅Z轴，保持X和Y轴不变）
            Vector3 currentLocalEulerAngles = targetTrans.localEulerAngles;
            targetTrans.localRotation = Quaternion.Euler(currentLocalEulerAngles.x, currentLocalEulerAngles.y, smoothedAngle);
        }
    }

    /// <summary>
    /// 获取列表中的有效对象（过滤空引用）
    /// </summary>
    private List<Transform> GetValidRotationTargets()
    {
        List<Transform> validTargets = new List<Transform>();

        foreach (var trans in targetRotationTransforms)
        {
            if (trans != null)
            {
                validTargets.Add(trans);
            }
            else
            {
                Debug.LogWarning($"[FaceMouse] 旋转列表中存在空对象，请移除无效项", this);
            }
        }

        return validTargets;
    }

    public override void Save()
    {
        ModData.WriteData(Data);
    }

    [System.Serializable]
    [MemoryPackable]
    public partial class FaceMouseData
    {
        /// <summary>旋转速度（度/秒）</summary>
        public float RotationSpeed = 180f;

        /// <summary>鼠标在世界空间中的位置</summary>
        public Vector2 FocusPoint = Vector2.zero;

        /// <summary>是否启用旋转功能</summary>
        public bool ActivateRotation = true;

        /// <summary>停止旋转的距离阈值，鼠标接近到此距离内时不再旋转</summary>
        [Tooltip("鼠标与物体的距离小于此值时，停止旋转并保留当前角度")]
        public float StopRotationDistance = 0.1f; // 默认1.5单位距离
    }
}