using UnityEngine;
using Sirenix.OdinInspector;

[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteBodyClipAutoConfig : MonoBehaviour
{
    [Tooltip("淹没系数，越大越容易被水淹没（1=正常，2=双倍淹没深度）")]
    [Min(0f)]
    public float submergeScale = 1f;

    SpriteRenderer sr;
    Sprite _lastSprite;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        ApplyBodyUVRange();
    }

    void LateUpdate()
    {
        if (sr != null && sr.sprite != _lastSprite)
        {
            ApplyBodyUVRange();
        }
    }

    // 如果你在编辑器里换了 sprite，也可以在 OnValidate 里自动更新
    void OnValidate()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        ApplyBodyUVRange();
    }

    [Button("应用身体裁剪 UV 范围")]
    void ApplyBodyUVRange()
    {
        if (sr == null || sr.sprite == null)
            return;

        Sprite sprite = sr.sprite;
        _lastSprite = sprite;
        
        // 从 Pivot（即角色脚底，localY=0）开始算裁剪范围，
        // 忽略脚底下方的透明填充区域，确保 _BodyClip 能裁剪到可见像素
        float localMinY = 0f;
        float localMaxY = sprite.bounds.max.y;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var mat = sr.sharedMaterial;
            if (mat != null)
            {
                mat.SetFloat("_BodyMinV", localMinY);
                mat.SetFloat("_BodyMaxV", localMaxY);
            }
            return;
        }
#endif

        // 运行模式下，统一使用 MaterialPropertyBlock 传递参数，避免打断合批和其它脚本（如入水逻辑）的 MPB 冲突
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        sr.GetPropertyBlock(block);
        
        block.SetFloat("_BodyMinV", localMinY);
        block.SetFloat("_BodyMaxV", localMaxY);
        
        // 将 Tile_Water 写入的原始 _BodyClip 乘以淹没系数
        float rawClip = block.GetFloat("_BodyClip");
        block.SetFloat("_BodyClip", Mathf.Clamp01(rawClip * submergeScale));
        
        sr.SetPropertyBlock(block);
    }
}