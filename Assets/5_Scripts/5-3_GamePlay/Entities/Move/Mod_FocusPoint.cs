using MemoryPack;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[DefaultExecutionOrder(-50)]
public partial class Mod_FocusPoint : Module
{
    #region Fields
    public FocusPoint_Data Data = new FocusPoint_Data();
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }
    public Mod_GameController Mod_GameController;
    public Mod_TurnBack turnBody; // 添加TurnBody引用

    [Tooltip("需要跟随鼠标旋转的对象列表，列表为空时脚本不执行任何操作")]
    public List<Transform> targetRotationTransforms = new List<Transform>();
    
    private List<Transform> _cachedValidTargets;
    private readonly Dictionary<Transform, float> _currentAimAngles = new Dictionary<Transform, float>();
    private readonly Dictionary<Transform, float> _baseLocalXAngles = new Dictionary<Transform, float>();
    private readonly HashSet<Transform> _presentedRotationTargets = new HashSet<Transform>();
    private int _lastTargetCount = -1;
    private bool _needsRefresh = true;
    #endregion

    #region Unity Methods
    public override void Awake()
    {
        _Data.ID = ModText.FocusPoint;
    }

    public void OnValidate()
    {
        _Data.ID = ModText.FocusPoint;
    }

    private void LateUpdate()
    {
        if (!IsRuntimeLoaded || !Enabled || Mod_GameController == null) return;
        RefreshPresentedRotation(Time.deltaTime);
    }
    #endregion

    #region Module Methods
    protected override void OnLoad()
    {
        ModData.ReadData(ref Data);

        // 优先从物品所有者获取Controller
        Mod_GameController = item.Owner != null
            ? item.Owner.itemMods.GetMod_ByID(ModText.Controller).GetComponent<Mod_GameController>()
            : item.itemMods.GetMod_ByID(ModText.Controller).GetComponent<Mod_GameController>();

        // 获取TurnBody组件
        turnBody = item.Owner != null
            ? item.Owner.itemMods.GetMod_ByID(ModText.TrunBody) as Mod_TurnBack
            : item.itemMods.GetMod_ByID(ModText.TrunBody) as Mod_TurnBack;

        var focusPointComponents = item.GetComponentsInChildren<MonoBehaviour>(true);
        var targetSet = new HashSet<Transform>(targetRotationTransforms);

        for (int i = 0; i < focusPointComponents.Length; i++)
        {
            var component = focusPointComponents[i];
            if (component is not IFocusPoint)
                continue;

            if (targetSet.Add(component.transform))
            {
                targetRotationTransforms.Add(component.transform);
            }
        }

        _needsRefresh = true;
    }

    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override bool WantsRenderFrameUpdate => true;

    public override void ModRenderUpdate(float deltaTime)
    {
        // 优化：提前检查列表状态和旋转激活状态
        if (targetRotationTransforms == null || targetRotationTransforms.Count == 0 || !Data.ActivateRotation) 
            return;

        PlayerTakeItem_FaceMouse(deltaTime, includePresentedTargets: false);
    }

    protected override void OnSave()
    {
        ModData.WriteData(Data);
    }
    #endregion

    #region Public Methods
    public void PlayerTakeItem_FaceMouse(float deltaTime, bool includePresentedTargets = true)
    {
        if (Mod_GameController == null)
        {
            Debug.LogWarning("Mod_GameController 获取失败：FaceMouse 无法运行");
            return;
        }

        // 更新鼠标世界位置（供外部脚本调用）
        Vector3 pointerWorld = Mod_GameController.GetMouseWorldPosition();
        Data.See_Point = pointerWorld;
        Data.DefaultSkill_Point = pointerWorld;
        // 仅在启用旋转且列表有对象时执行逻辑
        if (Data.ActivateRotation)
        {
            FaceToMouse(Data.See_Point, deltaTime, includePresentedTargets ? (bool?)null : false);
        }
    }

    internal void RefreshPresentedRotation(float deltaTime)
    {
        // 身体在 -100 顺序完成手持位置更新，随后瞄准，最后由伤害模块采样命中盒。
        if (Data.ActivateRotation && _presentedRotationTargets.Count > 0)
            FaceToMouse(Data.See_Point, deltaTime, afterPresentation: true);
    }

    [ContextMenu("清理空对象")]
    public void ClearNullObjects()
    {
        if (targetRotationTransforms == null) return;
        
        int originalCount = targetRotationTransforms.Count;
        targetRotationTransforms.RemoveAll(trans => trans == null);
        
        int removedCount = originalCount - targetRotationTransforms.Count;
        if (removedCount > 0)
        {
            // 标记缓存需要刷新
            _needsRefresh = true;
            Debug.Log($"[FaceMouse] 已清理 {removedCount} 个空对象，当前列表大小：{targetRotationTransforms.Count}", this);
        }
    }

    /// <summary>注册需要随统一朝向旋转的对象。</summary>
    public void AddRotationTarget(Transform target, bool updateAfterPresentation = false)
    {
        if (target == null) return;
        if (updateAfterPresentation) _presentedRotationTargets.Add(target);
        else _presentedRotationTargets.Remove(target);
        if (targetRotationTransforms.Contains(target)) return;

        targetRotationTransforms.Add(target);
        _needsRefresh = true;
    }

    /// <summary>注销旋转对象并清理其平滑角度状态。</summary>
    public void RemoveRotationTarget(Transform target)
    {
        if (target == null)
            return;

        targetRotationTransforms.Remove(target);
        _currentAimAngles.Remove(target);
        _baseLocalXAngles.Remove(target);
        _presentedRotationTargets.Remove(target);
        _needsRefresh = true;
    }
    #endregion

    #region Private Methods
    private void FaceToMouse(Vector3 targetPosition, float deltaTime, bool? afterPresentation = null)
    {
        bool bodyRotationAim = turnBody != null && !turnBody.IsMirroringEnabled;
        List<Transform> targets = GetValidRotationTargets();
        // 普通瞄准整轮只取一次环绕拓扑，本体旋转不需要查询世界边界。
        WorldTopologyDomain topology = !bodyRotationAim && targets.Count > 0
            ? WorldTopologyRuntime.GetActiveDomain() : default;
        // 获取玩家当前朝向
        float playerFacingDirection = 1f; // 1表示朝右，-1表示朝左
        if (turnBody != null)
        {
            playerFacingDirection = turnBody.currentDirection.x;
        }

        // 遍历所有有效目标执行旋转
        foreach (var targetTrans in targets)
        {
            if (afterPresentation.HasValue && _presentedRotationTargets.Contains(targetTrans) != afterPresentation.Value)
                continue;
            Vector3 targetOrigin = targetTrans.position;
            Vector2 direction = ResolveAimDelta(targetOrigin, targetPosition, bodyRotationAim, topology);

            // 距离平方直接复用方向，避免另一次世界查询和开平方。
            if (IsWithinAimStopDistance(direction, Data.StopRotationDistance))
                continue;

            // 本体已经转动时，手持物只补父节点坐标中的瞄准角，避免重复叠加整个人的旋转。
            if (bodyRotationAim && targetTrans.parent != null)
                direction = targetTrans.parent.InverseTransformDirection(direction);
            
            // 根据玩家朝向调整方向
            if (playerFacingDirection < 0) // 玩家朝左
            {
                // 镜像X轴方向
                direction.x = -direction.x;
            }
            
            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            // 可选朝向组件缺失时不生成编辑器诊断分配。
            if (targetTrans.TryGetComponent(out HandAimOrientation aimOrientation))
            {
                targetAngle += aimOrientation.AngleOffsetDegrees;
            }

            // 独立保存瞄准角，避免从 Y=180° 的欧拉角反解时丢失左右翻转。
            if (!_currentAimAngles.TryGetValue(targetTrans, out float currentAngle))
            {
                currentAngle = targetTrans.localEulerAngles.z;
                _currentAimAngles[targetTrans] = currentAngle;
                _baseLocalXAngles[targetTrans] = targetTrans.localEulerAngles.x;
            }

            float smoothedAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, Data.RotationSpeed * deltaTime);
            _currentAimAngles[targetTrans] = smoothedAngle;

            // 由同一次写入合成人物转身 Y 轴和手持瞄准 Z 轴，防止两个模块互相覆盖。
            float facingAngleY = turnBody != null
                ? turnBody.CurrentTurnAngleY
                : (playerFacingDirection < 0f ? 180f : 0f);
            float baseLocalX = _baseLocalXAngles[targetTrans];
            targetTrans.localRotation = Quaternion.Euler(baseLocalX, facingAngleY, smoothedAngle);
        }
    }

    #region 瞄准方向计算

    internal static Vector2 ResolveAimDelta(Vector2 origin, Vector2 target,
        bool direct, in WorldTopologyDomain topology)
    {
        if (direct)
            return target - origin;
        float2 delta = topology.ShortestDelta(new float2(origin.x, origin.y),
            new float2(target.x, target.y));
        return new Vector2(delta.x, delta.y);
    }

    internal static bool IsWithinAimStopDistance(Vector2 delta, float distance)
        => distance >= 0f && delta.sqrMagnitude <= distance * distance;

    #endregion

    private List<Transform> GetValidRotationTargets()
    {
        // 检查是否需要刷新缓存
        bool needsRefresh = _needsRefresh || 
                           _cachedValidTargets == null || 
                           _lastTargetCount != targetRotationTransforms.Count;
        
        if (needsRefresh)
        {
            _cachedValidTargets = new List<Transform>(targetRotationTransforms.Count);
            
            for (int i = 0; i < targetRotationTransforms.Count; i++)
            {
                var trans = targetRotationTransforms[i];
                if (trans != null)
                {
                    _cachedValidTargets.Add(trans);
                }
            }
            
            _lastTargetCount = targetRotationTransforms.Count;
            _needsRefresh = false;

            var trackedTargets = new List<Transform>(_currentAimAngles.Keys);
            for (int i = 0; i < trackedTargets.Count; i++)
            {
                Transform trackedTarget = trackedTargets[i];
                if (trackedTarget != null && _cachedValidTargets.Contains(trackedTarget))
                    continue;

                _currentAimAngles.Remove(trackedTarget);
                _baseLocalXAngles.Remove(trackedTarget);
                _presentedRotationTargets.Remove(trackedTarget);
            }
            
            // 仅在调试模式下输出警告
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_cachedValidTargets.Count < targetRotationTransforms.Count)
            {
                Debug.LogWarning($"[FaceMouse] 旋转列表中存在 {(targetRotationTransforms.Count - _cachedValidTargets.Count)} 个空对象，建议使用ContextMenu清理", this);
            }
            #endif
        }
        
        return _cachedValidTargets;
    }
    #endregion

    #region Data Class
    [System.Serializable]
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class FocusPoint_Data
    {
        /// <summary>旋转速度（度/秒）</summary>
        [MemoryPackOrder(0)] public float RotationSpeed = 180f;

        [MemoryPackIgnore] public Dictionary<string, Vector2> FocusPoints = new Dictionary<string, Vector2>()
        {
              { "See", Vector2.zero },
              { "Move", Vector2.zero },
              { "Skill_0", Vector2.zero }
        };
        [MemoryPackInclude, MemoryPackOrder(1)] private Dictionary<string, ModuleVector2SaveSnapshot> FocusPointSnapshots
        {
            get => ModuleVector2SaveSnapshot.CaptureDictionary(FocusPoints);
            set => FocusPoints = ModuleVector2SaveSnapshot.RestoreDictionary(value);
        }

        /// <summary>是否启用旋转功能</summary>
        [MemoryPackOrder(2)] public bool ActivateRotation = true;

        /// <summary>停止旋转的距离阈值，鼠标接近到此距离内时不再旋转</summary>
        [Tooltip("鼠标与物体的距离小于此值时，停止旋转并保留当前角度")]
        [MemoryPackOrder(3)] public float StopRotationDistance = 0.1f; // 默认1.5单位距离

        [MemoryPackIgnore]
        public Vector2 See_Point { get=> FocusPoints["See"]; set => FocusPoints["See"] = value; }
        [MemoryPackIgnore] public Vector2 Move_Point { get=> FocusPoints["Move"]; set => FocusPoints["Move"] = value; }
        [MemoryPackIgnore] public Vector2 DefaultSkill_Point { get=> FocusPoints["Skill_0"]; set => FocusPoints["Skill_0"] = value; }
    }
    #endregion
}
