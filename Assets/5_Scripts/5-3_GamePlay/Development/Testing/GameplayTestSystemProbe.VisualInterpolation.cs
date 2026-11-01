using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 视觉插值规则采样

    private static JObject CaptureVisualMotionInterpolation(JObject args)
    {
        var history = new WorldVisualMotionHistory();
        history.Reset(Vector3.zero, 0d);
        history.Capture(Vector3.right, 0.05d);

        JArray CaptureDisplayFrames(int fps)
        {
            var samples = new JArray();
            for (int i = 0; i <= fps / 20; i++)
                samples.Add(history.Evaluate(0.05d + (double)i / fps).x);
            return samples;
        }

        JArray at60Fps = CaptureDisplayFrames(60);
        JArray at120Fps = CaptureDisplayFrames(120);
        JArray at240Fps = CaptureDisplayFrames(240);
        float frozen = history.Evaluate(0.075d).x;
        bool pausedUnchanged = history.Evaluate(0.075d).x == frozen;

        history.Capture(new Vector3(2f, 0f, 0f), 0.1d);
        float nextTickMid = history.Evaluate(0.125d).x;
        history.Capture(new Vector3(20f, 0f, 0f), 0.15d);
        float teleported = history.Evaluate(0.15d).x;
        history.Reset(new Vector3(-3f, 0f, 0f), 0.2d);
        float reused = history.Evaluate(0.225d).x;

        var wrapped = new WorldVisualMotionHistory();
        var domain = new WorldTopologyDomain(new int2(0, 0), new int2(100, 100), true);
        wrapped.Reset(new Vector3(99f, 5f, 0f), 1d);
        wrapped.CaptureWrapped(new Vector2(0f, 5f), domain, 1.05d);
        float seamMid = wrapped.Evaluate(1.075d).x;

        bool sourceUnchanged = false, projectionTranslated = false, projectionRestored = false;
        Scene scene = default;
        WorldPresentationProjection projection = null;
        try
        {
            scene = CreateGameplayTestProbeScene("[LuaVisualInterpolation]");
            var source = new GameObject("SourceVisual") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(source, scene);
            source.transform.position = new Vector3(7f, 3f, 0f);
            source.AddComponent<SpriteRenderer>();
            projection = WorldPresentationProjection.Create(source.transform);
            projection.Synchronize(Matrix4x4.identity, true);
            Vector3 original = projection.transform.position;
            projection.ApplyVisualMotionTranslation(new Vector3(-0.5f, 0.25f, 0f));
            sourceUnchanged = source.transform.position == new Vector3(7f, 3f, 0f);
            projectionTranslated = projection.transform.position == original + new Vector3(-0.5f, 0.25f, 0f);
            projection.Synchronize(Matrix4x4.identity, true);
            projectionRestored = projection.transform.position == original;
        }
        finally
        {
            projection?.Release();
            if (scene.IsValid()) CloseGameplayTestProbeScene(scene);
        }

        return new JObject
        {
            ["fps60"] = at60Fps,
            ["fps120"] = at120Fps,
            ["fps240"] = at240Fps,
            ["nextTickMid"] = nextTickMid,
            ["pausedUnchanged"] = pausedUnchanged,
            ["teleported"] = teleported,
            ["reused"] = reused,
            ["seamMid"] = seamMid,
            ["pixelAligned"] = WorldVisualMotionHistory.PixelAlignTranslation(new Vector3(0.12f, 0f), 32f).x,
            ["sourceUnchanged"] = sourceUnchanged,
            ["projectionTranslated"] = projectionTranslated,
            ["projectionRestored"] = projectionRestored
        };
    }

    #endregion
}
