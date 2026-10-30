using System.Collections.Generic;
using UnityEngine;

public class Skill_Laser : Skill, IGameplayFixedStepSimulationSystem
{
    #region 字段和属性

    [Header("组件引用")]
    public LineRenderer lineRenderer;
    public Transform laserEffect;
    public BoxCollider2D laserCollider; // 2D激光碰撞器
    public List<Module> mods = new List<Module>();
    
    [Header("预制体引用")]
    public GameObject laserEffectPrefab;
    
    [Tooltip("缓存的激光施法点Transform")]
    private Transform laserCastingPoint;
    
    [Tooltip("移动组件引用")]
    Mod_Mover mover;
    private const float MovementSpeedMultiplier = 0.25f;
    private bool _movementSpeedModified;
    private bool _initialized;
    private bool _isUnloaded;

    #endregion

    #region 生命周期方法

    private void OnEnable()
    {
        if (_initialized && !_isUnloaded)
            GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.PhysicsPreparation);
    }

    private void OnDisable() => GameplayFixedStepSimulation.Unregister(this);

    // Start is called before the first frame update
    void Start()
    {
        if (_isUnloaded) return;
        if (runtimeSkill == null)
        {
            Debug.LogError("激光技能：runtimeSkill为空！");
            return;
        }

        if (runtimeSkill.skillManager == null)
        {
            Debug.LogError("激光技能：skillManager为空！");
            return;
        }

        laserCastingPoint = GetCastingPointTransform();
        if (laserCastingPoint == null)
        {
            Debug.LogError("激光技能：施法点为空！");
            return;
        }

        // 获取移动组件
        if (runtimeSkill.skillSender != null)
        {
            runtimeSkill.skillSender.itemMods.GetMod_ByID(ModText.Mod_Mover, out mover); // 获取技能数据
            if (mover != null)
            {
                mover.Data.Speed.MultiplicativeModifier *= MovementSpeedMultiplier;
                _movementSpeedModified = true;
            }
            else
            {
                Debug.LogWarning("激光技能：找不到Mod_Mover组件！");
            }
        }
        else
        {
            Debug.LogWarning("激光技能：skillSender为空！");
        }

        // 获取LineRenderer组件
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.SetPosition(0, laserCastingPoint.position);
            lineRenderer.SetPosition(1, new Vector3(runtimeSkill.targetPoint.x, runtimeSkill.targetPoint.y, 0));
        }
        else
        {
            Debug.LogWarning("激光技能：找不到LineRenderer组件！");
        }

        // 实例化并设置特效位置
        if (laserEffectPrefab != null)
        {
            laserEffect = Instantiate(laserEffectPrefab).transform;
            laserEffect.position = new Vector3(runtimeSkill.targetPoint.x, runtimeSkill.targetPoint.y, transform.position.z);
        }
        else
        {
            Debug.LogWarning("激光技能：laserEffectPrefab未分配！");
        }

        // 获取子对象上的BoxCollider2D组件
        laserCollider = GetComponentInChildren<BoxCollider2D>();
        if (laserCollider == null)
        {
            Debug.LogWarning("激光技能：在子对象中找不到BoxCollider2D！");
        }

        // 获取所有子对象上的Module组件
        mods = new List<Module>(GetComponentsInChildren<Module>());
        Debug.Log($"激光技能：找到 {mods.Count} 个模块");

        // 加载所有模块
        foreach (var mod in mods)
        {
            if (mod != null)
            {
                mod.Load();
            }
            else
            {
                Debug.LogWarning("激光技能：在模块列表中发现空模块！");
            }
        }
        _initialized = true;
        GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.PhysicsPreparation);
    }

    public void SimulateFixedStep(float deltaTime)
    {
        if (_isUnloaded || runtimeSkill == null) return;
        using var worldScope = runtimeSkill.skillSender?.UseOwnedWorldForItemCallbacks();
        foreach (var mod in mods)
            if (mod != null) mod.ModUpdate(deltaTime);
        if (laserCollider != null && TryGetLaserTarget(out Vector2 target))
            UpdateLaserCollider(target);
    }

    private void Update() => UpdateLaserPresentation();

    #endregion

    #region 激光更新方法

    private bool TryGetLaserTarget(out Vector2 target)
    {
        target = default;
        if (_isUnloaded || laserCastingPoint == null || runtimeSkill?.skillManager?.focusPoint?.Data == null) return false;
        target = WorldTopologyRuntime.NearestImagePosition(laserCastingPoint.position,
            runtimeSkill.skillManager.focusPoint.Data.DefaultSkill_Point);
        return true;
    }

    [Tooltip("更新激光线的显示")]
    private void UpdateLaserPresentation()
    {
        if (_isUnloaded || runtimeSkill == null) return;
        using var worldScope = runtimeSkill.skillSender?.UseOwnedWorldForItemCallbacks();
        if (!TryGetLaserTarget(out Vector2 visualTargetPoint)) return;

        // 更新激光线起点和终点
        if (lineRenderer != null)
        {
            lineRenderer.SetPosition(0, laserCastingPoint.position);
            lineRenderer.SetPosition(1, new Vector3(visualTargetPoint.x, visualTargetPoint.y));
        }

        // 更新特效位置
        if (laserEffect != null)
        {
            laserEffect.position = new Vector3(visualTargetPoint.x, visualTargetPoint.y, 0);
        }
    }

    [Tooltip("更新激光碰撞器的大小和旋转")]
    private void UpdateLaserCollider(Vector2 targetPoint)
    {
        Vector2 startPoint = laserCastingPoint.position; // 使用缓存的施法点Transform
        Vector2 endPoint = targetPoint;

        // 计算激光线的中心点
        Vector2 center = (startPoint + endPoint) * 0.5f;

        // 计算激光线的长度
        float length = (endPoint - startPoint).magnitude;

        // 设置碰撞器的位置到中心点
        laserCollider.transform.position = new Vector3(center.x, center.y, laserCollider.transform.position.z);

        // 设置碰撞器的大小（假设激光线有一定宽度）
        float lineWidth = lineRenderer != null ? lineRenderer.startWidth : 0.1f;
        laserCollider.size = new Vector2(length, lineWidth);

        // 计算并设置碰撞器的旋转
        Vector2 direction = endPoint - startPoint;
        if (direction != Vector2.zero)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            laserCollider.transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    #endregion

    #region 销毁清理方法

    [Tooltip("销毁时清理资源")]
    private void OnDestroy()
    {
        Unload();
    }

    public override void Save()
    {
        foreach (var mod in mods)
        {
            if (mod != null)
                mod.Save();
        }

        mover?.Save();
    }

    public override void Unload()
    {
        GameplayFixedStepSimulation.Unregister(this);
        if (_isUnloaded)
            return;

        _isUnloaded = true;
        foreach (var mod in mods)
        {
            if (mod != null)
                mod.Unload();
        }

        if (_movementSpeedModified && mover != null)
        {
            mover.Data.Speed.MultiplicativeModifier /= MovementSpeedMultiplier;
            _movementSpeedModified = false;
        }

        if (laserEffect != null)
        {
            Destroy(laserEffect.gameObject);
            laserEffect = null;
        }
    }

    #endregion
}
