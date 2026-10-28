using Sirenix.OdinInspector;
using FlatWorld.Spaceflight;
using UnityEngine;

public class Planet : MonoBehaviour
{
    #region 星体表现数据
    public const string CommonPrefabId = "通用星体";
    [System.NonSerialized, ShowInInspector]
    public PlanetData planetData; // 行星运行数据

    private Vector3 renderScale;
    private bool initializedPresentation;
    [SerializeField] private PlanetVisualConfiguration visualConfiguration;
    private PlanetSurfacePresentation surfacePresentation;
    public PlanetVisualConfiguration VisualConfiguration => visualConfiguration;

    // 公模只提供组件结构，外观由启动目录或生成星球的数据注入。
    public void ConfigurePlanetVisualTemplate(PlanetVisualConfiguration visual)
    {
        visualConfiguration = visual;
        GetComponentInChildren<SpriteRenderer>(true).color = new Color(visual.BaseColor[0], visual.BaseColor[1], visual.BaseColor[2]);
    }

    public void ApplyUniverse(FlatWorld.Spaceflight.SpaceSession session, FlatWorld.Spaceflight.BodyState body)
    {
        if (session == null || body == null) return;
        if (!initializedPresentation)
        {
            SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
            float diameter = sprite == null ? 1f : Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            renderScale = transform.localScale * ((float)(body.RadiusMeters * 2d) / Mathf.Max(0.001f, diameter));
            // 星球圆边与表面按屏幕像素绘制，避免把小圆贴图放大成模糊色块。
            surfacePresentation = PlanetSurfacePresentation.Create(sprite, visualConfiguration);
            initializedPresentation = true;
        }
        // 赤道轴保持固定，自转只交给球面纹理，平台不会随整张画面绕圈。
        Vector2 position = session.ProjectPosition(new FlatWorld.Spaceflight.SpaceSectorPosition(body.Sector, body.PositionMeters));
        transform.position = new Vector3(position.x, position.y, 0f);
        transform.localScale = renderScale;
        transform.rotation = Quaternion.Euler(0f, 0f, (float)(body.EquatorTiltRadians * 180d / System.Math.PI));
        surfacePresentation?.UpdateLighting(session, body);
    }

    public static void ApplySurfaceProperties(MaterialPropertyBlock properties, PlanetVisualConfiguration visual)
        => PlanetSurfacePresentation.ApplyVisualProperties(properties, visual);

    private void OnDestroy() => surfacePresentation?.Dispose();

    #endregion
}
