using UnityEngine;
using UnityEngine.UI;

/// <summary>两块燧石按真实敲击运动，火花与成功余烬只负责呈现。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FlintStrikeToolGraphic : MaskableGraphic
{
    #region 美术与敲击状态

    [SerializeField] private Texture2D artworkAtlas;
    private const float StrikeDuration = .65f;
    private float strikeElapsed = StrikeDuration;
    public Texture2D ArtworkAtlas => artworkAtlas;
    public int StrikeRevision { get; private set; }
    public bool LastStrikeSucceeded { get; private set; }
    public override Texture mainTexture => artworkAtlas;

    public void ConfigureArtwork(Texture2D atlas)
    {
        artworkAtlas = atlas;
        SetAllDirty();
    }

    public void PlayStrike(bool succeeded)
    {
        LastStrikeSucceeded = succeeded;
        StrikeRevision++;
        strikeElapsed = StrikeDuration * .25f;
        SetVerticesDirty();
    }

    private void Update()
    {
        if (strikeElapsed >= StrikeDuration) return;
        strikeElapsed = Mathf.Min(StrikeDuration, strikeElapsed + Time.unscaledDeltaTime);
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        strikeElapsed = StrikeDuration;
        base.OnDisable();
    }

    #endregion

    #region 石片与火花绘制

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (artworkAtlas == null) return;
        Rect bounds = rectTransform.rect;
        float scale = Mathf.Min(bounds.width / 600f, bounds.height / 300f);
        Vector2 center = bounds.center;
        float phase = strikeElapsed / StrikeDuration;
        float contact = phase < .25f ? phase / .25f : Mathf.Max(0f, 1f - (phase - .25f) / .6f);
        // 图集左右各一块石头，独立位移与旋转后在火绒上方交击。
        AddQuad(mesh, center + new Vector2(-116f + 46f * contact, 4f + 14f * contact) * scale,
            Vector2.one * (260f * scale), -8f * contact, new Rect(0f, 0f, .5f, 1f), color);
        AddQuad(mesh, center + new Vector2(116f - 54f * contact, 4f - 4f * contact) * scale,
            Vector2.one * (260f * scale), 5f * contact, new Rect(.5f, 0f, .5f, 1f), color);
        if (phase < .25f || phase >= 1f) return;
        float flight = (phase - .25f) / .75f;
        Vector2 origin = center + new Vector2(0f, -76f) * scale;
        Color sparkColor = new Color(1f, .72f, .25f, (1f - flight) * color.a);
        Rect sparkUv = new Rect(.2f, .6f, 0f, 0f);
        for (int index = 0; index < 9; index++)
        {
            float angle = (index * 23f + 185f) * Mathf.Deg2Rad;
            Vector2 velocity = new Vector2(Mathf.Cos(angle) * 110f, Mathf.Sin(angle) * 45f);
            Vector2 position = origin + (velocity * flight + Vector2.down * (25f * flight * flight)) * scale;
            AddQuad(mesh, position, Vector2.one * ((index % 3 + 2f) * scale), 0f, sparkUv, sparkColor);
        }
        if (LastStrikeSucceeded)
        {
            Color ember = new Color(1f, .42f, .12f, (1f - flight) * color.a);
            AddQuad(mesh, origin + Vector2.down * (40f * scale),
                Vector2.one * ((12f + 6f * Mathf.Sin(flight * Mathf.PI)) * scale), 45f, sparkUv, ember);
        }
    }

    private static void AddQuad(VertexHelper mesh, Vector2 center, Vector2 size, float angle, Rect uv, Color tint)
    {
        int first = mesh.currentVertCount;
        float radians = angle * Mathf.Deg2Rad;
        Vector2 right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (size.x * .5f);
        Vector2 up = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * (size.y * .5f);
        mesh.AddVert(center - right - up, tint, new Vector2(uv.xMin, uv.yMin));
        mesh.AddVert(center - right + up, tint, new Vector2(uv.xMin, uv.yMax));
        mesh.AddVert(center + right + up, tint, new Vector2(uv.xMax, uv.yMax));
        mesh.AddVert(center + right - up, tint, new Vector2(uv.xMax, uv.yMin));
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first + 2, first + 3, first);
    }

    #endregion
}
