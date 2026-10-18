using System;
using System.Collections.Generic;
using FlatWorld.Diagnostics;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 太阳阴影与建筑能力缓存规则

    private static void RegisterShadowPerformanceOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("rendering/shadow_proxy_changes", ShadowProxyChanges);
        operations.Add("rendering/building_capability_cache", BuildingCapabilityCache);
        operations.Add("rendering/ship_part_placement_state", ServiceShipPartPlacementState);
        operations.Add("rendering/effect_composition", RenderEffectComposition);
    }

    // 通过真实 MPB 验证静态覆雪不重传，而水线换图和效果启停仍会提交。
    private static JObject RenderEffectComposition(JObject args)
    {
        var root = new GameObject("[LuaRenderEffectComposition]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var texture = new Texture2D(4, 4);
        Sprite first = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0f), 1f);
        Sprite second = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 1f), 1f);
        bool statisticsEnabled = PerformanceDebugRegistry.StatisticsEnabled;
        try
        {
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = first;
            var snow = root.AddComponent<SeasonalSnowRenderEffect>();
            var controller = root.AddComponent<ActorRenderEffectController>();
            root.SetActive(true);
            PerformanceDebugRegistry.SetStatisticsEnabled(true);
            long framesBefore = ReadRenderDiagnostic("FlatWorld.ActorRender.Frame", true);
            long idleBefore = ReadRenderDiagnostic("FlatWorld.ActorRender.IdleSkippedFrames", false);
            long submittedBefore = ReadRenderDiagnostic("FlatWorld.ActorRender.SubmittedRenderers", false);
            long spriteSkippedBefore = ReadRenderDiagnostic("FlatWorld.ActorRender.UnchangedSpriteRenderers", false);
            controller.RenderFrame(0f);
            int bodyMinId = Shader.PropertyToID("_BodyMinV");
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetFloat(bodyMinId, 42f);
            renderer.SetPropertyBlock(block);
            renderer.sprite = second;
            controller.RenderFrame(0f);
            renderer.GetPropertyBlock(block);
            float snowIdleMin = block.GetFloat(bodyMinId);
            snow.enabled = false;
            controller.RenderFrame(0f);
            renderer.GetPropertyBlock(block);
            float snowDisabledMin = block.GetFloat(bodyMinId);
            root.AddComponent<WaterImmersionRenderEffect>();
            controller.RefreshBindings();
            renderer.sprite = first;
            controller.RenderFrame(0f);
            renderer.GetPropertyBlock(block);
            float waterFirstMin = block.GetFloat(bodyMinId);
            renderer.sprite = second;
            controller.RenderFrame(0f);
            renderer.GetPropertyBlock(block);
            float waterChangedMin = block.GetFloat(bodyMinId);
            controller.RenderFrame(0f);
            return new JObject
            {
                ["snowIdleMin"] = snowIdleMin, ["snowDisabledMin"] = snowDisabledMin,
                ["waterFirstMin"] = waterFirstMin, ["waterChangedMin"] = waterChangedMin,
                ["diagnosticFrames"] = ReadRenderDiagnostic("FlatWorld.ActorRender.Frame", true) - framesBefore,
                ["diagnosticIdleFrames"] = ReadRenderDiagnostic("FlatWorld.ActorRender.IdleSkippedFrames", false) - idleBefore,
                ["diagnosticSubmitted"] = ReadRenderDiagnostic("FlatWorld.ActorRender.SubmittedRenderers", false) - submittedBefore,
                ["diagnosticSpriteSkipped"] = ReadRenderDiagnostic("FlatWorld.ActorRender.UnchangedSpriteRenderers", false) - spriteSkippedBefore
            };
        }
        finally
        {
            PerformanceDebugRegistry.SetStatisticsEnabled(statisticsEnabled);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    // 只读正式诊断快照，Lua 据实际提交和跳过数量判断点位有没有失真。
    private static long ReadRenderDiagnostic(string name, bool calls)
    {
        foreach (PerformanceDebugRegistry.Snapshot point in PerformanceDebugRegistry.CaptureSnapshot())
            if (point.Name == name) return calls ? point.Calls : point.Work;
        throw new InvalidOperationException("渲染性能点没有登记：" + name);
    }

    // 独立禁用物品只采正式能力查询，模块结构与动态属性均不复制生产判断。
    private static JObject BuildingCapabilityCache(JObject args) => WithEntityProbeItem(item =>
    {
        item.BindData(new Data_GeneralItem { IDName = "probe:building-capabilities" });
        bool empty = BuildingPlacementLifecycle.GetExtension(item) == null &&
            BuildingPlacementLifecycle.GetTraversalPolicy(item) == null;
        Mod_ShipPart Create(string stableName)
        {
            var child = new GameObject(stableName);
            child.transform.SetParent(item.transform, false);
            Mod_ShipPart part = child.AddComponent<Mod_ShipPart>();
            part.ModData.StableName = stableName;
            part.item = item;
            part.Configuration.Kind = ShipPieceKind.Floor;
            return part;
        }
        Mod_ShipPart first = Create("first");
        Mod_ShipPart second = Create("second");
        item.itemMods.AddMod(first);
        bool addInvalidated = ReferenceEquals(BuildingPlacementLifecycle.GetExtension(item), first) &&
            ReferenceEquals(BuildingPlacementLifecycle.GetTraversalPolicy(item), first);
        item.itemMods.AddMod(second);
        bool firstPreserved = ReferenceEquals(BuildingPlacementLifecycle.GetExtension(item), first) &&
            ReferenceEquals(BuildingPlacementLifecycle.GetTraversalPolicy(item), first);
        IBuildingPlacementExtension extension = BuildingPlacementLifecycle.GetExtension(item);
        IBuildingTraversalPolicy traversal = BuildingPlacementLifecycle.GetTraversalPolicy(item);
        int floorLayer = extension.OccupancyLayer;
        bool floorBlocks = traversal.BlocksMovement;
        first.Configuration.Kind = ShipPieceKind.Wall;
        first.Configuration.Layer = 3;
        first.Configuration.BlocksMovement = false;
        int changedLayer = extension.OccupancyLayer;
        bool changedBlocks = traversal.BlocksMovement;
        first.Configuration.BlocksMovement = true;
        bool liveBlocks = traversal.BlocksMovement;
        // 先预热两种正式查询，再单独采样重复查询的线程分配。
        BuildingPlacementLifecycle.GetExtension(item);
        BuildingPlacementLifecycle.GetTraversalPolicy(item);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            BuildingPlacementLifecycle.GetExtension(item);
            BuildingPlacementLifecycle.GetTraversalPolicy(item);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        item.itemMods.RemoveMod(first);
        bool removeInvalidated = ReferenceEquals(BuildingPlacementLifecycle.GetExtension(item), second) &&
            ReferenceEquals(BuildingPlacementLifecycle.GetTraversalPolicy(item), second);
        item.itemMods.RemoveMod(second);
        bool emptyAgain = BuildingPlacementLifecycle.GetExtension(item) == null &&
            BuildingPlacementLifecycle.GetTraversalPolicy(item) == null;
        item.itemMods.AddMod(first);
        bool readdInvalidated = ReferenceEquals(BuildingPlacementLifecycle.GetExtension(item), first) &&
            ReferenceEquals(BuildingPlacementLifecycle.GetTraversalPolicy(item), first);
        return new JObject
        {
            ["empty"] = empty, ["addInvalidated"] = addInvalidated, ["firstPreserved"] = firstPreserved,
            ["floorLayer"] = floorLayer, ["floorBlocks"] = floorBlocks, ["changedLayer"] = changedLayer,
            ["changedBlocks"] = changedBlocks, ["liveBlocks"] = liveBlocks, ["allocated"] = allocated,
            ["removeInvalidated"] = removeInvalidated, ["emptyAgain"] = emptyAgain,
            ["readdInvalidated"] = readdInvalidated
        };
    });

    // 使用正式代理采样复用、翻转、透明度、绘制模式及归池失效，不创建材质实例。
    private static JObject ShadowProxyChanges(JObject args)
    {
        var texture = new Texture2D(8, 8);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), Vector2.one * 0.5f,
            16f, 0, SpriteMeshType.FullRect, Vector4.one);
        Sprite nextFrame = Sprite.Create(texture, new Rect(0f, 0f, 4f, 8f), Vector2.one * 0.5f,
            16f, 0, SpriteMeshType.FullRect, Vector4.one);
        WorldShadowProjectionManager.ShadowProxy proxy = null;
        try
        {
            proxy = new WorldShadowProjectionManager.ShadowProxy();
            proxy.gameObject.hideFlags = HideFlags.HideAndDontSave;
            proxy.gameObject.SetActive(false);
            var block = new MaterialPropertyBlock();
            Vector4 caster = new(2f, 1f, 3f, 1f), uv = new(0f, 0f, 1f, 1f);
            bool initialSource = proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool initialProperties = proxy.UpdateProperties(block, caster, uv, 1f);
            bool repeatedSource = proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool repeatedProperties = proxy.UpdateProperties(block, caster, uv, 1f);
            bool frameChanged = proxy.SetSource(nextFrame, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool frameProperties = proxy.UpdateProperties(block, caster, new Vector4(0f, 0f, 0.5f, 1f), 1f);
            bool frameSprite = proxy.SpriteRenderer.sprite == nextFrame;
            bool frameMesh = proxy.gameObject.GetComponent<MeshFilter>().sharedMesh ==
                SharedSpriteMeshCache.GetSunShadowGeometry(nextFrame).Mesh;
            proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, true, false, 0.25f);
            proxy.SetPose(Vector3.one, Quaternion.identity, new Vector3(2f, 3f, 1f));
            bool alphaUpdated = proxy.UpdateProperties(block, caster, uv, 1f);
            proxy.MeshRenderer.GetPropertyBlock(block);
            float alpha = block.GetColor(Shader.PropertyToID("_RendererColor")).a;
            float mirroredScale = proxy.transform.localScale.x;
            proxy.SetPose(Vector3.one, Quaternion.identity, new Vector3(2f, 3f, 1f));
            float repeatedScale = proxy.transform.localScale.x;
            caster.x = 7f;
            bool movedFootUpdated = proxy.UpdateProperties(block, caster, uv, 1f);
            proxy.MeshRenderer.GetPropertyBlock(block);
            float foot = block.GetVector(Shader.PropertyToID("_SunShadowCaster")).x;
            proxy.enabled = true;
            proxy.SetSource(sprite, SpriteDrawMode.Sliced, Vector2.one * 2f, false, false, 1f);
            bool slicedVisible = proxy.SpriteRenderer.enabled && !proxy.MeshRenderer.enabled;
            proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool meshVisible = !proxy.SpriteRenderer.enabled && proxy.MeshRenderer.enabled;
            proxy.enabled = false;
            proxy.ResetSource();
            proxy.ResetBounds();
            bool recycledSource = proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool recycledProperties = proxy.UpdateProperties(block, caster, uv, 1f);
            return new JObject
            {
                ["initialSource"] = initialSource, ["initialProperties"] = initialProperties,
                ["repeatedSource"] = repeatedSource, ["repeatedProperties"] = repeatedProperties,
                ["frameChanged"] = frameChanged, ["frameProperties"] = frameProperties,
                ["frameSprite"] = frameSprite, ["frameMesh"] = frameMesh,
                ["alphaUpdated"] = alphaUpdated, ["alpha"] = alpha, ["mirroredScale"] = mirroredScale,
                ["repeatedScale"] = repeatedScale, ["movedFootUpdated"] = movedFootUpdated, ["foot"] = foot,
                ["slicedVisible"] = slicedVisible, ["meshVisible"] = meshVisible,
                ["recycledSource"] = recycledSource, ["recycledProperties"] = recycledProperties
            };
        }
        finally
        {
            if (proxy != null) UnityEngine.Object.DestroyImmediate(proxy.gameObject);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(nextFrame);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    #endregion
}
