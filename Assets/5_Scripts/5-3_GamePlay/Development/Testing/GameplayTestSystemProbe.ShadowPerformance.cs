using System;
using System.Collections.Generic;
using FlatWorld.Diagnostics;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 太阳阴影与建筑能力缓存规则

    private static void RegisterShadowPerformanceOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("rendering/shadow_quad_indices", ShadowQuadIndices);
        operations.Add("rendering/shadow_proxy_changes", ShadowProxyChanges);
        operations.Add("rendering/shadow_proxy_blur_scale", ShadowProxyBlurScale);
        operations.Add("rendering/shadow_footprint_matrix", ShadowFootprintMatrix);
        operations.Add("rendering/contact_shadow_culling_indices", ContactShadowCullingIndices);
        operations.Add("rendering/building_capability_cache", BuildingCapabilityCache);
        operations.Add("rendering/ship_part_placement_state", ServiceShipPartPlacementState);
        operations.Add("rendering/ship_block_visual", ServiceShipBlockVisual);
        operations.Add("rendering/effect_composition", RenderEffectComposition);
        operations.Add("rendering/snow_sparse_refresh", SnowSparseRefresh);
        operations.Add("rendering/sprite_snow_bounds", SpriteSnowBounds);
    }

    #region 阴影批次索引采样

    // 读取正式初始化流程上传的网格索引，绕序和边界交给 Lua 检查。
    private static JObject ShadowQuadIndices(JObject args)
    {
        int quads = (int)Number(args, "quads", 1);
        var mesh = new Mesh { name = "[LuaShadowQuadIndices]", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            mesh.SetVertexBufferParams(quads * 4,
                new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position,
                    UnityEngine.Rendering.VertexAttributeFormat.Float32, 3));
            FlatWorld.AIECS.AiecsShadowRenderer.InitializeQuadBatchTriangleIndexBuffer(mesh, quads);
            using var data = Mesh.AcquireReadOnlyMeshData(mesh);
            var indices = data[0].GetIndexData<ushort>();
            var first = new JArray();
            var last = new JArray();
            for (int index = 0; index < 6; index++)
            {
                first.Add(indices[index]);
                last.Add(indices[indices.Length - 6 + index]);
            }
            return new JObject { ["vertexCount"] = data[0].vertexCount, ["indexCount"] = indices.Length,
                ["uint16"] = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16,
                ["first"] = first, ["last"] = last };
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }

    #endregion

    // 使用裁剪规则本身覆盖相机切换和初始容量以上的可见索引。
    private static JObject ContactShadowCullingIndices(JObject args)
    {
        const int instanceCount = 96;
        var instances = new ContactShadowBatchRenderer.InstanceData[instanceCount];
        for (int i = 0; i < instanceCount; i++)
            instances[i].Footprint = new Vector4(i - 48, 0f, 0.25f, 0.25f);

        var indices = new int[instanceCount];
        var planes = new NativeArray<Plane>(1, Allocator.Temp);
        try
        {
            planes[0] = new Plane(Vector3.right, new Vector3(-100f, 0f, 0f));
            int allVisible = ContactShadowBatchRenderer.CollectVisibleInstanceIndices(
                instances, instanceCount, planes, indices);
            bool allVisibleOrdered = allVisible == instanceCount;
            for (int i = 0; allVisibleOrdered && i < allVisible; i++)
                allVisibleOrdered = indices[i] == i;

            planes[0] = new Plane(Vector3.right, Vector3.zero);
            int partialVisible = ContactShadowBatchRenderer.CollectVisibleInstanceIndices(
                instances, instanceCount, planes, indices);
            bool partialOrdered = partialVisible == 48;
            for (int i = 0; partialOrdered && i < partialVisible; i++)
                partialOrdered = indices[i] == i + 48;

            // 模拟切换到只看数组右端的相机，旧相机索引不能残留到本轮有效范围。
            planes[0] = new Plane(Vector3.right, new Vector3(40f, 0f, 0f));
            int changedCameraVisible = ContactShadowBatchRenderer.CollectVisibleInstanceIndices(
                instances, instanceCount, planes, indices);
            bool changedCameraOrdered = changedCameraVisible == 8;
            for (int i = 0; changedCameraOrdered && i < changedCameraVisible; i++)
                changedCameraOrdered = indices[i] == i + 88;

            planes[0] = new Plane(Vector3.right, new Vector3(100f, 0f, 0f));
            int invisible = ContactShadowBatchRenderer.CollectVisibleInstanceIndices(
                instances, instanceCount, planes, indices);
            return new JObject
            {
                ["allVisible"] = allVisibleOrdered,
                ["partialVisible"] = partialOrdered,
                ["changedCamera"] = changedCameraOrdered,
                ["allInvisible"] = invisible == 0
            };
        }
        finally { planes.Dispose(); }
    }

    // 读取正式控制器的 MPB，覆盖偏心原点、连续翻面、换图和静态免重传。
    private static JObject SpriteSnowBounds(JObject args)
    {
        var root = new GameObject("[LuaSpriteSnowBounds]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var texture = new Texture2D(8, 12);
        Sprite first = Sprite.Create(texture, new Rect(0, 0, 8, 12), new Vector2(.25f, .25f), 2f,
            0, SpriteMeshType.FullRect);
        Sprite second = Sprite.Create(texture, new Rect(0, 0, 8, 12), new Vector2(.25f, .75f), 2f,
            0, SpriteMeshType.FullRect);
        try
        {
            var renderer = root.AddComponent<SpriteRenderer>();
            root.AddComponent<WaterImmersionRenderEffect>();
            var controller = root.AddComponent<ActorRenderEffectController>();
            root.SetActive(true);
            int boundsId = Shader.PropertyToID("_SpriteLocalBounds");
            var block = new MaterialPropertyBlock();
            var samples = new JArray();
            for (int index = 0; index < 8; index++)
            {
                renderer.sprite = index < 4 ? first : second;
                renderer.flipX = (index & 1) != 0;
                renderer.flipY = (index & 2) != 0;
                root.transform.SetPositionAndRotation(new Vector3(15f, -21f, 0f),
                    Quaternion.Euler(0f, 0f, index * 47f));
                root.transform.localScale = new Vector3(index % 2 == 0 ? 2f : -2f, .5f, 1f);
                controller.RenderFrame(0f);
                renderer.GetPropertyBlock(block);
                Vector4 bounds = block.GetVector(boundsId);
                block.SetVector(boundsId, new Vector4(37f, 0f, 0f, 0f));
                renderer.SetPropertyBlock(block);
                controller.RenderFrame(0f);
                renderer.GetPropertyBlock(block);
                samples.Add(new JObject
                {
                    ["centerX"] = bounds.x, ["centerY"] = bounds.y,
                    ["halfWidth"] = bounds.z, ["halfHeight"] = bounds.w,
                    ["unchangedMarker"] = block.GetVector(boundsId).x
                });
            }
            return new JObject { ["samples"] = samples };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    /// <summary>对照正式雪厚采样，覆盖无雪跳过、有效覆盖面与玩家编辑的边界。</summary>
    private static JObject SnowSparseRefresh(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(2, 2);
        buffer.SetCell(0, 0, new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        buffer.SetCell(1, 0, new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        using ChunkTerrainData terrain = buffer.Seal();

        bool emptySkipped = !WorldSnowSystem.HasPotentialSnow(terrain);
        terrain.SetEnvironmentValue(SnowDepthLayer.LayerId, 0, 0, 0f);
        bool zeroLayerSkipped = !WorldSnowSystem.HasPotentialSnow(terrain);
        float maximumError = 0f;
        int compared = 0;

        void Compare(int x, int y)
        {
            TerrainCell surface = TerrainSupportLayer.GetSurfaceCell(terrain, x, y);
            float original = WorldSnowSystem.GetSurfaceDepth(terrain, x, y, null, 0f);
            float reused = WorldSnowSystem.GetSurfaceDepth(terrain, x, y, surface, null, 0f);
            maximumError = Mathf.Max(maximumError, Mathf.Abs(original - reused));
            compared++;
        }

        terrain.SetEnvironmentValue(SnowDepthLayer.LayerId, 0, 0, 0.3f);
        bool naturalDetected = WorldSnowSystem.HasPotentialSnow(terrain);
        Compare(0, 0);
        terrain.SetEnvironmentValue(SnowDepthLayer.LayerId, 0, 0, 0f);

        terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, 0.4f);
        bool weatherDetected = WorldSnowSystem.HasPotentialSnow(terrain);
        Compare(0, 0);
        terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, 0f);

        terrain.SetEnvironmentValue(WorldSnowInteraction.EditedLayer, 0, 0, 1f);
        terrain.SetEnvironmentValue(WorldSnowInteraction.DepthLayer, 0, 0, 0.7f);
        terrain.SetEnvironmentValue(WorldSnowInteraction.SeasonLayer, 0, 0, 0.2f);
        bool editedDetected = WorldSnowSystem.HasPotentialSnow(terrain);
        Compare(0, 0);

        TerrainSupportLayer.Set(terrain, 0, 0, 2, 1);
        Compare(0, 0);
        terrain.SetCell(1, 0, default);
        Compare(1, 0);

        terrain.SetEnvironmentValue(WorldSnowInteraction.EditedLayer, 0, 0, 0f);
        bool clearedSkipped = !WorldSnowSystem.HasPotentialSnow(terrain);
        return new JObject
        {
            ["emptySkipped"] = emptySkipped,
            ["zeroLayerSkipped"] = zeroLayerSkipped,
            ["naturalDetected"] = naturalDetected,
            ["weatherDetected"] = weatherDetected,
            ["editedDetected"] = editedDetected,
            ["clearedSkipped"] = clearedSkipped,
            ["compared"] = compared,
            ["maximumError"] = maximumError
        };
    }

    // 独立四角基准对照正式仿射包围盒，覆盖镜像、偏心点、非等比缩放和三维旋转。
    private static JObject ShadowFootprintMatrix(JObject args)
    {
        Bounds[] localBounds =
        {
            new(new Vector3(1.3f, -.8f, 0f), new Vector3(5.4f, 3.3f, 0f)),
            new(new Vector3(-2f, 3f, 9f), new Vector3(1.2f, 7.5f, 2f)),
            new(new Vector3(.2f, -.4f, 0f), Vector3.zero)
        };
        Vector3[] scales =
        {
            Vector3.one, new(-2f, .4f, 1f), new(.25f, -3f, 1f), new(-1.5f, -2.5f, 2f)
        };
        Quaternion[] rotations =
        {
            Quaternion.identity, Quaternion.Euler(0f, 0f, 45f),
            Quaternion.Euler(0f, 0f, 173f), Quaternion.Euler(18f, -13f, 82f)
        };

        float maximumError = 0f;
        int cases = 0;
        for (int shape = 0; shape < localBounds.Length; shape++)
        for (int scale = 0; scale < scales.Length; scale++)
        for (int rotation = 0; rotation < rotations.Length; rotation++)
        for (int flips = 0; flips < 4; flips++)
        {
            Bounds local = localBounds[shape];
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(13.75f, -21.5f, 4.25f),
                rotations[rotation], scales[scale]);
            matrix.m01 += .17f;
            matrix.m20 -= .06f;
            bool flipX = (flips & 1) != 0, flipY = (flips & 2) != 0;
            Bounds actual = ShadowFootprintResolver.TransformVisibleBounds(local, matrix, flipX, flipY);

            Bounds expected = default;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = new((corner & 1) == 0 ? local.min.x : local.max.x,
                    corner < 2 ? local.min.y : local.max.y, 0f);
                if (flipX) point.x = -point.x;
                if (flipY) point.y = -point.y;
                point = matrix.MultiplyPoint3x4(point);
                if (corner == 0) expected = new Bounds(point, Vector3.zero);
                else expected.Encapsulate(point);
            }
            maximumError = Mathf.Max(maximumError,
                Mathf.Abs(actual.min.x - expected.min.x), Mathf.Abs(actual.min.y - expected.min.y),
                Mathf.Abs(actual.min.z - expected.min.z), Mathf.Abs(actual.max.x - expected.max.x),
                Mathf.Abs(actual.max.y - expected.max.y), Mathf.Abs(actual.max.z - expected.max.z));
            cases++;
        }

        // 石头导入轮廓含透明边距，默认脚点必须进入实际可见底边，显式零重叠仍保留。
        var paddedItem = new Bounds(new Vector3(-.03125f, 0f, 0f), new Vector3(.9375f, .875f, 0f));
        Vector3 contactFoot = ShadowFootprintResolver.ResolveFoot(Matrix4x4.identity, paddedItem, null);
        Vector3 explicitFoot = ShadowFootprintResolver.ResolveFoot(Matrix4x4.identity, paddedItem,
            new Vector2(0f, -.4f), footOverlap: 0f);
        var halfSize = new Bounds(paddedItem.center * .5f, paddedItem.size * .5f);
        Vector3 smallContactFoot = ShadowFootprintResolver.ResolveFoot(Matrix4x4.identity, halfSize, null);
        return new JObject
        {
            ["cases"] = cases, ["maximumError"] = maximumError,
            ["contactFootY"] = contactFoot.y, ["opaqueBottomY"] = -.3125f,
            ["smallContactFootY"] = smallContactFoot.y, ["smallOpaqueBottomY"] = -.15625f,
            ["explicitFootY"] = explicitFoot.y
        };
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
            bool initialProperties = proxy.UpdateProperties(block, caster, uv);
            bool repeatedSource = proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool repeatedProperties = proxy.UpdateProperties(block, caster, uv);
            bool frameChanged = proxy.SetSource(nextFrame, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool frameProperties = proxy.UpdateProperties(block, caster, new Vector4(0f, 0f, 0.5f, 1f));
            bool frameSprite = proxy.SpriteRenderer.sprite == nextFrame;
            bool frameMesh = proxy.gameObject.GetComponent<MeshFilter>().sharedMesh ==
                SharedSpriteMeshCache.GetSunShadowGeometry(nextFrame).Mesh;
            proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, true, false, 0.25f);
            proxy.SetPose(Vector3.one, Quaternion.identity, new Vector3(2f, 3f, 1f));
            bool alphaUpdated = proxy.UpdateProperties(block, caster, uv);
            proxy.MeshRenderer.GetPropertyBlock(block);
            float alpha = block.GetColor(Shader.PropertyToID("_RendererColor")).a;
            float mirroredScale = proxy.transform.localScale.x;
            proxy.SetPose(Vector3.one, Quaternion.identity, new Vector3(2f, 3f, 1f));
            float repeatedScale = proxy.transform.localScale.x;
            caster.x = 7f;
            bool movedFootUpdated = proxy.UpdateProperties(block, caster, uv);
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
            bool recycledProperties = proxy.UpdateProperties(block, caster, uv);
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

    // 采样正式代理的源分辨率、图集换帧、静态复用与归池，不复制柔化计算。
    private static JObject ShadowProxyBlurScale(JObject args)
    {
        float pixelsPerUnit = (float?)args["pixelsPerUnit"] ?? 1254f;
        var texture = new Texture2D(16, 8) { filterMode = FilterMode.Point };
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), Vector2.one * 0.5f,
            pixelsPerUnit, 0, SpriteMeshType.FullRect);
        Sprite nextFrame = Sprite.Create(texture, new Rect(8f, 0f, 8f, 8f), Vector2.one * 0.5f,
            16f, 0, SpriteMeshType.FullRect);
        WorldShadowProjectionManager.ShadowProxy proxy = null;
        try
        {
            proxy = new WorldShadowProjectionManager.ShadowProxy();
            proxy.gameObject.hideFlags = HideFlags.HideAndDontSave;
            proxy.gameObject.SetActive(false);
            var block = new MaterialPropertyBlock();
            Vector4 caster = new(0f, 1f, 1f, 1f), uv = new(0f, 0f, 0.5f, 1f);
            int texelId = Shader.PropertyToID("_SunShadowTexelSize");
            proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool initialUpdated = proxy.UpdateProperties(block, caster, uv);
            proxy.MeshRenderer.GetPropertyBlock(block);
            Vector4 texel = block.GetVector(texelId);
            bool textureBound = block.GetTexture(Shader.PropertyToID("_MainTex")) == texture;
            Bounds meshBounds = proxy.gameObject.GetComponent<MeshFilter>().sharedMesh.bounds;
            float paddingX = meshBounds.max.x - sprite.bounds.max.x;
            float paddingY = meshBounds.max.y - sprite.bounds.max.y;
            bool repeatedSource = proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool repeatedProperties = proxy.UpdateProperties(block, caster, uv);

            proxy.SetSource(nextFrame, SpriteDrawMode.Simple, Vector2.one, true, false, 1f);
            bool frameUpdated = proxy.UpdateProperties(block, caster, new Vector4(0.5f, 0f, 1f, 1f));
            proxy.MeshRenderer.GetPropertyBlock(block);
            float nextScale = block.GetVector(texelId).z;
            Vector4 nextUv = block.GetVector(Shader.PropertyToID("_SunShadowUvBounds"));
            proxy.ResetSource();
            proxy.SetSource(sprite, SpriteDrawMode.Simple, Vector2.one, false, false, 1f);
            bool recycledUpdated = proxy.UpdateProperties(block, caster, uv);
            proxy.MeshRenderer.GetPropertyBlock(block);
            return new JObject
            {
                ["initialUpdated"] = initialUpdated, ["texelX"] = texel.x, ["texelY"] = texel.y,
                ["scale"] = texel.z, ["paddingX"] = paddingX, ["paddingY"] = paddingY,
                ["textureBound"] = textureBound, ["pointFilterPreserved"] = texture.filterMode == FilterMode.Point,
                ["repeatedSource"] = repeatedSource, ["repeatedProperties"] = repeatedProperties,
                ["frameUpdated"] = frameUpdated, ["nextScale"] = nextScale,
                ["nextUvMinX"] = nextUv.x, ["nextUvMaxX"] = nextUv.z,
                ["recycledUpdated"] = recycledUpdated, ["recycledScale"] = block.GetVector(texelId).z
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
