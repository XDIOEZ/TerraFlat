using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region Shader 降雪资源与覆盖采样

    // 只读取正式预制体和覆盖计算，不实例化雪效或干扰当前世界。
    private static JObject WorldShaderSnow(JObject args)
    {
        GameObject prefab = Resources.Load<GameObject>("Weather/SnowEffect");
        MeshRenderer renderer = prefab != null ? prefab.GetComponent<MeshRenderer>() : null;
        Vector4 field = SnowEffectController.ResolveFieldSettings(new Vector2(20f, 10f), 1.1f, 1.5f, 1f);
        Vector4 half = SnowEffectController.ResolveFieldSettings(new Vector2(20f, 10f), 1.1f, 1.5f, 0.5f);
        Vector4 clear = SnowEffectController.ResolveFieldSettings(new Vector2(20f, 10f), 1.1f, 1.5f, 0f);
        Vector4 large = SnowEffectController.ResolveFieldSettings(new Vector2(2000f, 1000f), 1.1f, 1.5f, 1f);
        Vector4 invalid = SnowEffectController.ResolveFieldSettings(new Vector2(-10f, 0f), 0f, -1f, 2f);
        Vector4 negative = SnowEffectController.ResolveFieldSettings(new Vector2(20f, 10f), 1f, 1f, -1f);
        Vector4 drift = SnowEffectController.ResolveDrift(1000000007.25d, -1000000002.5d, new Vector2(20f, 10f));
        Vector4 wrapped = SnowEffectController.ResolveDrift(1000000027.25d, -1000000012.5d, new Vector2(20f, 10f));
        return new JObject
        {
            ["prefabPresent"] = prefab != null,
            ["shaderController"] = prefab != null && prefab.GetComponent<SnowEffectController>() != null,
            ["particleSystems"] = prefab != null ? prefab.GetComponentsInChildren<ParticleSystem>(true).Length : -1,
            ["meshFilters"] = prefab != null ? prefab.GetComponentsInChildren<MeshFilter>(true).Length : -1,
            ["meshRenderers"] = prefab != null ? prefab.GetComponentsInChildren<MeshRenderer>(true).Length : -1,
            ["shader"] = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "",
            ["width"] = field.x, ["height"] = field.y, ["density"] = field.z,
            ["halfDensity"] = half.z, ["clearDensity"] = clear.z, ["largeDensity"] = large.z,
            ["invalidWidth"] = invalid.x, ["invalidHeight"] = invalid.y, ["invalidDensity"] = invalid.z,
            ["negativeDensity"] = negative.z, ["maximumFlakes"] = SnowEffectController.MaximumFlakes,
            ["driftX"] = drift.x, ["driftY"] = drift.y,
            ["wrappedX"] = wrapped.x, ["wrappedY"] = wrapped.y
        };
    }

    #endregion
}
