using Sirenix.OdinInspector;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class Mod_TurnBody : Module, ITurnBody
{
    [Header("转向控制")]
    [SerializeField, Range(0.05f, 5f), Tooltip("转向所需时间（秒）")]
    private float rotationDuration = 0.3f;

    [SerializeField, Tooltip("需要控制旋转的目标对象列表，默认自动获取子对象中含Animator的物体")]
    public List<Transform> controlledTransforms = new();

    [SerializeField, Tooltip("当前面向方向，默认右方")]
    public Vector2 currentDirection = Vector2.right;

    [SerializeField, ReadOnly, Tooltip("是否正在转身（由脚本自动控制）")]
    public bool isTurning = false;

    public UltEvent<Vector2> OnTrun = new UltEvent<Vector2>();//TODO 转身事件 输入转身的方向

    private float turnTimeElapsed;
    private float startY;
    private float targetY;

    public Mod_FocusPoint faceMouse;

    public Ex_ModData modData;
    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.TrunBody;
        }
    }

    public override void Load()
    {
        faceMouse = item.itemMods.GetMod_ByID(ModText.FocusPoint) as Mod_FocusPoint;
        controlledTransforms.Clear();

        // 获取所有子物体上的 Animator 组件
        Animator[] animators = item.GetComponentsInChildren<Animator>();
        if (animators.Length > 0)
        {
            foreach (var animator in animators)
            {
                if (animator != null)
                    AddControlledTransform(animator.transform);
            }
        }

        if (faceMouse == null)
            Debug.LogError("[TurnBody] 初始化失败：FaceMouse 模块未找到！"+item.name);
    }

    public override void ModUpdate(float deltaTime)
    {
        UpdateWork();
        UpdateTurn(deltaTime);
    }

    private void UpdateWork()
    {
        if (faceMouse == null) return;

        Vector2 characterPos = transform.position;
        Vector2 mousePos = faceMouse.Data.See_Point;

        Vector2 directionToTarget = mousePos - characterPos;
        if (directionToTarget.sqrMagnitude < 0.001f) return;

        TurnBodyToDirection(directionToTarget);
    }

    public void TurnBodyToDirection(Vector2 targetDirection)
    {
        if (isTurning) return;

        OnTrun.Invoke(targetDirection);
        if (Mathf.Abs(targetDirection.x) < 0.01f) return;

        float targetSign = Mathf.Sign(targetDirection.x);
        float facingSign = Mathf.Sign(currentDirection.x);
        if (facingSign == targetSign) return;

        currentDirection = (targetDirection.x > 0) ? Vector2.right : Vector2.left;

        isTurning = true;
        turnTimeElapsed = 0f;

        if (controlledTransforms.Count == 0) return;

        startY = NormalizeAngle(controlledTransforms[0].eulerAngles.y);

        targetY = (currentDirection == Vector2.right) ? 0f : 180f;
    }

    public void UpdateTurn(float deltaTime)
    {
        if (!isTurning) return;

        turnTimeElapsed += deltaTime;
        float t = Mathf.Clamp01(turnTimeElapsed / rotationDuration);
        float newY = Mathf.LerpAngle(startY, targetY, t);

        foreach (var tform in controlledTransforms)
        {
            if (tform != null)
            {
                // 只修改Y轴旋转，保持X和Z轴不变
                Vector3 currentEulerAngles = tform.eulerAngles;
                tform.rotation = Quaternion.Euler(currentEulerAngles.x, newY, currentEulerAngles.z);
            }
        }

        if (Mathf.Abs(Mathf.DeltaAngle(newY, targetY)) < 0.5f || t >= 1f)
        {
            foreach (var tform in controlledTransforms)
            {
                if (tform != null)
                {
                    // 只修改Y轴旋转，保持X和Z轴不变
                    Vector3 currentEulerAngles = tform.eulerAngles;
                    tform.rotation = Quaternion.Euler(currentEulerAngles.x, targetY, currentEulerAngles.z);
                }
            }

            isTurning = false;
        }
    }

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0) angle += 360f;
        return angle;
    }

    public override void Save() { }

    public void ResetTurnState()
    {
        isTurning = false;
        turnTimeElapsed = 0f;
        Debug.Log("[TurnBody] 状态重置");
    }

    /// <summary>
    /// 添加受控制的变换对象到列表中，并更新其朝向
    /// </summary>
    /// <param name="transform">要添加的变换对象</param>
    public void AddControlledTransform(Transform transform)
    {
        if (transform == null) return;
        
        // 添加到控制列表
        controlledTransforms.Add(transform);
        
        // 立即更新该对象的朝向以匹配当前方向
        UpdateTransformDirection(transform);
    }
    
    /// <summary>
    /// 更新指定变换对象的朝向以匹配当前方向
    /// </summary>
    /// <param name="transform">要更新的变换对象</param>
    private void UpdateTransformDirection(Transform transform)
    {
        if (transform == null) return;
        
        // 根据当前方向设置Y轴旋转
        float targetYRotation = (currentDirection == Vector2.right) ? 0f : 180f;
        Vector3 currentEulerAngles = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(currentEulerAngles.x, targetYRotation, currentEulerAngles.z);
    }
    
    /// <summary>
    /// 批量更新所有受控制对象的朝向
    /// </summary>
    public void UpdateAllTransformDirections()
    {
        foreach (var transform in controlledTransforms)
        {
            UpdateTransformDirection(transform);
        }
    }
}