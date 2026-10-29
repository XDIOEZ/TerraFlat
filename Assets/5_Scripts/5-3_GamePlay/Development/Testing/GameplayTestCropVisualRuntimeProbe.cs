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

    private static void Register()
    {
        GameplayTestRuntimeProbe.Register("agriculture", "crop_visual_reenable", Capture);
        GameplayTestRuntimeProbe.Register("agriculture", "cultivator_cold_presentation", CaptureCultivatorColdPresentation);
    }

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
                // 实例化仅负责注册，现场必须先正式加载模块再检查表现启停。
                if (!item.IsInitialized) item.Load();
                Mod_Crop crop = item.GetComponentInChildren<Mod_Crop>(true);
                Mod_CropVisual visual = item.GetComponentInChildren<Mod_CropVisual>(true);
                SpriteRenderer renderer = item.Sprite;
                if (crop == null || visual == null || renderer == null || !visual.IsRuntimeLoaded)
                    throw new InvalidOperationException($"正式作物缺少已加载的成长或表现模块：{id}，" +
                        $"成长={crop != null}，表现={visual != null}，贴图={renderer != null}，表现已加载={visual?.IsRuntimeLoaded}");
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

    #region 培育仓冷节点只读表现回归
    // 独立定义和快照复现先显示后激活，不登记机器或触碰玩家现场。
    private static JObject CaptureCultivatorColdPresentation(Player player, JObject args)
    {
        const string id = "SpaceCultivator";
        GameRes resources = GameRes.ExistingInstance;
        MachineDefinition sharedDefinition = MachineCatalog.Get(id);
        MachineContent sharedContent = sharedDefinition.CachedContent;
        MachineDefinition definition = MachinePersistence.Clone(sharedDefinition);
        var node = new MachineEntity { Definition = definition, Snapshot = resources.CreateItemData(id) };
        var state = new CultivatorState();
        state.Plots.Add(new CultivatorPlotState { CropId = "RadishCrop", Growth = .25f });
        state.Plots.Add(new CultivatorPlotState { CropId = "RiceCrop", Growth = 0f });
        state.Plots.Add(new CultivatorPlotState());
        state.Plots.Add(new CultivatorPlotState());
        MachineModuleState.Write(node.Snapshot, Mod_Cultivator.ModuleId, state);
        byte[] before = (byte[])MachineModuleState.Binary(node.Snapshot, Mod_Cultivator.ModuleId).BitData.Clone();
        Sprite body = resources.ItemDefinitions[id].Sprite;
        if (body == null) throw new InvalidOperationException("培育仓回归缺少正式机身图片。");
        var presentation = new CultivatorPlotPresentation();
        var result = new JObject { ["initiallyCold"] = !node.Active && node.Logic == null && definition.CachedContent == null };
        presentation.Refresh(node);
        result["plotCount"] = presentation.PlotCount;
        result["contentResolved"] = definition.CachedContent != null;
        MachineContent resolved = definition.Content;
        result["radishVisible"] = presentation.TryGetPlot(0, body, out Sprite radish, out _, out Vector3 scale);
        result["radishSprite"] = radish != null ? radish.GetInstanceID() : 0;
        result["expectedRadishSprite"] = resources.ItemDefinitions["RadishCrop"].Sprite.GetInstanceID();
        result["scaleX"] = scale.x;
        result["scaleY"] = scale.y;
        result["seedlingVisible"] = presentation.TryGetPlot(1, body, out Sprite seedling, out _, out _);
        resources.ItemDefinitions["RiceCrop"].TryGetVisualStateSprite("seedling", out Sprite expectedSeedling);
        result["seedlingMatches"] = seedling != null && seedling == expectedSeedling;
        result["emptyHidden"] = !presentation.TryGetPlot(2, body, out _, out _, out _);
        result["invalidHidden"] = !presentation.TryGetPlot(-1, body, out _, out _, out _) &&
            !presentation.TryGetPlot(presentation.PlotCount, body, out _, out _, out _);
        result["missingBodyHidden"] = !presentation.TryGetPlot(0, null, out _, out _, out _);
        presentation.Refresh(node);
        result["contentReused"] = ReferenceEquals(resolved, definition.Content) &&
            ReferenceEquals(resolved, MachineContent.GetOrCreateDefinitionContent(definition));
        result["snapshotUnchanged"] = Convert.ToBase64String(before) == Convert.ToBase64String(
            MachineModuleState.Binary(node.Snapshot, Mod_Cultivator.ModuleId).BitData);

        state.Plots[0].CropId = null;
        state.Plots[1].Growth = 1f;
        MachineModuleState.Write(node.Snapshot, Mod_Cultivator.ModuleId, state);
        presentation.Refresh(node);
        result["removedHidden"] = !presentation.TryGetPlot(0, body, out _, out _, out _);
        result["matureVisible"] = presentation.TryGetPlot(1, body, out Sprite mature, out _, out _);
        resources.ItemDefinitions["RiceCrop"].TryGetVisualStateSprite("mature", out Sprite expectedMature);
        result["matureMatches"] = mature != null && mature == expectedMature && mature != seedling;
        presentation.Refresh(null);
        result["unboundEmpty"] = presentation.PlotCount == 0 && !presentation.TryGetPlot(1, body, out _, out _, out _);
        presentation.Refresh(node);
        result["reboundVisible"] = presentation.PlotCount == state.Plots.Count &&
            presentation.TryGetPlot(1, body, out Sprite rebound, out _, out _) && rebound == mature;

        var retryDefinition = new MachineDefinition { Id = "__missing_cultivator_presentation_test__", LogicId = "cultivator" };
        var retryNode = new MachineEntity { Definition = retryDefinition, Snapshot = node.Snapshot };
        try { presentation.Refresh(retryNode); result["missingDefinitionRejected"] = false; }
        catch (InvalidOperationException error) { result["missingDefinitionRejected"] = error.Message.Contains(retryDefinition.Id); }
        result["failedBindingCleared"] = presentation.PlotCount == 0 &&
            !presentation.TryGetPlot(1, body, out _, out _, out _);
        retryDefinition.Id = id;
        presentation.Refresh(retryNode);
        result["sameDefinitionRetryVisible"] = retryDefinition.CachedContent != null &&
            presentation.TryGetPlot(1, body, out Sprite retried, out _, out _) && retried == mature;
        retryNode.Snapshot = null;
        try { presentation.Refresh(retryNode); result["missingSnapshotRejected"] = false; }
        catch (InvalidOperationException) { result["missingSnapshotRejected"] = true; }
        result["failedSnapshotCleared"] = presentation.PlotCount == 0 &&
            !presentation.TryGetPlot(1, body, out _, out _, out _);
        retryNode.Snapshot = node.Snapshot;
        presentation.Refresh(retryNode);
        MachineContent previousContent = retryDefinition.Content;
        retryDefinition.Content = null;
        presentation.Refresh(retryNode);
        result["sameDefinitionContentRebound"] = !ReferenceEquals(previousContent, retryDefinition.Content) &&
            presentation.TryGetPlot(1, body, out Sprite refreshed, out _, out _) && refreshed == mature;
        result["stillCold"] = !node.Active && node.Logic == null && node.Processor == null &&
            !retryNode.Active && retryNode.Logic == null && retryNode.Processor == null;
        result["sharedDefinitionUnchanged"] = ReferenceEquals(sharedContent, sharedDefinition.CachedContent);
        return result;
    }
    #endregion
}
