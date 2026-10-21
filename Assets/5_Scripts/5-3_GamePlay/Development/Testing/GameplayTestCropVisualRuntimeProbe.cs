using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>在隔离世界用正式作物检查表现模块停用、恢复与卸载。</summary>
public static class GameplayTestCropVisualRuntimeProbe
{
    #region 农作物表现启停回归

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif

    private static void Register() => GameplayTestRuntimeProbe.Register(
        "agriculture", "crop_visual_reenable", Capture);

    // 两种正式作物分别覆盖连续缩放和阶段图片，不替表现模块重算成长规则。
    private static JObject Capture(Player player, JObject args)
    {
        var samples = new JArray();
        foreach (string id in new[] { "RadishCrop", "RiceCrop" })
        {
            Item item = ItemMgr.Instance.InstantiateItem(id, player.transform.position);
            if (item == null) throw new InvalidOperationException("无法创建正式回归作物：" + id);
            try
            {
                Mod_Crop crop = item.GetComponentInChildren<Mod_Crop>(true);
                Mod_CropVisual visual = item.GetComponentInChildren<Mod_CropVisual>(true);
                SpriteRenderer renderer = item.Sprite;
                if (crop == null || visual == null || renderer == null || !visual.IsRuntimeLoaded)
                    throw new InvalidOperationException("正式作物缺少已加载的成长或表现模块：" + id);
                RuntimeItemDefinition definition = GameRes.Instance.ItemDefinitions[id];
                bool discrete = definition.TryGetVisualStateSprite("mature", out Sprite matureSprite);
                uint generation = visual.RuntimeGeneration;
                crop.SetStage(CropStage.Seedling);
                JObject initial = Snapshot(renderer);
                visual.enabled = false;
                JObject disabled = Snapshot(renderer);
                crop.SetStage(CropStage.Mature);
                JObject whileDisabled = Snapshot(renderer);
                visual.enabled = true;
                JObject restored = Snapshot(renderer);
                crop.SetStage(CropStage.Seedling);
                JObject afterGrowthEvent = Snapshot(renderer);
                for (int i = 0; i < 2; i++)
                {
                    visual.enabled = false;
                    crop.SetStage(CropStage.Mature);
                    visual.enabled = true;
                    crop.SetStage(CropStage.Seedling);
                }
                JObject repeated = Snapshot(renderer);
                uint restoredGeneration = visual.RuntimeGeneration;
                visual.Unload();
                JObject unloaded = Snapshot(renderer);
                visual.enabled = false;
                visual.enabled = true;
                crop.SetStage(CropStage.Mature);
                samples.Add(new JObject
                {
                    ["id"] = id, ["discrete"] = discrete,
                    ["seedlingScale"] = visual.SeedlingScale,
                    ["buriedClip"] = Mathf.Clamp01(visual.buriedClip),
                    ["matureSprite"] = discrete ? matureSprite.GetInstanceID() : renderer.sprite.GetInstanceID(),
                    ["initial"] = initial, ["disabled"] = disabled, ["whileDisabled"] = whileDisabled,
                    ["restored"] = restored, ["afterGrowthEvent"] = afterGrowthEvent, ["repeated"] = repeated,
                    ["generation"] = generation, ["restoredGeneration"] = restoredGeneration,
                    ["unloaded"] = unloaded, ["afterUnloadEnable"] = Snapshot(renderer),
                    ["loadedAfterUnloadEnable"] = visual.IsRuntimeLoaded
                });
            }
            finally { ItemMgr.Instance.DespawnItem(item, false); }
        }
        return new JObject { ["samples"] = samples };
    }

    private static JObject Snapshot(SpriteRenderer renderer)
    {
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        return new JObject
        {
            ["scaleX"] = renderer.transform.localScale.x,
            ["scaleY"] = renderer.transform.localScale.y,
            ["sprite"] = renderer.sprite != null ? renderer.sprite.GetInstanceID() : 0,
            ["material"] = renderer.sharedMaterial != null ? renderer.sharedMaterial.GetInstanceID() : 0,
            ["clip"] = properties.GetFloat(Shader.PropertyToID("_BodyClip"))
        };
    }

    #endregion
}
