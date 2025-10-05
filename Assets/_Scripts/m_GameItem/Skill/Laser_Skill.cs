using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Laser_Skill : Skill
{
    [Header("组件引用")]
    public LineRenderer lineRenderer;
    public Transform laserEffect;
    public BoxCollider2D laserCollider; // 2D激光碰撞器
    public List<Module> mods = new List<Module>();
    
    [Header("预制体引用")]
    public GameObject laserEffectPrefab;
    public Vector3 startPoint;
    Mover mover;

    // Start is called before the first frame update
    void Start()
    {
        if (runtimeSkill == null)
        {
            Debug.LogError("Laser_Skill: runtimeSkill is null!");
            return;
        }

        if (runtimeSkill.skillManager == null)
        {
            Debug.LogError("Laser_Skill: skillManager is null!");
            return;
        }

        // 获取施法点
        if (runtimeSkill.skillManager.castingPoint == null || !runtimeSkill.skillManager.castingPoint.ContainsKey("Laser"))
        {
            Debug.LogError("Laser_Skill: castingPoint dictionary is null or doesn't contain 'Laser' key!");
            return;
        }

        startPoint = runtimeSkill.skillManager.castingPoint["Laser"].position; // 初始化激光线

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
                Debug.LogWarning("Laser_Skill: Could not find Mover component!");
            }
        }
        else
        {
            Debug.LogWarning("Laser_Skill: skillSender is null!");
        }

        // 获取LineRenderer组件
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.SetPosition(0, startPoint);
            lineRenderer.SetPosition(1, new Vector3(runtimeSkill.targetPoint.x, runtimeSkill.targetPoint.y, 0));
        }
        else
        {
            Debug.LogWarning("Laser_Skill: LineRenderer component not found!");
        }

        // 实例化并设置特效位置
        if (laserEffectPrefab != null)
        {
            laserEffect = Instantiate(laserEffectPrefab).transform;
            laserEffect.position = new Vector3(runtimeSkill.targetPoint.x, runtimeSkill.targetPoint.y, transform.position.z);
        }
        else
        {
            Debug.LogWarning("Laser_Skill: laserEffectPrefab is not assigned!");
        }

        // 获取子对象上的BoxCollider2D组件
        laserCollider = GetComponentInChildren<BoxCollider2D>();
        if (laserCollider == null)
        {
            Debug.LogWarning("Laser_Skill: BoxCollider2D not found in children!");
        }

        // 获取所有子对象上的Module组件
        mods = new List<Module>(GetComponentsInChildren<Module>());
        Debug.Log($"Laser_Skill: Found {mods.Count} modules");

        // 加载所有模块
        foreach (var mod in mods)
        {
            if (mod != null)
            {
                mod.Load();
            }
            else
            {
                Debug.LogWarning("Laser_Skill: Found null module in mods list!");
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        // 检查runtimeSkill是否存在
        if (runtimeSkill == null)
        {
            Debug.LogError("Laser_Skill: runtimeSkill is null in Update!");
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
                Debug.LogWarning("Laser_Skill: Found null module in mods list during Update!");
            }
        }

        // 更新激光线
        UpdateLaser();
    }

    private void UpdateLaser()
    {
        // 检查runtimeSkill是否存在
        if (runtimeSkill == null)
        {
            Debug.LogError("Laser_Skill: runtimeSkill is null in UpdateLaser!");
            return;
        }

        // 检查skillManager是否存在
        if (runtimeSkill.skillManager == null)
        {
            Debug.LogError("Laser_Skill: skillManager is null in UpdateLaser!");
            return;
        }

        // 检查聚焦点数据是否存在
        if (runtimeSkill.skillManager.focusPoint == null || runtimeSkill.skillManager.focusPoint.Data == null)
        {
            Debug.LogError("Laser_Skill: focusPoint or its data is null!");
            return;
        }

        // 获取施法点
        if (runtimeSkill.skillManager.castingPoint == null || !runtimeSkill.skillManager.castingPoint.ContainsKey("Laser"))
        {
            Debug.LogError("Laser_Skill: castingPoint dictionary is null or doesn't contain 'Laser' key in UpdateLaser!");
            return;
        }

        // 获取实时的目标点
        Vector2 currentTargetPoint = runtimeSkill.skillManager.focusPoint.Data.DefaultSkill_Point;
        startPoint = runtimeSkill.skillManager.castingPoint["Laser"].position; // 初始化激光线

        // 更新激光线起点和终点
        if (lineRenderer != null)
        {
            lineRenderer.SetPosition(0, startPoint);
            lineRenderer.SetPosition(1, new Vector3(currentTargetPoint.x, currentTargetPoint.y));
        }

        // 更新特效位置
        if (laserEffect != null)
        {
            laserEffect.position = new Vector3(currentTargetPoint.x, currentTargetPoint.y, 0);
        }

        // 更新碰撞器的大小和旋转以匹配激光线
        if (laserCollider != null)
        {
            UpdateLaserCollider(currentTargetPoint);
        }

        // 更新进度
        runtimeSkill.progress += Time.deltaTime;
    }

    // 更新激光碰撞器的大小和旋转
    private void UpdateLaserCollider(Vector2 targetPoint)
    {
        // 检查runtimeSkill和skillSender是否存在
        if (runtimeSkill == null || runtimeSkill.skillSender == null)
        {
            Debug.LogError("Laser_Skill: runtimeSkill or skillSender is null in UpdateLaserCollider!");
            return;
        }

        Vector2 startPoint = runtimeSkill.skillSender.transform.position;
        Vector2 endPoint = targetPoint;

        // 计算激光线的中心点
        Vector2 center = (startPoint + endPoint) * 0.5f;

        // 计算激光线的长度
        float length = Vector2.Distance(startPoint, endPoint);

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

    // 销毁时清理
    private void OnDestroy()
    {
        // 恢复移动速度
        if (mover != null)
        {
            mover.Data.Speed.MultiplicativeModifier /= 0.25f;
        }

        // 检查runtimeSkill是否存在
        if (runtimeSkill == null)
        {
            Debug.LogWarning("Laser_Skill: runtimeSkill is null in OnDestroy!");
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
}