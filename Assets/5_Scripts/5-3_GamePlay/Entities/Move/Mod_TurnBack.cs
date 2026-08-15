using Sirenix.OdinInspector;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class Mod_TurnBack : Module
{
    #region 数据和属性

    [Header("转向控制")]
    [SerializeField, Range(0.05f, 5f), Tooltip("转向所需时间（秒）")]
    private float Duration = 0.3f;

    [SerializeField, Tooltip("需要控制旋转的目标对象列表，默认自动获取子对象中含Animator的物体")]
    public List<Transform> controlledTransforms_Direction = new();
    [SerializeField, Tooltip("需要控制位置的目标对象列表")]
    public List<Transform> controlledTransforms_Position = new();

    [SerializeField, Tooltip("默认位置，用于计算目标位置(默认角色初始状态朝向右边)")]
    public Vector2 DefaultPosition = new Vector2(0.5f, 0f);

    [SerializeField, Tooltip("当前面向方向，默认右方")]
    public Vector2 currentDirection = Vector2.right;

    [SerializeField, ReadOnly, Tooltip("是否正在转身（由脚本自动控制）")]
    public bool isTurning = false;

    /// <summary>角色当前实际使用的左右转身角，供瞄准对象合成最终旋转。</summary>
    public float CurrentTurnAngleY { get; private set; }

    public UltEvent<Vector2> OnTrun = new UltEvent<Vector2>();

    private float turnTimeElapsed;
    private float startY;
    private float targetY;
    
    // 位置变换记录
    private Dictionary<Transform, Vector3> positionStartValues = new Dictionary<Transform, Vector3>();
    private Dictionary<Transform, Vector3> positionTargetValues = new Dictionary<Transform, Vector3>();
    
    // 位置转换完成标志
    private bool isPositionTransforming = false;

    public Mod_FocusPoint faceMouse;

    public Ex_ModData modData;
    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    #endregion

    #region 生命周期方法

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

        controlledTransforms_Direction.Clear();
        CollectTurnDirectionTransforms();
        targetY = currentDirection == Vector2.right ? 0f : 180f;
        CurrentTurnAngleY = targetY;
        UpdateAllTransformDirections();

        if (faceMouse == null)
            Debug.LogError("[TurnBody] 初始化失败：FaceMouse 模块未找到！" + item.name);
    }

    public override void ModUpdate(float deltaTime)
    {
        UpdateWork();
        UpdateTurn(deltaTime);
        UpdateTransform_Positions();
    }

    public override void Save() { }

    #endregion

    #region 转向逻辑

    private void UpdateWork()
    {
        if (faceMouse == null) return;

        Vector2 characterPos = transform.position;
        Vector2 mousePos = faceMouse.Data.See_Point;

        Vector2 directionToTarget = WorldTopologyRuntime.ShortestDelta(characterPos, mousePos);
        if (directionToTarget.sqrMagnitude < 0.001f) return;

        TurnBodyToDirection(directionToTarget);
    }

    public void TurnBodyToDirection(Vector2 targetDirection)
    {
        OnTrun.Invoke(targetDirection);

        if (Mathf.Abs(targetDirection.x) < 0.01f) return;

        float targetSign = Mathf.Sign(targetDirection.x);
        float facingSign = Mathf.Sign(currentDirection.x);
        // 转身过程中也要接收最新方向，否则目标从左侧切到右侧时会继续倒着追。
        if (facingSign == targetSign) return;

        currentDirection = (targetDirection.x > 0) ? Vector2.right : Vector2.left;

        isTurning = true;
        turnTimeElapsed = 0f;

        targetY = (currentDirection == Vector2.right) ? 0f : 180f;
        startY = controlledTransforms_Direction.Count > 0
            ? NormalizeAngle(controlledTransforms_Direction[0].eulerAngles.y)
            : CurrentTurnAngleY;
        CurrentTurnAngleY = startY;

        // 记录位置的起始值和目标值
        RecordPositionTransformValues();
        isPositionTransforming = true;  // 开始位置转换
    }

    public void UpdateTurn(float deltaTime)
    {
        if (!isTurning) return;

        turnTimeElapsed += deltaTime;
        float t = Mathf.Clamp01(turnTimeElapsed / Duration);
        float newY = Mathf.LerpAngle(startY, targetY, t);
        CurrentTurnAngleY = newY;

        foreach (var tform in controlledTransforms_Direction)
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
            foreach (var tform in controlledTransforms_Direction)
            {
                if (tform != null)
                {
                    // 只修改Y轴旋转，保持X和Z轴不变
                    Vector3 currentEulerAngles = tform.eulerAngles;
                    tform.rotation = Quaternion.Euler(currentEulerAngles.x, targetY, currentEulerAngles.z);
                }
            }

            CurrentTurnAngleY = targetY;
            isTurning = false;
        }
    }

    #endregion

    #region 受控对象管理

    private void CollectTurnDirectionTransforms()
    {
        if (item == null)
        {
            Debug.LogError("[TurnBody] 初始化失败：item 为空，无法收集 ITrunDirection 模块");
            return;
        }

        foreach (var mod in item.itemMods.Mods.Values)
        {
            // 同时负责瞄准的对象由 FocusPoint 一次性合成 Y/Z 旋转，避免双重写入。
            if (!(mod is ITrunDirection) || mod is IFocusPoint)
                continue;

            AddControlledTransform(mod.transform);
        }
    }

    /// <summary>
    /// 添加受控制的变换对象到列表中，并更新其朝向
    /// </summary>
    /// <param name="transform">要添加的变换对象</param>
    public void AddControlledTransform(Transform transform)
    {
        if (transform == null)
        {
            Debug.LogError("[TurnBody] 受控制的变换对象为空！");
            return;
        }

        if (controlledTransforms_Direction.Contains(transform))
            return;


        // 添加到控制列表
        controlledTransforms_Direction.Add(transform);

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

        // 根据当前转身插值设置Y轴旋转
        Vector3 currentEulerAngles = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(currentEulerAngles.x, CurrentTurnAngleY, currentEulerAngles.z);
    }
    /// <summary>
    /// 批量更新所有受控制对象的朝向
    /// </summary>
    public void UpdateAllTransformDirections()
    {
        foreach (var transform in controlledTransforms_Direction)
        {
            UpdateTransformDirection(transform);
        }
    }


    void UpdateTransform_Positions()
    {
        if (!isPositionTransforming)
        {
            // 位置转换完成后，直接设置最终位置
            foreach (var transform in controlledTransforms_Position)
            {
                if (transform == null) continue;

                Vector3 localPos = transform.localPosition;
                localPos.x = Mathf.Abs(localPos.x) * currentDirection.x;
                transform.localPosition = localPos;
            }
            return;
        }

        // 位置转换中，使用插值更新位置
        float t = Mathf.Clamp01(turnTimeElapsed / Duration);

        foreach (var transform in controlledTransforms_Position)
        {
            if (transform == null) continue;

            // 获取起始值和目标值
            if (positionStartValues.TryGetValue(transform, out Vector3 startPos) &&
                positionTargetValues.TryGetValue(transform, out Vector3 targetPos))
            {
                // 使用 Vector3.Lerp 平滑插值
                Vector3 lerpedPos = Vector3.Lerp(startPos, targetPos, t);
                transform.localPosition = lerpedPos;
            }
        }

        // 检查位置转换是否完成
        if (t >= 1f)
        {
            isPositionTransforming = false;
        }
    }

    /// <summary>
    /// 记录位置变换的起始值和目标值
    /// </summary>
    private void RecordPositionTransformValues()
    {
        positionStartValues.Clear();
        positionTargetValues.Clear();

        foreach (var transform in controlledTransforms_Position)
        {
            if (transform == null) continue;

            // 记录当前位置作为起始值
            Vector3 currentPos = transform.localPosition;
            positionStartValues[transform] = currentPos;

            // 计算目标位置：根据当前朝向和DefaultPosition镜像
            Vector3 targetPos = new Vector3(
                DefaultPosition.x * currentDirection.x,
                DefaultPosition.y,
                currentPos.z  // 保持Z轴不变
            );
            positionTargetValues[transform] = targetPos;
        }
    }

    #endregion

    #region 工具和辅助方法

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0) angle += 360f;
        return angle;
    }

    public void ResetTurnState()
    {
        isTurning = false;
        turnTimeElapsed = 0f;
        targetY = currentDirection == Vector2.right ? 0f : 180f;
        CurrentTurnAngleY = targetY;
        UpdateAllTransformDirections();
    }

    #endregion
}
