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
    /// <summary>资源销毁前通知使用者释放注册和引用；只允许主线程订阅。</summary>
    internal static event Action Clearing;
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
            return mesh;
        }
        catch
        {
            DestroyMesh(mesh, false);
            throw;
        }
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
        Clearing?.Invoke();
        foreach (Mesh mesh in meshes.Values) DestroyMesh(mesh, immediate);
        meshes.Clear();
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
        Clear();
        Application.quitting -= Clear;
        Application.quitting += Clear;
    }

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

    /// <summary>在托管引用消失前同步销毁原生 Mesh。</summary>
    private static void BeforeAssemblyReload() => ClearMeshes(true);

    /// <summary>停止播放时同步释放，包括资源加载尚未完成的会话。</summary>
    private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) ClearMeshes(true);
    }
#endif

    #endregion
}
