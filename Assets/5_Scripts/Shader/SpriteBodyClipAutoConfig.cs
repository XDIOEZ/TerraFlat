using UnityEngine;
using Sirenix.OdinInspector;

[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteBodyClipAutoConfig : MonoBehaviour
{
    public string bodyMinProp = "_BodyMinV";
    public string bodyMaxProp = "_BodyMaxV";

    SpriteRenderer sr;
    MaterialPropertyBlock mpb;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        ApplyBodyUVRange();
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
        Texture tex = sprite.texture;
        if (tex == null)
            return;

        // sprite 在贴图上的像素矩形
        Rect r = sprite.textureRect;

        // 转成 0~1 的 UV（以整张贴图为基准）
        float bodyMinV = r.yMin / tex.height;
        float bodyMaxV = r.yMax / tex.height;

#if UNITY_EDITOR
        // 编辑器下：直接写入 sharedMaterial，方便在 Inspector 里看到参数变化
        if (!Application.isPlaying)
        {
            var mat = sr.sharedMaterial;
            if (mat != null)
            {
                mat.SetFloat(bodyMinProp, bodyMinV);
                mat.SetFloat(bodyMaxProp, bodyMaxV);
            }
            return;
        }
#endif

        // 运行时：使用 MaterialPropertyBlock 做每个 Renderer 的覆盖，不污染共享材质
        if (mpb == null) mpb = new MaterialPropertyBlock();
        sr.GetPropertyBlock(mpb);
        mpb.SetFloat(bodyMinProp, bodyMinV);
        mpb.SetFloat(bodyMaxProp, bodyMaxV);
        sr.SetPropertyBlock(mpb);
    }
}