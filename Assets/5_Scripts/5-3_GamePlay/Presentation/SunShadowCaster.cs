using UnityEngine;

/// <summary>
/// 世界实体的可选太阳投影配置。无需给每种建筑编写脚本；普通实体自动参与，特殊 Prefab 可覆盖主体和高度。
/// 高度 1 使用 Sprite 可见高度，0 表示关闭；FootOffset 以世界单位校正落地点，不参与玩法或存档。
/// </summary>
[DisallowMultipleComponent]
public sealed class SunShadowCaster : MonoBehaviour
{
    #region 作者配置
    [Tooltip("只投射此主体；未指定时使用 Item.Sprite。")]
    public SpriteRenderer Source;
    [Tooltip("允许该物体投射太阳长阴影。")]
    public bool CastShadow = true;
    [Min(0f), Tooltip("视觉高度倍率，默认 1；设为 0 可关闭此物体的投影。")]
    public float HeightMultiplier = 1f;
    [Tooltip("相对自动脚底位置的世界 Y 偏移。")]
    public float FootOffset;
    #endregion
}
