using UnityEngine;
using UnityEngine.Rendering;

/// <summary>为通用投射物提供纯表现层飞行拖尾，不参与命中、速度或飞行结算。</summary>
[DisallowMultipleComponent]
public sealed class ProjectileFlightTrailPresenter : MonoBehaviour
{
    #region 配置

    [Min(0.01f), Tooltip("拖尾保留时间；数值越大，飞得越快时尾巴越长。")]
    public float TrailTime = 0.08f;

    [Min(0f), Tooltip("拖尾靠近投射物一端的宽度。")]
    public float StartWidth = 0.065f;

    [Min(0f), Tooltip("拖尾末端宽度。")]
    public float EndWidth = 0.008f;

    [Min(0.001f), Tooltip("相邻拖尾顶点的最小距离，避免高速飞行生成过密网格。")]
    public float MinVertexDistance = 0.035f;

    [Tooltip("拖尾起始颜色；末端会自动淡到透明。")]
    public Color TrailColor = new Color(1f, 0.93f, 0.72f, 0.62f);

    [Range(0f, 1f), Tooltip("直线投射物把拖尾锚点向素材尾端偏移的比例；抛物线旋转物仍从中心出发。")]
    public float TailAnchorRatio = 0.82f;

    #endregion

    #region 运行时状态

    private const string RuntimeTrailObjectName = "ProjectileFlightTrail";
    private const float MinimumFlightSpeedSqr = 0.01f;

    private static Material sharedTrailMaterial;
    private static bool warnedMissingTrailShader;

    private Mod_Projectile projectile;
    private Item ownerItem;
    private Rigidbody2D ownerBody;
    private SpriteRenderer sourceRenderer;
    private GameObject trailObject;
    private TrailRenderer trailRenderer;
    private bool wasFlying;

    #endregion

    #region 生命周期

    private void Awake()
    {
        projectile = GetComponent<Mod_Projectile>();
    }

    /// <summary>在物理与模块更新之后读取最终位置，让拖尾紧跟实际投射表现。</summary>
    private void LateUpdate()
    {
        ResolveOwner();
        bool flying = IsProjectileFlying();
        if (flying)
        {
            EnsureTrail();
            if (trailRenderer != null)
            {
                SyncTrailSorting();
                if (!wasFlying)
                    BeginTrail();
            }
        }
        else if (wasFlying)
        {
            StopTrail(false);
        }

        wasFlying = flying;
    }

    private void OnDisable()
    {
        StopTrail(true);
        wasFlying = false;
    }

    private void OnDestroy()
    {
        DestroyRuntimeTrail();
    }

    #endregion

    #region 飞行判定

    private void ResolveOwner()
    {
        projectile ??= GetComponent<Mod_Projectile>();
        Item resolved = projectile != null ? projectile.item : null;
        if (ReferenceEquals(ownerItem, resolved))
            return;

        DestroyRuntimeTrail();
        ownerItem = resolved;
        ownerBody = ownerItem != null ? ownerItem.GetComponent<Rigidbody2D>() : null;
        sourceRenderer = ownerItem != null
            ? ownerItem.Sprite ?? ownerItem.GetComponentInChildren<SpriteRenderer>(true)
            : null;
        wasFlying = false;
    }

    private bool IsProjectileFlying()
    {
        if (projectile == null || ownerItem == null || !projectile.HasActiveWorldAttachment)
            return false;

        ownerBody ??= ownerItem.GetComponent<Rigidbody2D>();
        return ownerBody != null &&
               ownerBody.bodyType == RigidbodyType2D.Dynamic &&
               ownerBody.velocity.sqrMagnitude > MinimumFlightSpeedSqr &&
               ownerItem.itemData?.Stack != null &&
               !ownerItem.itemData.Stack.CanBePickedUp;
    }

    #endregion

    #region 拖尾表现

    private void EnsureTrail()
    {
        if (trailRenderer != null || sourceRenderer == null)
            return;

        Material material = ResolveSharedTrailMaterial();
        if (material == null)
            return;

        trailObject = new GameObject(RuntimeTrailObjectName)
        {
            hideFlags = HideFlags.DontSave,
            layer = sourceRenderer.gameObject.layer
        };
        trailObject.transform.SetParent(sourceRenderer.transform, false);
        trailObject.transform.localPosition = ResolveTrailAnchorLocalPosition();
        trailObject.transform.localRotation = Quaternion.identity;
        trailObject.transform.localScale = Vector3.one;

        trailRenderer = trailObject.AddComponent<TrailRenderer>();
        trailRenderer.hideFlags = HideFlags.DontSave;
        trailRenderer.sharedMaterial = material;
        trailRenderer.time = Mathf.Max(0.01f, TrailTime);
        trailRenderer.startWidth = Mathf.Max(0f, StartWidth);
        trailRenderer.endWidth = Mathf.Max(0f, EndWidth);
        trailRenderer.minVertexDistance = Mathf.Max(0.001f, MinVertexDistance);
        trailRenderer.numCornerVertices = 2;
        trailRenderer.numCapVertices = 2;
        trailRenderer.alignment = LineAlignment.View;
        trailRenderer.textureMode = LineTextureMode.Stretch;
        trailRenderer.shadowCastingMode = ShadowCastingMode.Off;
        trailRenderer.receiveShadows = false;
        trailRenderer.lightProbeUsage = LightProbeUsage.Off;
        trailRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        trailRenderer.colorGradient = BuildGradient();
        trailRenderer.emitting = false;
        trailRenderer.Clear();
        SyncTrailSorting();
    }

    private void BeginTrail()
    {
        if (trailRenderer == null)
            return;

        trailObject.transform.localPosition = ResolveTrailAnchorLocalPosition();
        trailRenderer.time = Mathf.Max(0.01f, TrailTime);
        trailRenderer.startWidth = Mathf.Max(0f, StartWidth);
        trailRenderer.endWidth = Mathf.Max(0f, EndWidth);
        trailRenderer.minVertexDistance = Mathf.Max(0.001f, MinVertexDistance);
        trailRenderer.colorGradient = BuildGradient();
        trailRenderer.Clear();
        trailRenderer.emitting = true;
    }

    private void StopTrail(bool clear)
    {
        if (trailRenderer == null)
            return;

        trailRenderer.emitting = false;
        if (clear)
            trailRenderer.Clear();
    }

    /// <summary>直线箭矢从素材尾部拉出拖尾，旋转抛射物保持中心锚点避免尾巴绕圈。</summary>
    private Vector3 ResolveTrailAnchorLocalPosition()
    {
        if (sourceRenderer?.sprite == null || projectile == null || projectile.UseVisibleArc)
            return Vector3.zero;

        Bounds bounds = sourceRenderer.sprite.bounds;
        float radians = projectile.SpriteForwardAngleDegrees * Mathf.Deg2Rad;
        Vector2 forward = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        float projectedExtent =
            Mathf.Abs(forward.x) * bounds.extents.x +
            Mathf.Abs(forward.y) * bounds.extents.y;
        Vector2 anchor = (Vector2)bounds.center -
                         forward * projectedExtent * Mathf.Clamp01(TailAnchorRatio);
        return anchor;
    }

    private void SyncTrailSorting()
    {
        if (trailRenderer == null || sourceRenderer == null)
            return;

        trailObject.layer = sourceRenderer.gameObject.layer;
        trailRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        trailRenderer.sortingOrder = sourceRenderer.sortingOrder - 1;
    }

    private Gradient BuildGradient()
    {
        Color rgb = new Color(TrailColor.r, TrailColor.g, TrailColor.b, 1f);
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(rgb, 0f),
                new GradientColorKey(rgb, 1f)
            },
            new[]
            {
                new GradientAlphaKey(Mathf.Clamp01(TrailColor.a), 0f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    private static Material ResolveSharedTrailMaterial()
    {
        if (sharedTrailMaterial != null)
            return sharedTrailMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                        Shader.Find("Sprites/Default");
        if (shader == null)
        {
            if (!warnedMissingTrailShader)
            {
                warnedMissingTrailShader = true;
                Debug.LogWarning("[ProjectileFlightTrailPresenter] 缺少可用的无光照 Sprite Shader，投射物拖尾已关闭。");
            }
            return null;
        }

        sharedTrailMaterial = new Material(shader)
        {
            name = "Projectile Flight Trail (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedTrailMaterial;
    }

    private void DestroyRuntimeTrail()
    {
        if (trailObject != null)
            Destroy(trailObject);

        trailObject = null;
        trailRenderer = null;
    }

    #endregion
}
