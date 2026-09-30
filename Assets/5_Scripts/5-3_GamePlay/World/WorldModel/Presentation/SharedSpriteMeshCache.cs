using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 主线程资源会话级 Sprite 网格缓存；每个 Unity Sprite 身份只持有一份隐藏 Mesh。
/// 预热与动态内容兜底共用入口，跨区块和世界复用；资源重载时先通知使用者解绑，再销毁网格。
/// 不持有 BRG 或 BatchMeshID，不允许调用方修改或销毁返回的共享 Mesh。
/// </summary>
public static class SharedSpriteMeshCache
{
    #region 共享网格

    // Unity Object 键使用 Unity 身份比较，并持有源 Sprite，避免按名称合并不同资源。
    private static readonly Dictionary<Sprite, Mesh> meshes = new();
    private static readonly Dictionary<Sprite, Geometry> geometries = new();
    private static readonly Dictionary<Sprite, SunShadowGeometry> sunShadowGeometries = new();
    internal const float SunShadowPaddingPixels = 5f; // 覆盖最大四像素模糊半径与半像素线性过滤边界。
    private const float SunShadowReferencePixelsPerUnit = 16f; // 阴影柔化按世界像素密度归一，避免高分辨率 Sprite 变成硬边。

    internal sealed class SunShadowGeometry
    {
        internal Mesh Mesh;
        internal Vector4 UvBounds;
        internal Vector3 UvToLocalY;
        internal float TexelScale;
    }

    /// <summary>行网格复用同一份原始几何，不能为每个实例重复读取 Sprite 的分配型数组属性。</summary>
    internal sealed class Geometry
    {
        internal readonly Vector3[] Vertices;
        internal readonly Vector2[] Uv;
        internal readonly int[] Triangles;
        internal readonly Bounds Bounds;
        internal readonly Vector4 UvRect;
        internal Geometry(Vector3[] vertices, Vector2[] uv, int[] triangles, Bounds bounds)
        {
            Vertices = vertices; Uv = uv; Triangles = triangles; Bounds = bounds;
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            foreach (Vector2 value in uv) { min = Vector2.Min(min, value); max = Vector2.Max(max, value); }
            UvRect = new Vector4(min.x, min.y, max.x, max.y);
        }
    }

    internal static Geometry GetGeometry(Sprite sprite)
    {
        Mesh mesh = GetOrCreate(sprite);
        if (!geometries.TryGetValue(sprite, out Geometry geometry))
            geometries.Add(sprite, geometry = new Geometry(mesh.vertices, mesh.uv, mesh.triangles, mesh.bounds));
        return geometry;
    }
    /// <summary>资源销毁前通知使用者释放注册和引用；只允许主线程订阅。</summary>
    internal static event Action Clearing;
    /// <summary>会话结束后禁止重新登记渲染资源，直到下一次运行初始化。</summary>
    private static bool sessionEnding;
    private static bool clearing;
    internal static bool IsSessionEnding => sessionEnding;
    /// <summary>当前会话已构造的唯一网格数。</summary>
    public static int Count => meshes.Count;

    /// <summary>命中时不读取 Sprite 数组；未预热的动态 Sprite 在此构造一次。</summary>
    public static Mesh GetOrCreate(Sprite sprite)
    {
        if (sprite == null) throw new ArgumentNullException(nameof(sprite));
        if (meshes.TryGetValue(sprite, out Mesh existing) && existing != null)
            return existing;

        Mesh mesh = CreateMesh(sprite);
        meshes[sprite] = mesh;
        return mesh;
    }

    /// <summary>批量预热；重复 Sprite 直接命中缓存，空资源忽略。</summary>
    public static void Prewarm(IEnumerable<Sprite> sprites)
    {
        if (sprites == null) throw new ArgumentNullException(nameof(sprites));
        foreach (Sprite sprite in sprites)
            if (sprite != null) GetOrCreate(sprite);
    }

    /// <summary>唯一 Sprite 几何读取入口；构造失败也释放已经创建的 Unity 对象。</summary>
    private static Mesh CreateMesh(Sprite sprite)
    {
        Vector2[] spriteVertices = sprite.vertices;
        Vector2[] spriteUv = sprite.uv;
        ushort[] spriteTriangles = sprite.triangles;
        var vertices = new Vector3[spriteVertices.Length];
        var triangles = new int[spriteTriangles.Length];
        for (int i = 0; i < spriteVertices.Length; i++) vertices[i] = spriteVertices[i];
        for (int i = 0; i < spriteTriangles.Length; i++) triangles[i] = spriteTriangles[i];

        var mesh = new Mesh { name = $"SharedSprite_{sprite.name}", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            mesh.vertices = vertices;
            mesh.uv = spriteUv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            geometries[sprite] = new Geometry(vertices, spriteUv, triangles, mesh.bounds);
            return mesh;
        }
        catch
        {
            DestroyMesh(mesh, false);
            throw;
        }
    }

    #endregion

    #region 太阳投影几何

    /// <summary>每张 Sprite 共享一个带透明外沿的四边形，不修改本体网格与贴图过滤。</summary>
    internal static SunShadowGeometry GetSunShadowGeometry(Sprite sprite)
    {
        if (sunShadowGeometries.TryGetValue(sprite, out SunShadowGeometry existing)) return existing;
        Geometry source = GetGeometry(sprite);
        Vector2 origin = default, uvX = default, uvY = default;
        bool mapped = false;
        for (int triangle = 0; triangle < source.Triangles.Length; triangle += 3)
        {
            int a = source.Triangles[triangle], b = source.Triangles[triangle + 1], c = source.Triangles[triangle + 2];
            Vector2 p = source.Vertices[a];
            Vector2 x = (Vector2)source.Vertices[b] - p, y = (Vector2)source.Vertices[c] - p;
            float determinant = x.x * y.y - x.y * y.x;
            if (Mathf.Abs(determinant) < 1e-12f) continue;
            Vector2 u = source.Uv[b] - source.Uv[a], v = source.Uv[c] - source.Uv[a];
            uvX = (u * y.y - v * x.y) / determinant;
            uvY = (v * x.x - u * y.x) / determinant;
            origin = source.Uv[a] - uvX * p.x - uvY * p.y;
            mapped = true;
            break;
        }
        if (!mapped) throw new InvalidOperationException($"太阳投影 Sprite 没有有效三角形：{sprite.name}");

        // 从真实顶点恢复 UV 映射，旋转或翻转打包的图集也沿用原图坐标。
        float uvDeterminant = uvX.x * uvY.y - uvX.y * uvY.x;
        if (Mathf.Abs(uvDeterminant) < 1e-20f)
            throw new InvalidOperationException($"太阳投影 Sprite 的 UV 无效：{sprite.name}");
        Vector2 uvToY = new Vector2(-uvX.y, uvX.x) / uvDeterminant;
        float texelScale = Mathf.Max(0.01f, sprite.pixelsPerUnit / SunShadowReferencePixelsPerUnit);
        Bounds bounds = source.Bounds;
        bounds.Expand(SunShadowPaddingPixels * texelScale * 2f / sprite.pixelsPerUnit);
        var vertices = new Vector3[4];
        var uv = new Vector2[4];
        for (int corner = 0; corner < 4; corner++)
        {
            bool right = corner == 1 || corner == 2, top = corner >= 2;
            Vector3 point = new Vector3(right ? bounds.max.x : bounds.min.x, top ? bounds.max.y : bounds.min.y, 0f);
            vertices[corner] = point;
            uv[corner] = origin + uvX * point.x + uvY * point.y;
        }
        var mesh = new Mesh { name = $"SunShadow_{sprite.name}", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            existing = new SunShadowGeometry { Mesh = mesh, UvBounds = source.UvRect,
                UvToLocalY = new Vector3(uvToY.x, uvToY.y, -Vector2.Dot(uvToY, origin)),
                TexelScale = texelScale };
            sunShadowGeometries.Add(sprite, existing);
            return existing;
        }
        catch { DestroyMesh(mesh, false); throw; }
    }

    /// <summary>CPU 剔除使用阴影外沿的真实范围，镜像和旋转与绘制变换一致。</summary>
    internal static Bounds GetSunShadowWorldBounds(Sprite sprite, Matrix4x4 localToWorld,
        bool flipX = false, bool flipY = false)
    {
        Bounds local = GetSunShadowGeometry(sprite).Mesh.bounds;
        Bounds world = default;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 point = new Vector3((corner & 1) == 0 ? local.min.x : local.max.x,
                corner < 2 ? local.min.y : local.max.y, 0f);
            if (flipX) point.x = -point.x;
            if (flipY) point.y = -point.y;
            point = localToWorld.MultiplyPoint3x4(point);
            if (corner == 0) world = new Bounds(point, Vector3.zero);
            else world.Encapsulate(point);
        }
        return world;
    }

    #endregion

    #region 会话与编辑器清理

    /// <summary>F5、失败、取消或 GameRes 销毁时释放整个会话；普通退出世界不调用。</summary>
    public static void Clear() => ClearMeshes(false);

    /// <summary>幂等释放入口；释放后允许新资源会话再次预热。</summary>
    public static void Dispose() => Clear();

    /// <summary>先释放使用者，确保 BRG 不再引用即将销毁的 Mesh 和 Texture。</summary>
    private static void ClearMeshes(bool immediate)
    {
        if (clearing) return;
        clearing = true;
        try
        {
            // 一个消费者退出失败不能阻断其它消费者和共享网格的回收。
            Action handlers = Clearing;
            if (handlers != null)
                foreach (Action release in handlers.GetInvocationList())
                {
                    try { release(); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            foreach (Mesh mesh in meshes.Values)
            {
                try { DestroyMesh(mesh, immediate); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            foreach (SunShadowGeometry geometry in sunShadowGeometries.Values)
            {
                try { DestroyMesh(geometry.Mesh, immediate); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
        finally
        {
            meshes.Clear();
            geometries.Clear();
            sunShadowGeometries.Clear();
            clearing = false;
        }
    }

    /// <summary>运行时延迟销毁，编辑器卸载前立即销毁自有隐藏对象。</summary>
    private static void DestroyMesh(Mesh mesh, bool immediate)
    {
        if (mesh == null) return;
        if (immediate || !Application.isPlaying) UnityEngine.Object.DestroyImmediate(mesh);
        else UnityEngine.Object.Destroy(mesh);
    }

    /// <summary>覆盖关闭 Domain Reload 的启动路径，并登记 Player 退出清理。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        EndSession(false);
        sessionEnding = false;
        Application.quitting -= OnApplicationQuitting;
        Application.quitting += OnApplicationQuitting;
    }

    /// <summary>退出先关闭重建入口，再按 BRG 到共享 Mesh 的顺序释放资源。</summary>
    private static void EndSession(bool immediate)
    {
        sessionEnding = true;
        ClearMeshes(immediate);
    }

    private static void OnApplicationQuitting() => EndSession(false);

#if UNITY_EDITOR
    /// <summary>脚本域重载和停止播放前清理，不能依赖 HideAndDontSave 被自动卸载。</summary>
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorCleanup()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    /// <summary>脚本热重载只释放原生资源；真正退出 Play Mode 才关闭资源会话。</summary>
    private static void BeforeAssemblyReload() => ClearMeshes(true);

    /// <summary>停止播放时同步释放，包括资源加载尚未完成的会话。</summary>
    private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) EndSession(true);
        else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode) sessionEnding = false;
    }
#endif

    #endregion
}
