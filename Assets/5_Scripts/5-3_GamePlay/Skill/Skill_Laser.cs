using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Skill_Laser : Skill
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
    Mover mover;

    #endregion

    #region 生命周期方法

    // Start is called before the first frame update
    void Start()
    {
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
            runtimeSkill.skillSender.itemMods.GetMod_ByID(ModText.Mover, out mover); // 获取技能数据
            if (mover != null)
            {
                mover.Data.Speed.MultiplicativeModifier *= 0.25f;
            }
            else
            {
                Debug.LogWarning("激光技能：找不到Mover组件！");
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
    }

    // Update is called once per frame
    void Update()
    {
        // 检查runtimeSkill是否存在
        if (runtimeSkill == null)
        {
            Debug.LogError("激光技能：Update中runtimeSkill为空！");
            return;
        }

        // 更新所有模块
        foreach (var mod in mods)
        {
            if (mod != null)
            {
                mod.ModUpdate(Time.deltaTime);
            }
            else
            {
                Debug.LogWarning("激光技能：在Update中发现模块列表中的空模块！");
            }
        }

        // 更新激光线
        UpdateLaser();
    }

    #endregion

    #region 激光更新方法

    [Tooltip("更新激光线的显示")]
    private void UpdateLaser()
    {
        // 检查runtimeSkill是否存在
        if (runtimeSkill == null)
        {
            Debug.LogError("激光技能：UpdateLaser中runtimeSkill为空！");
            return;
        }

        // 检查skillManager是否存在
        if (runtimeSkill.skillManager == null)
        {
            Debug.LogError("激光技能：UpdateLaser中skillManager为空！");
            return;
        }

        // 检查聚焦点数据是否存在
        if (runtimeSkill.skillManager.focusPoint == null || runtimeSkill.skillManager.focusPoint.Data == null)
        {
            Debug.LogError("激光技能：focusPoint或其数据为空！");
            return;
        }

        // 检查施法点是否已缓存
        if (laserCastingPoint == null)
        {
            Debug.LogError("激光技能：UpdateLaser中laserCastingPoint为空！");
            return;
        }

        // 获取实时的目标点
        Vector2 currentTargetPoint = runtimeSkill.skillManager.focusPoint.Data.DefaultSkill_Point;
        Vector2 visualTargetPoint = laserCastingPoint != null
            ? WorldTopologyRuntime.NearestImagePosition(laserCastingPoint.position, currentTargetPoint)
            : currentTargetPoint;

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

        // 更新碰撞器的大小和旋转以匹配激光线
        if (laserCollider != null)
        {
            UpdateLaserCollider(visualTargetPoint);
        }

        // 更新进度
        runtimeSkill.progress += Time.deltaTime;
    }

    [Tooltip("更新激光碰撞器的大小和旋转")]
    private void UpdateLaserCollider(Vector2 targetPoint)
    {
        // 检查缓存的施法点是否存在
        if (laserCastingPoint == null)
        {
            Debug.LogError("激光技能：UpdateLaserCollider中laserCastingPoint为空！");
            return;
        }

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
        // 检查runtimeSkill是否存在
        if (runtimeSkill == null)
        {
            Debug.LogWarning("激光技能：OnDestroy中runtimeSkill为空！");
            return;
        }

        // 检查skillSender是否存在，如果存在则保存模块
        if (runtimeSkill.skillSender != null)
        {
            // 保存所有模块
            foreach (var mod in mods)
            {
                if (mod != null)
                {
                    mod.Save();
                }
            }

            // 销毁特效
            if (laserEffect != null)
            {
                Destroy(laserEffect.gameObject);
            }
        }
        else
        {
            // 如果skillSender不存在，直接清理特效
            if (laserEffect != null)
            {
                Destroy(laserEffect.gameObject);
            }
        }
    }

    public override void Save()
    {
        // 保存所有模块
        foreach (var mod in mods)
        {
            mod.Save();
        }

        // 恢复移动速度
        if (mover != null)
        {
            mover.Data.Speed.MultiplicativeModifier /= 0.25f;
            mover.Save();
        }
       
    }

    #endregion
}
