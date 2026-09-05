using UnityEngine;

/// <summary>
/// 将水面 Renderer 绑定到玩家选择的正式材质：风格化和写实材质各自保存参数及 Shader 变体。
/// 只替换共享材质，保留 Chunk 写入的水深纹理和世界坐标 MPB；区块池重新激活时读取最新偏好。
/// </summary>
[DisallowMultipleComponent]
public sealed class WaterVisualStyleBinding : MonoBehaviour
{
    #region 正式资源引用

    [SerializeField, Tooltip("同节点的水面渲染器")]
    private Renderer waterRenderer;
    [SerializeField, Tooltip("风格化水面材质")]
    private Material stylizedMaterial;
    [SerializeField, Tooltip("写实水面材质")]
    private Material realisticMaterial;

    #endregion

    #region 事件驱动的材质应用

    /// <summary>资源引用必须由正式 Prefab 提供，不在运行时搜索或创建替代材质。</summary>
    private void Awake()
    {
        if (waterRenderer == null || stylizedMaterial == null || realisticMaterial == null)
            throw new MissingReferenceException($"[{nameof(WaterVisualStyleBinding)}] 水体风格资源未配置：{name}");
    }

    /// <summary>显示或从区块池激活时使用当前玩家偏好。</summary>
    private void OnEnable()
    {
        WaterVisualSettings.Changed += ApplyStyle;
        ApplyStyle();
    }

    /// <summary>隐藏水层或归还区块池时解除订阅。</summary>
    private void OnDisable()
    {
        WaterVisualSettings.Changed -= ApplyStyle;
    }

    /// <summary>共享材质切换不清空渲染器的水深与岸线数据。</summary>
    private void ApplyStyle()
    {
        waterRenderer.sharedMaterial = WaterVisualSettings.Style == WaterVisualStyle.Stylized
            ? stylizedMaterial
            : realisticMaterial;
    }

    #endregion
}
