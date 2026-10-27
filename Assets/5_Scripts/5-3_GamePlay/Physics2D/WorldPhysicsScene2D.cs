using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>原生物理空间独立于世界身份，地表和太空物理岛共用创建及回收入口。</summary>
public sealed partial class WorldPhysicsScene2D : IDisposable
{
    #region 原生物理空间生命周期
    public Scene Scene { get; }
    public PhysicsScene2D PhysicsScene { get; }
    public bool IsValid => !disposed && Scene.IsValid() && Scene.isLoaded && PhysicsScene.IsValid();
    private readonly bool ownsScene;
    private bool disposed;

    private WorldPhysicsScene2D(Scene scene, bool ownsScene)
    {
        Scene = scene;
        PhysicsScene = scene.GetPhysicsScene2D();
        this.ownsScene = ownsScene;
    }

    public static Scene CreateIsolatedScene(string name)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 编辑态预览场景自带独立原生物理空间，快速规则无需切换 PlayMode。
            Scene preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            preview.name = name;
            return preview;
        }
#endif
        return SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics2D));
    }

    public static WorldPhysicsScene2D CreateIsolated(string name) => new(CreateIsolatedScene(name), true);

    public static WorldPhysicsScene2D Borrow(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new ArgumentException("物理空间必须绑定已加载的 Unity 场景。", nameof(scene));
        return new WorldPhysicsScene2D(scene, false);
    }

    partial void DisposeTransientQueries();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DisposeTransientQueries();
        if (!ownsScene || !Scene.IsValid() || !Scene.isLoaded) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(Scene))
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(Scene);
            else UnityEditor.SceneManagement.EditorSceneManager.CloseScene(Scene, true);
            return;
        }
#endif
        SceneManager.UnloadSceneAsync(Scene);
    }
    #endregion
}
