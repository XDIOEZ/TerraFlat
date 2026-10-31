using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 手持食物 Shader 咬痕采样
    private static JObject CaptureHeldFoodShaderBites(JObject args)
    {
        var scene = CreateGameplayTestProbeScene("[LuaHeldFoodBites]");
        var root = new GameObject("[LuaHeldFoodBites]");
        var actor = new GameObject("[LuaHeldFoodOwner]");
        var services = new GameObject("[LuaHeldFoodRendering]");
        SceneManager.MoveGameObjectToScene(root, scene);
        SceneManager.MoveGameObjectToScene(actor, scene);
        SceneManager.MoveGameObjectToScene(services, scene);
        if (WorldSortingManager.GetInstance() == null) services.AddComponent<WorldSortingManager>();
        WorldRenderingManager rendering = WorldRenderingManager.EnsureForOwner(services.transform);
        root.SetActive(false);
        var item = root.AddComponent<GameplayTestRenderingLifecycleItem>();
        item.BindData(new Data_GeneralItem { IDName = "probe:food", Guid = 75831, inHand = true,
            Stack = new ItemStack { Amount = 2f } });
        Texture2D texture = new(2, 2);
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        texture.Apply();
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 1f);
        sprite.name = "FoodBiteProbe";
        var material = new Material(Shader.Find("Game/2D/Sprite-Lit-Master"));
        var originalMaterial = new Material(Shader.Find("Sprites/Default"));
        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = originalMaterial;
        item.Sprite = renderer;
        rendering.RegisterItemRendering(item);
        renderer.sortingOrder = 17;
        int originalLayer = renderer.sortingLayerID;
        item.Sprite = renderer;
        var held = root.AddComponent<Mod_HeldFood>();
        held.ModuleInit(item, new Ex_ModData_MemoryPackable { StableName = "heldFood",
            ModuleId = Mod_HeldFood.ModuleId, Enabled = true });
        var persistent = new ModData_FoodData();
        persistent.FoodData.Max_EatingProgress = args.Value<float?>("maximum") ?? 3f;
        var context = new FoodRuntimeContext(item, persistent, null, null, null, null, null);
        var ownerController = actor.AddComponent<ActorRenderEffectController>();
        ownerController.SetEffectSpriteMaterial(material);
        var samples = new JArray();
        var block = new MaterialPropertyBlock();
        int bitesId = Shader.PropertyToID("_HeldFoodBites");
        int circlesId = Shader.PropertyToID("_HeldFoodBiteCircles");
        bool registered = false;
        WorldSortingManager.GetResourceSortingKey(WorldSortingManager.HeldFoodCategory, out int heldLayer, out int heldOrder);
        try
        {
            root.SetActive(true);
            held.Load();
            held.BindFoodContext(context);
            block.SetFloat("_WaterEnabled", .37f);
            renderer.SetPropertyBlock(block);
            ownerController.RegisterExternalRenderers(root.transform);
            registered = true;
            foreach (JObject step in (JArray)args["steps"])
            {
                bool inHand = step.Value<bool?>("held") ?? item.InHand;
                if (!inHand && registered)
                {
                    ownerController.UnregisterExternalRenderers(root.transform);
                    registered = false;
                }
                item.SetInHand(inHand);
                if (inHand && !registered)
                {
                    ownerController.RegisterExternalRenderers(root.transform);
                    registered = true;
                }
                renderer.flipX = step.Value<bool?>("flipX") ?? false;
                renderer.flipY = step.Value<bool?>("flipY") ?? false;
                renderer.sprite = step.Value<bool?>("missingSprite") == true ? null : sprite;
                if (step["progress"] != null) context.EatingProgress = step.Value<float>("progress");
                held.OnFoodStateChanged(new FoodStateChangedContext(context));
                ActorRenderEffectController controller = root.GetComponent<ActorRenderEffectController>();
                controller?.RenderFrame(0f);
                ownerController.RenderFrame(0f);
                renderer.GetPropertyBlock(block);
                Vector4 parameters = block.GetVector(bitesId);
                var circlesTexture = parameters.x > 0 ? block.GetTexture(circlesId) as Texture2D : null;
                var circles = new JArray();
                if (circlesTexture != null)
                    foreach (Vector4 circle in circlesTexture.GetPixelData<Vector4>(0))
                        circles.Add(new JArray(circle.x, circle.y, circle.z));
                samples.Add(new JObject
                {
                    ["count"] = parameters.x, ["flipX"] = parameters.z, ["flipY"] = parameters.w,
                    ["texture"] = circlesTexture != null ? circlesTexture.GetInstanceID() : 0,
                    ["textureWidth"] = circlesTexture != null ? circlesTexture.width : 0,
                    ["textureHeight"] = circlesTexture != null ? circlesTexture.height : 0,
                    ["effectCount"] = root.GetComponents<HeldFoodBiteRenderEffect>().Length,
                    ["maskCount"] = root.GetComponentsInChildren<SpriteMask>().Length,
                    ["water"] = block.GetFloat("_WaterEnabled"), ["circles"] = circles,
                    ["material"] = renderer.sharedMaterial.GetInstanceID(),
                    ["sortingLayer"] = renderer.sortingLayerID,
                    ["sortingOrder"] = renderer.sortingOrder
                });
            }
            return new JObject { ["samples"] = samples, ["heldLayer"] = heldLayer, ["heldOrder"] = heldOrder,
                ["originalLayer"] = originalLayer, ["masterMaterial"] = material.GetInstanceID(),
                ["originalMaterial"] = originalMaterial.GetInstanceID() };
        }
        finally
        {
            if (registered) ownerController.UnregisterExternalRenderers(root.transform);
            held.Unload();
            rendering.UnregisterItemRendering(item);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(actor);
            Object.DestroyImmediate(services);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(originalMaterial);
            CloseGameplayTestProbeScene(scene);
        }
    }
    #endregion
}
