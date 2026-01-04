using UltEvents;
using UnityEngine;

public abstract class BaseNoise : ScriptableObject
{
    [Tooltip("噪声采样频率")]
    public float frequency = 0.01f;

    [Tooltip("随机种子偏移")]
    public int seedOffset = 0;

    public UltEvent UpdateEvent = new UltEvent();

    public BiomeData biomeData;

    /// <summary>
    /// 在世界坐标采样噪声，返回 [0,1] 值
    /// </summary>
    public abstract float Sample(float x, float y, int seed);

    // 当SO参数在Inspector中变化时触发
    [ContextMenu("Force Update")]
    private void Update()
    {
         UpdateEvent.Invoke();
    }

    // 当SO被加载时触发
    protected virtual void OnEnable()
    {
        // 防止未初始化的情况
        if (UpdateEvent == null)
            UpdateEvent = new UltEvent();
    }
}
