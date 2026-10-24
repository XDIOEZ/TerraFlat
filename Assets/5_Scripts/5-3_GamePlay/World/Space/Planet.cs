using Sirenix.OdinInspector;
using UnityEngine;

public class Planet : MonoBehaviour
{
    #region 星体表现数据
    [ShowInInspector]
    public PlanetData planetData; // 行星运行数据

    private Vector3 renderScale;
    private bool initializedPresentation;
    [SerializeField] private PlanetSurfaceStyle surfaceStyle = PlanetSurfaceStyle.Rocky;
    [SerializeField] private Color surfaceAccentColor = Color.gray;
    [SerializeField, Range(0f, 1f)] private float surfaceDetailStrength = 0.2f;
    [SerializeField, Min(1f)] private float surfaceDetailScale = 12f;
    private PlanetSurfacePresentation surfacePresentation;

    public void ApplyUniverse(FlatWorld.Spaceflight.SpaceSession session, FlatWorld.Spaceflight.BodyState body)
    {
        if (session == null || body == null) return;
        if (!initializedPresentation)
        {
            SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
            float diameter = sprite == null ? 1f : Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            renderScale = transform.localScale * ((float)(body.RadiusMeters * 2d) / Mathf.Max(0.001f, diameter));
            // 星球圆边与表面按屏幕像素绘制，避免把小圆贴图放大成模糊色块。
            surfacePresentation = PlanetSurfacePresentation.Create(sprite, surfaceStyle, surfaceAccentColor,
                surfaceDetailStrength, surfaceDetailScale);
            initializedPresentation = true;
        }
        // 赤道轴保持固定，自转只交给球面纹理，平台不会随整张画面绕圈。
        Vector2 position = session.ProjectPosition(new FlatWorld.Spaceflight.SpaceSectorPosition(body.Sector, body.PositionMeters));
        transform.position = new Vector3(position.x, position.y, 0f);
        transform.localScale = renderScale;
        transform.rotation = Quaternion.Euler(0f, 0f, (float)(body.EquatorTiltRadians * 180d / System.Math.PI));
        surfacePresentation?.UpdateLighting(session, body);
    }

    // 同一个星体外观供实际太空和远景观察复用，不在观察端另写颜色或类型规则。
    public void ApplySurfaceProperties(MaterialPropertyBlock properties)
    {
        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>(true);
        properties.SetColor("_Color", sprite != null ? sprite.color : Color.white);
        properties.SetColor("_SurfaceAccentColor", surfaceAccentColor);
        properties.SetFloat("_SurfaceStyle", (float)surfaceStyle);
        properties.SetFloat("_SurfaceDetailStrength", Mathf.Clamp01(surfaceDetailStrength));
        properties.SetFloat("_SurfaceDetailScale", Mathf.Max(1f, surfaceDetailScale));
        properties.SetFloat("_SurfaceExtent", surfaceStyle == PlanetSurfaceStyle.Stellar ? 1.3f : 1.12f);
    }

    private void OnDestroy() => surfacePresentation?.Dispose();

    #endregion
}
