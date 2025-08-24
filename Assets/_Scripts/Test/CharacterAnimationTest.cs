using UnityEngine;

public class CharacterAnimationTest : MonoBehaviour
{
    [Header("抖动参数")]
    public float angle = 10f;     // 最大旋转角度（左右摇的幅度）
    public float frequency = 2f;  // 摆动频率（每秒来回几次）

    private void Update()
    {
        // 正弦函数在 [-1,1] 之间变化
        float rotationZ = Mathf.Sin(Time.time * frequency) * angle;

        // 赋值给 localRotation（只绕 Z 轴摇摆，2D 角色一般在 XY 平面）
        transform.localRotation = Quaternion.Euler(0, 0, rotationZ);
    }
}
