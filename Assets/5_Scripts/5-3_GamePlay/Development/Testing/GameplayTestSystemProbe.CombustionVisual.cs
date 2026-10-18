using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 掉落物燃烧材质采样

    // 只在独立禁用节点调用正式粒子工厂，采样实际绑定的材质而不推进正在玩的世界。
    private static JObject ServiceDroppedCombustionMaterial(JObject args)
    {
        var root = new GameObject("Lua 掉落物燃烧材质采样");
        root.SetActive(false);
        try
        {
            ParticleSystem first = WorldItemCombustionVisual.Create(root.transform);
            ParticleSystem second = WorldItemCombustionVisual.Create(root.transform);
            Material material = first.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Material other = second.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            return new JObject
            {
                ["assigned"] = material != null && other != null,
                ["shared"] = material != null && material == other,
                ["shader"] = material != null && material.shader != null ? material.shader.name : null,
                ["supported"] = material != null && material.shader != null && material.shader.isSupported,
                ["texture"] = material != null && material.mainTexture != null ? material.mainTexture.name : null
            };
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    #endregion
}
