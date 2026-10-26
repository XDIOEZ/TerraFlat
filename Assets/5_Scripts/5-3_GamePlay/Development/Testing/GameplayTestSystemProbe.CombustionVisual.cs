using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region Shader 火焰资源与生命周期采样

    // 独立节点调用正式工厂，采样共享资源、排序、包围盒与池复用，不推进世界模拟。
    private static JObject ServiceDroppedCombustionMaterial(JObject args)
    {
        var root = new GameObject("Lua Shader 火焰采样");
        root.SetActive(false);
        try
        {
            ShaderFlameEffect first = WorldItemCombustionVisual.Create(root.transform);
            ShaderFlameEffect second = WorldItemCombustionVisual.Create(root.transform);
            Material material = first.Renderer.sharedMaterial;
            Material other = second.Renderer.sharedMaterial;
            Mesh mesh = first.GetComponent<MeshFilter>().sharedMesh;
            Mesh otherMesh = second.GetComponent<MeshFilter>().sharedMesh;
            first.Configure(new Vector2(2f, 3f), 1.2f);
            first.SetSorting(0, 37);
            Bounds bounds = first.Renderer.localBounds;
            var properties = new MaterialPropertyBlock();
            first.Renderer.GetPropertyBlock(properties);
            Vector4 instance = properties.GetVector(Shader.PropertyToID("_FlameInstance"));
            second.Renderer.GetPropertyBlock(properties);
            Vector4 secondInstance = properties.GetVector(Shader.PropertyToID("_FlameInstance"));

            root.SetActive(true);
            bool startsStopped = !first.Renderer.enabled && !second.Renderer.enabled;
            first.SetBurning(true);
            bool lights = first.Renderer.enabled;
            first.enabled = false;
            bool disabled = !first.Renderer.enabled;
            first.enabled = true;
            bool reenabled = first.Renderer.enabled;
            first.SetBurning(false);
            root.SetActive(false);
            root.SetActive(true);
            bool staysExtinguished = !first.Renderer.enabled;
            first.Configure(Vector2.one, 0f);
            first.SetBurning(true);
            bool zeroIntensityHidden = !first.Renderer.enabled;
            first.Configure(new Vector2(float.NaN, float.PositiveInfinity), float.NaN);
            Bounds sanitized = first.Renderer.localBounds;

            var adapterRoot = new GameObject("Lua 燃烧模块适配器");
            adapterRoot.transform.SetParent(root.transform, false);
            adapterRoot.transform.localRotation = Quaternion.Euler(0f, 0f, 75f);
            CombustionVisualEffect adapter = adapterRoot.AddComponent<CombustionVisualEffect>();
            adapter.ConfigureFlamePresentation(2f, 1.5f, 3f, true, false, Vector3.zero);
            adapter.BindWorldPresentation(adapter, 0, 80);
            adapter.SetCombustionActive(true);
            ShaderFlameEffect adapted = adapterRoot.GetComponentInChildren<ShaderFlameEffect>();
            bool adapterLights = adapted != null && adapted.Renderer.enabled;
            int adapterOrder = adapted != null ? adapted.Renderer.sortingOrder : -1;
            Vector3 attachedUp = adapted != null ? adapted.transform.up : Vector3.zero;
            adapter.SetCombustionActive(false);
            bool adapterExtinguishes = adapted != null && !adapted.Renderer.enabled;
            // 火把头只提供位置，火焰在倾斜和左右翻面后仍可独立向世界上方燃烧。
            adapter.ConfigureFlamePresentation(2f, 1.5f, 3f, false, false, Vector3.zero);
            Vector3 uprightUp = adapted != null ? adapted.transform.up : Vector3.zero;
            adapterRoot.transform.localRotation = Quaternion.Euler(0f, 180f, -35f);
            adapter.ConfigureFlamePresentation(2f, 1.5f, 3f, false, false, Vector3.zero);
            Vector3 mirroredUp = adapted != null ? adapted.transform.up : Vector3.zero;

            // 直接采样正式熔炉配置，炉口底面像素位置由 Lua 断言。
            string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath,
                "GameConfig/Items/shells/building_bodies.json");
            JObject smelter = JObject.Parse(StreamingAssetsTextLoader.ReadAllText(path))["items"]
                .OfType<JObject>().Single(value => (string)value["id"] == "Smelter");
            JObject smelterFlame = (JObject)smelter["modules"]["combustionVisual"];
            JObject smelterParameters = (JObject)smelterFlame["parameters"];

            return new JObject
            {
                ["assigned"] = material != null && other != null,
                ["shared"] = material != null && material == other,
                ["shader"] = material != null && material.shader != null ? material.shader.name : null,
                ["supported"] = material != null && material.shader != null && material.shader.isSupported,
                ["passes"] = material != null && material.FindPass("Flame2D") >= 0 && material.FindPass("FlameForward") >= 0,
                ["pixelsPerUnit"] = material != null ? material.GetFloat("_PixelsPerUnit") : 0f,
                ["animationFPS"] = material != null ? material.GetFloat("_AnimationFPS") : 0f,
                ["sharedMesh"] = mesh != null && mesh == otherMesh,
                ["vertices"] = mesh != null ? mesh.vertexCount : 0,
                ["particles"] = root.GetComponentsInChildren<ParticleSystem>(true).Length,
                ["independentPhase"] = instance.w != secondInstance.w,
                ["width"] = instance.x,
                ["height"] = instance.y,
                ["boundsWidth"] = bounds.size.x,
                ["boundsHeight"] = bounds.size.y,
                ["boundsCenterY"] = bounds.center.y,
                ["sortingOrder"] = first.Renderer.sortingOrder,
                ["startsStopped"] = startsStopped,
                ["lights"] = lights,
                ["disabled"] = disabled,
                ["reenabled"] = reenabled,
                ["staysExtinguished"] = staysExtinguished,
                ["zeroIntensityHidden"] = zeroIntensityHidden,
                ["finiteBounds"] = !float.IsNaN(sanitized.size.x) && !float.IsInfinity(sanitized.size.x) &&
                                   !float.IsNaN(sanitized.size.y) && !float.IsInfinity(sanitized.size.y),
                ["adapterLights"] = adapterLights,
                ["adapterOrder"] = adapterOrder,
                ["adapterExtinguishes"] = adapterExtinguishes,
                ["attachedUpX"] = attachedUp.x,
                ["attachedUpY"] = attachedUp.y,
                ["uprightUpX"] = uprightUp.x,
                ["uprightUpY"] = uprightUp.y,
                ["uprightUpZ"] = uprightUp.z,
                ["mirroredUpX"] = mirroredUp.x,
                ["mirroredUpY"] = mirroredUp.y,
                ["mirroredUpZ"] = mirroredUp.z,
                ["smelter"] = new JObject
                {
                    ["spriteAddress"] = (string)smelter["visual"]["spriteAddress"],
                    ["enabled"] = (bool)smelterFlame["enabled"],
                    ["followRenderer"] = (bool)smelterParameters["followSourceRenderer"],
                    ["anchor"] = smelterParameters["sourceRendererAnchorOffset"].DeepClone()
                }
            };
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    #endregion
}
