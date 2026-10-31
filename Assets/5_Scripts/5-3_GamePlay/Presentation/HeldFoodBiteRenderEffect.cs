using UnityEngine;

/// <summary>通过统一 MPB 合成上传每口圆心与半径，GPU 直接裁剪食物原图。</summary>
public sealed class HeldFoodBiteRenderEffect : ActorRenderEffectModule
{
    #region 咬痕状态
    private static readonly int BitesId = Shader.PropertyToID("_HeldFoodBites");
    private static readonly int CirclesId = Shader.PropertyToID("_HeldFoodBiteCircles");
    private SpriteRenderer target;
    private Sprite sourceSprite;
    private ActorRenderEffectController controller;
    private FoodBiteGeometry geometry;
    private Texture2D circlesTexture;
    private int completedBites, maximumBites, randomSeed;
    public override bool RequiresContinuousRendering => false;
    public override bool RequiresSpriteBodyRange => target != null;

    public void SetBites(SpriteRenderer renderer, int completed, int maximum, int seed)
    {
        if (completed == 0) { ClearBites(); return; }
        if (target == renderer && sourceSprite == renderer.sprite && completedBites == completed &&
            maximumBites == maximum && randomSeed == seed) return;
        if (target != renderer) ClearBites();
        if (sourceSprite != renderer.sprite) geometry = null;
        target = renderer;
        sourceSprite = renderer.sprite;
        completedBites = completed;
        maximumBites = maximum;
        randomSeed = seed;
        RebuildBiteParameters();
        if (controller == null)
        {
            controller = GetComponent<ActorRenderEffectController>() ?? gameObject.AddComponent<ActorRenderEffectController>();
            controller.RefreshBindings();
        }
        // 延迟到正常渲染提交，快捷栏先保存不含咬痕的外部 Renderer 原始状态。
        MarkRenderStateDirty();
    }

    protected override void PrepareFrame(float deltaTime)
    {
        if (target != null && sourceSprite != target.sprite)
        {
            sourceSprite = target.sprite;
            geometry = null;
            RebuildBiteParameters();
            MarkRenderStateDirty();
        }
    }

    private void RebuildBiteParameters()
    {
        if (sourceSprite == null || completedBites >= maximumBites) { ReleaseParameterTexture(); return; }
        geometry ??= new FoodBiteGeometry(sourceSprite);
        Vector4[] circles = geometry.BuildBiteCircles(completedBites, maximumBites, randomSeed);
        if (circles.Length == 0) { ReleaseParameterTexture(); return; }
        if (circlesTexture == null || circlesTexture.width != circles.Length)
        {
            ReleaseParameterTexture();
            circlesTexture = new Texture2D(circles.Length, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "HeldFoodBiteCircles", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
        }
        circlesTexture.SetPixelData(circles, 0);
        circlesTexture.Apply(false, false);
    }

    protected override bool AppliesTo(Renderer renderer) => renderer == target;
    protected override bool AppliesToExternalRenderer() => false;

    protected override void ApplyEffect(Renderer renderer, MaterialPropertyBlock block, float deltaTime)
    {
        block.SetVector(BitesId, new Vector4(completedBites >= maximumBites ? -1 : circlesTexture != null ? circlesTexture.width : 0,
            circlesTexture != null ? 1f / circlesTexture.width : 0f, target.flipX ? -1f : 1f, target.flipY ? -1f : 1f));
        block.SetTexture(CirclesId, circlesTexture);
    }

    public void ClearBites()
    {
        if (target == null && circlesTexture == null) return;
        completedBites = 0;
        maximumBites = 1;
        // 先清除 MPB 的纹理引用再释放资源，其他效果通道仍由控制器合成。
        if (target != null)
        {
            Texture2D previous = circlesTexture;
            circlesTexture = null;
            MarkRenderStateDirty();
            controller?.RenderFrame(0f);
            circlesTexture = previous;
        }
        ReleaseParameterTexture();
        target = null;
        sourceSprite = null;
        geometry = null;
    }

    private void OnDestroy() => ClearBites();

    private void ReleaseParameterTexture()
    {
        if (circlesTexture == null) return;
        if (Application.isPlaying) Destroy(circlesTexture);
        else DestroyImmediate(circlesTexture);
        circlesTexture = null;
    }
    #endregion
}
