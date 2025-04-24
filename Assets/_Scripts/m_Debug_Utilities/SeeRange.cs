using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SeeRange : MonoBehaviour
{
    [Header("可见范围设置")]
    public float[] radii = new float[1]; // 初始化为一个默认值数组
    public Vector2 Pianyi = new Vector2(0, 0);

   [Header("Gizmos 颜色设置")]
    public Color gizmoColor = Color.red; // 暴露 Gizmos 颜色设置

    // 在场景视图中绘制可见圆
    private void OnDrawGizmos()
    {
        Vector2 center = transform.position;
         center += Pianyi;

        // 检查 radii 数组是否已初始化并具有元素
        if (radii != null && radii.Length > 0)
        {
            // 绘制每个圆
            for (int i = 0; i < radii.Length; i++)
            {
                Gizmos.color = gizmoColor; // 使用暴露的颜色
                Gizmos.DrawWireSphere(center, radii[i]); // 绘制圆的边界
            }
        }
        else
        {
            Debug.LogWarning("radii 数组未初始化或为空。");
        }
    }
}
