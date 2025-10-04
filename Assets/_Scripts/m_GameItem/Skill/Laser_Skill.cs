using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Laser_Skill : MonoBehaviour
{
    [Header("运行时数据")]
    public RuntimeSkill runtimeSkill;
    
    [Header("组件引用")]
    public LineRenderer lineRenderer;
    public Transform laserEffect;
    public BoxCollider2D laserCollider; // 2D激光碰撞器
    public List<Module> mods = new List<Module>();
    
    [Header("预制体引用")]
    public GameObject laserEffectPrefab;
    public Vector3 startPoint;
    
    // Start is called before the first frame update
    void Start()
    {
     
        if (runtimeSkill != null)
        {
            startPoint = runtimeSkill.skillManager.transform.position + (Vector3)runtimeSkill.skillManager.castingPointOffset["Laser"];      // 初始化激光线
            if (lineRenderer != null)
            {
                lineRenderer.SetPosition(0, startPoint);
                lineRenderer.SetPosition(1, new Vector3(runtimeSkill.targetPoint.x, runtimeSkill.targetPoint.y, runtimeSkill.skillSender.transform.position.z));
            }
            
            // 实例化并设置特效位置
            if (laserEffectPrefab != null)
            {
                laserEffect = Instantiate(laserEffectPrefab).transform;
                laserEffect.position = new Vector3(runtimeSkill.targetPoint.x, runtimeSkill.targetPoint.y, transform.position.z);
            }
            
            // 获取子对象上的BoxCollider2D组件
            laserCollider = GetComponentInChildren<BoxCollider2D>();
            
            // 获取所有子对象上的Module组件
            mods = new List<Module>(GetComponentsInChildren<Module>());
            
            // 加载所有模块
            foreach (var mod in mods)
            {
                mod.Load();
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        // 检查skillSender是否存在，如果不存在则销毁技能
        if (runtimeSkill == null || runtimeSkill.skillSender == null)
        {
            Destroy(gameObject);
            return;
        }
        
        // 更新所有模块
        foreach (var mod in mods)
        {
            mod.ModUpdate(Time.deltaTime);
        }
        
        // 将targetPoint的引用改为 runtimeSkill.skillManager.focusPoint.Data.DefaultSkill_Point
        if (runtimeSkill != null)
        {
            // 获取实时的目标点
            Vector2 currentTargetPoint = runtimeSkill.skillManager.focusPoint.Data.DefaultSkill_Point;
        startPoint = runtimeSkill.skillManager.transform.position + (Vector3)runtimeSkill.skillManager.castingPointOffset["Laser"];      // 初始化激光线

            // 更新激光线起点和终点
            if (lineRenderer != null)
            {
                lineRenderer.SetPosition(0, startPoint);
                lineRenderer.SetPosition(1, new Vector3(currentTargetPoint.x, currentTargetPoint.y, runtimeSkill.skillSender.transform.position.z));
            }
            
            // 更新特效位置
            if (laserEffect != null)
            {
                laserEffect.position = new Vector3(currentTargetPoint.x, currentTargetPoint.y, laserEffect.position.z);
            }
            
            // 更新碰撞器的大小和旋转以匹配激光线
            if (laserCollider != null)
            {
                UpdateLaserCollider(currentTargetPoint);
            }
            
            // 更新进度
            runtimeSkill.progress += Time.deltaTime;
        }
    }
    
    // 更新激光碰撞器的大小和旋转
    private void UpdateLaserCollider(Vector2 targetPoint)
    {
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
        // 检查skillSender是否存在，如果存在则保存模块
        if (runtimeSkill != null && runtimeSkill.skillSender != null)
        {
            if (laserEffect != null)
            {
                // 保存所有模块
                foreach (var mod in mods)
                {
                    mod.Save();
                }
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