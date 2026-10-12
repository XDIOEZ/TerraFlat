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
        // 星体位置和自转共用权威时钟，表现层只投影本步结果。
        Vector2 position = session.ProjectPosition(new FlatWorld.Spaceflight.SpaceSectorPosition(body.Sector, body.PositionMeters));
        transform.position = new Vector3(position.x, position.y, 0f);
        transform.localScale = renderScale;
        transform.rotation = Quaternion.Euler(0f, 0f, (float)(session.GetUniverse(body.Sector).GetRotationRadians(body.BodyId) * 180d / System.Math.PI));
        surfacePresentation?.UpdateLighting(session, body);
    }

    private void OnDestroy() => surfacePresentation?.Dispose();

    #endregion
}
