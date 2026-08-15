using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_FocusPoint : Module
{
    #region Fields
    public FocusPoint_Data Data = new FocusPoint_Data();
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }
    public GameController GameController;
    public Mod_TurnBack turnBody; // 添加TurnBody引用

    [Tooltip("需要跟随鼠标旋转的对象列表，列表为空时脚本不执行任何操作")]
    public List<Transform> targetRotationTransforms = new List<Transform>();
    
    private List<Transform> _cachedValidTargets;
    private readonly Dictionary<Transform, float> _currentAimAngles = new Dictionary<Transform, float>();
    private readonly Dictionary<Transform, float> _baseLocalXAngles = new Dictionary<Transform, float>();
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
    #endregion

    #region Module Methods
    public override void Load()
    {
        ModData.ReadData(ref Data);

        // 优先从物品所有者获取Controller
        GameController = item.Owner != null
            ? item.Owner.itemMods.GetMod_ByID(ModText.Controller).GetComponent<GameController>()
            : item.itemMods.GetMod_ByID(ModText.Controller).GetComponent<GameController>();

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

    public override void ModUpdate(float deltaTime)
    {
        // 优化：提前检查列表状态和旋转激活状态
        if (targetRotationTransforms == null || targetRotationTransforms.Count == 0 || !Data.ActivateRotation) 
            return;

        PlayerTakeItem_FaceMouse(deltaTime);
    }

    public override void Save()
    {
        ModData.WriteData(Data);
    }
    #endregion

    #region Public Methods
    public void PlayerTakeItem_FaceMouse(float deltaTime)
    {
        if (GameController == null)
        {
            Debug.LogWarning("GameController 获取失败：FaceMouse 无法运行");
            return;
        }

        // 更新鼠标世界位置（供外部脚本调用）
        Data.See_Point = GameController.GetMouseWorldPosition();
        Data.DefaultSkill_Point = GameController.GetMouseWorldPosition();
        // 仅在启用旋转且列表有对象时执行逻辑
        if (Data.ActivateRotation)
        {
            FaceToMouse(Data.See_Point, deltaTime);
        }
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
    public void AddRotationTarget(Transform target)
    {
        if (target == null || targetRotationTransforms.Contains(target))
            return;

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
        _needsRefresh = true;
    }
    #endregion

    #region Private Methods
    private void FaceToMouse(Vector3 targetPosition, float deltaTime)
    {
        // 获取玩家当前朝向
        float playerFacingDirection = 1f; // 1表示朝右，-1表示朝左
        if (turnBody != null)
        {
            playerFacingDirection = turnBody.currentDirection.x;
        }

        // 遍历所有有效目标执行旋转
        foreach (var targetTrans in GetValidRotationTargets())
        {
            // 计算目标对象到鼠标位置的距离
            float distanceToMouse = WorldTopologyRuntime.Distance(targetTrans.position, targetPosition);

            // 如果距离小于阈值，则停止旋转，保留当前角度
            if (distanceToMouse <= Data.StopRotationDistance)
                continue;

            // 计算目标对象到鼠标位置的方向
            Vector2 direction = WorldTopologyRuntime.ShortestDelta(targetTrans.position, targetPosition);
            
            // 根据玩家朝向调整方向
            if (playerFacingDirection < 0) // 玩家朝左
            {
                // 镜像X轴方向
                direction.x = -direction.x;
            }
            
            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            HandAimOrientation aimOrientation = targetTrans.GetComponent<HandAimOrientation>();
            if (aimOrientation != null)
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
    [MemoryPackable]
    public partial class FocusPoint_Data
    {
        /// <summary>旋转速度（度/秒）</summary>
        public float RotationSpeed = 180f;

        public Dictionary<string, Vector2> FocusPoints = new Dictionary<string, Vector2>()
        {
              { "See", Vector2.zero },
              { "Move", Vector2.zero },
              { "Skill_0", Vector2.zero }
        };

        /// <summary>是否启用旋转功能</summary>
        public bool ActivateRotation = true;

        /// <summary>停止旋转的距离阈值，鼠标接近到此距离内时不再旋转</summary>
        [Tooltip("鼠标与物体的距离小于此值时，停止旋转并保留当前角度")]
        public float StopRotationDistance = 0.1f; // 默认1.5单位距离

        [MemoryPackIgnore]
        public Vector2 See_Point { get=> FocusPoints["See"]; set => FocusPoints["See"] = value; }
        public Vector2 Move_Point { get=> FocusPoints["Move"]; set => FocusPoints["Move"] = value; }
        public Vector2 DefaultSkill_Point { get=> FocusPoints["Skill_0"]; set => FocusPoints["Skill_0"] = value; }
    }
    #endregion
}
