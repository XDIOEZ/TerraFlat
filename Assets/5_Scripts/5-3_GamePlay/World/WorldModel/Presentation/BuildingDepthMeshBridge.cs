using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>尚由 Item 驱动的门、帐篷等只更换绘制后端；模块、碰撞、库存和存档不迁移到网格里。</summary>
internal static class BuildingDepthMeshBridge
{
    #region 注册与回收
    private static readonly Dictionary<Item, Binding> bindings = new();
    private static readonly Dictionary<SpriteRenderer, Source> projected = new();
    private static readonly List<Binding> scratch = new();
    private static readonly ProfilerMarker PresentMarker = new("FlatWorld.DepthMesh.BuildingBridge");
    private static bool hooked;
    private enum ShaderKind { Unsupported, Lit, Unlit, Emissive }
    private static readonly Dictionary<Shader, ShaderKind> shaderKinds = new();
    private static readonly int BodyClipId = Shader.PropertyToID("_BodyClip");
    private static readonly int BodyMinId = Shader.PropertyToID("_BodyMinV");
    private static readonly int BodyMaxId = Shader.PropertyToID("_BodyMaxV");
    private static readonly int ClipLocalId = Shader.PropertyToID("_ClipLocalY");
    private static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
    private static readonly int SnowId = Shader.PropertyToID("_SnowCoverage");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int OccluderId = Shader.PropertyToID("_PlayerOccluder");
    internal static bool IsProjected(SpriteRenderer source) => source != null && projected.ContainsKey(source);
    internal static void SetHighlighted(SpriteRenderer source, bool value)
    {
        if (source != null && projected.TryGetValue(source, out Source entry)) entry.Highlighted = value;
    }

    internal static void Register(Item item)
    {
        if (item == null) return;
        Unregister(item);
        Mod_Building building = item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
        if (building?.Data?.Role != BuildingRole.PlacedBuilding || item.Sprite == null) return;
        // 移动车辆不是静态格子建筑，继续由自己的动态表现控制。
        if (item.GetComponentInChildren<Mod_Carrier>(true) != null) return;
        if (!hooked)
        {
            hooked = true;
            ItemMgr.RuntimeItemUnregistered += Unregister;
            Item.RuntimeStructureChanged += Register;
            ChunkDepthMeshRenderer.BeforeFlush += Present;
        }
        ChunkDepthMeshRenderer.EnsureDispatcher();
        bindings.Add(item, new Binding(item, building));
    }

    // 贴地设施沿用原材质队列，其余排序键可由行网格保留。
    internal static bool TryResolveConfiguredDepthSorting(SpriteRenderer renderer, out bool groundSorting)
    {
        WorldSortingManager.ReadExternalRendererKey(renderer, out int layer, out int order);
        WorldRenderingManager rendering = WorldRenderingManager.Instance;
        rendering.GetSortingKey(WorldSortingManager.GroundBuildingCategory, out int groundLayer, out int groundOrder);
        groundSorting = layer == groundLayer && order == groundOrder;
        return true;
    }

    private static void Unregister(Item item)
    {
        if (ReferenceEquals(item, null) || !bindings.Remove(item, out Binding binding)) return;
        binding.Release();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        foreach (Binding binding in bindings.Values) binding.Release();
        bindings.Clear(); projected.Clear(); scratch.Clear(); shaderKinds.Clear();
        ItemMgr.RuntimeItemUnregistered -= Unregister;
        Item.RuntimeStructureChanged -= Register;
        ChunkDepthMeshRenderer.BeforeFlush -= Present;
        hooked = false;
    }

    private static void Present()
    {
        using (PresentMarker.Auto())
        {
            scratch.Clear(); scratch.AddRange(bindings.Values);
            foreach (Binding binding in scratch)
            {
                if (binding.Item == null || binding.Item.DestructionHandled) Unregister(binding.Item);
                else binding.Present();
            }
            scratch.Clear();
        }
    }
    #endregion

    #region 原有模块输出的只读映射
    private sealed class Source
    {
        internal readonly SpriteRenderer Renderer;
        internal readonly Transform Transform;
        internal readonly bool OriginalForceOff;
        internal readonly bool LogicalForceOff;
        internal readonly int Part;
        internal bool Submitted, Highlighted;
        internal Sprite LastSprite;
        internal Sprite FrameSprite;
        internal Material FrameMaterial;
        internal ShaderKind FrameShader;
        internal ChunkDepthMeshRenderer SubmittedRenderer;
        internal Source(SpriteRenderer renderer, int part)
        {
            Renderer = renderer; Transform = renderer.transform; OriginalForceOff = renderer.forceRenderingOff;
            LogicalForceOff = GameplayWorldRegistry.GetPresentationOriginalForceRenderingOff(renderer); Part = part;
        }
    }

    private sealed class Binding
    {
        internal readonly Item Item;
        private readonly Mod_Building building;
        private readonly List<Source> sources = new();
        private readonly HashSet<SortingGroup> nestedSortingGroups = new();
        private readonly SortingGroup rootSortingGroup;
        private readonly MaterialPropertyBlock properties = new();
        private readonly int entityId;
        private readonly int groundLayer, groundOrder;
        private readonly bool connectionLine;
        private ChunkView view;
        private ChunkTilemapRenderer owner;
        private bool hasSubmittedSources;
        private readonly CombustionVisualEffect[] combustionEffects;
        private SortingGroup combustionSortingGroup;
        private bool ownsCombustionSortingGroup, combustionSortingOverridden, originalCombustionSortingEnabled;
        private int originalCombustionSortingLayer, originalCombustionSortingOrder;

        internal Binding(Item item, Mod_Building building)
        {
            Item = item; this.building = building; entityId = item.GetInstanceID();
            rootSortingGroup = item.GetComponent<SortingGroup>();
            WorldSortingManager.GetResourceSortingKey(WorldSortingManager.GroundBuildingCategory, out groundLayer, out groundOrder);
            combustionEffects = item.GetComponentsInChildren<CombustionVisualEffect>(true);
            connectionLine = item.GetComponentInChildren<Mod_ShipPart>(true)?.Configuration.Kind == FlatWorld.Spaceflight.ShipPieceKind.Fiber;
            var renderers = new List<SpriteRenderer>();
            item.GetComponentsInChildren(true, renderers);
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer.gameObject.name == "Interaction Outline" ||
                    renderer.GetComponentInParent<Item>() != item || renderer.GetComponentInParent<Canvas>() != null) continue;
                sources.Add(new Source(renderer, sources.Count));
                for (Transform current = renderer.transform; current != null && current != item.transform; current = current.parent)
                {
                    SortingGroup group = current.GetComponent<SortingGroup>();
                    if (group != null) nestedSortingGroups.Add(group);
                }
            }
        }

        internal void Present()
        {
            string worldKey = GameplayWorldRegistry.GetItemWorldKey(Item);
            using var world = GameplayWorldRegistry.TryGet(worldKey, out _) ? GameplayWorldRegistry.UseWorld(worldKey) : null;
            Vector3 anchor = Item.transform.position;
            if (building == null || !Item.isActiveAndEnabled || Item.InHand || !building.IsInstalled() ||
                ChunkMgr.ExistingInstance == null ||
                !ChunkMgr.ExistingInstance.TryGetRuntimeChunkView(anchor, out ChunkView currentView))
            { Release(); return; }
            if (view != currentView || owner == null)
            {
                Release();
                view = currentView;
                owner = currentView.GetComponentInChildren<ChunkTilemapRenderer>(true);
            }
            if (owner == null || !owner.IsBatchPresentationRegistered) { Release(); return; }
            bool submerged = FlatWorld.Spaceflight.SpaceSession.Current?.Owns(Item.itemData.Guid) != true &&
                owner.IsBuildingSubmergedAtWorldAnchor(anchor);
            if (!submerged)
            {
                // 嵌套排序组不能被一层行网格表达，整件保留原生层级。
                if (RequiresNativeSortingHierarchy())
                {
                    if (hasSubmittedSources) ReleaseSources();
                    return;
                }
            }
            UpdateCombustionWaterSurfacePresentation(submerged);
            WorldSortingManager.ReadExternalRendererKey(Item.Sprite, out int sortingLayerId, out int sortingOrder);
            if (sortingLayerId == groundLayer && sortingOrder == groundOrder && !submerged)
            {
                if (hasSubmittedSources) ReleaseSources();
                return;
            }
            // 多部件建筑整件选择后端，不能把一个未知 MOD 图层和主体拆到两个同深度排序单元。
            foreach (Source source in sources)
            {
                source.FrameSprite = null;
                source.FrameMaterial = null;
                SpriteRenderer renderer = source.Renderer;
                if (renderer == null || !renderer.enabled || source.LogicalForceOff) continue;
                Sprite sprite = renderer.sprite;
                if (sprite == null) continue;
                Material material = renderer.sharedMaterial;
                ShaderKind shader = Kind(material);
                if (renderer.drawMode != SpriteDrawMode.Simple || renderer.maskInteraction != SpriteMaskInteraction.None ||
                    shader == ShaderKind.Unsupported)
                { Release(); return; }
                if (!renderer.gameObject.activeInHierarchy) continue;
                // 同步提交期间复用本次读取，保留逐帧换图、材质和 MPB 的实时变化。
                source.FrameSprite = sprite;
                source.FrameMaterial = material;
                source.FrameShader = shader;
            }
            SpriteRenderer mainSprite = Item.Sprite;
            int baseOrder = mainSprite != null ? mainSprite.sortingOrder : 0;
            bool shadowChanged = false;
            foreach (Source source in sources)
            {
                SpriteRenderer renderer = source.Renderer;
                Sprite sprite = source.FrameSprite;
                if (sprite == null)
                { Remove(source); continue; }
                Material material = source.FrameMaterial;
                ShaderKind shader = source.FrameShader;
                properties.Clear();
                renderer.GetPropertyBlock(properties);
                Vector4 crop = Vector4.zero;
                if (shader == ShaderKind.Emissive)
                    crop = new Vector4(Float(material, ClipLocalId, .38f), 0f, 0f, 1f);
                else
                {
                    float amount = Float(material, BodyClipId, 0f);
                    if (amount > 0f)
                    {
                        SharedSpriteMeshCache.Geometry geometry = SharedSpriteMeshCache.GetGeometry(sprite);
                        crop = new Vector4(Mathf.Lerp(Float(material, BodyMinId, geometry.Bounds.min.y),
                            Float(material, BodyMaxId, geometry.Bounds.max.y), amount), 0f, 0f, 1f);
                    }
                }
                Vector4 state = new(Float(material, HitFlashId, 0f), Float(material, SnowId, 0f),
                    Float(material, DissolveId, 0f), 1f);
                Vector3 flip = new(renderer.flipX ? -1f : 1f, renderer.flipY ? -1f : 1f, 1f);
                ChunkDepthMeshRenderer rendererBackend = owner.GetBuildingDepthRenderer(submerged, connectionLine && submerged);
                if (source.SubmittedRenderer != rendererBackend)
                    source.SubmittedRenderer?.Remove(ChunkDepthMeshRenderer.BuildingDomain, entityId, source.Part);
                source.SubmittedRenderer = rendererBackend;
                rendererBackend.Set(ChunkDepthMeshRenderer.BuildingDomain, entityId, source.Part,
                    sprite, material, source.Transform.localToWorldMatrix * Matrix4x4.Scale(flip),
                    anchor, renderer.sortingOrder - baseOrder - (submerged && connectionLine ? 1024 : 0), renderer.color, crop,
                    occluder: Float(material, OccluderId, 0f) > .5f, highlighted: source.Highlighted, state: state,
                    sortingLayerId: submerged ? null : sortingLayerId, sortingOrder: sortingOrder);
                if (renderer == mainSprite && (!source.Submitted || source.LastSprite != sprite)) shadowChanged = true;
                source.LastSprite = sprite;
                source.Submitted = true;
                hasSubmittedSources = true;
                projected[renderer] = source;
                renderer.forceRenderingOff = true;
            }
            if (shadowChanged) building.RefreshLightOcclusion();
        }

        private bool RequiresNativeSortingHierarchy()
        {
            SortingGroup single = null;
            foreach (SortingGroup group in nestedSortingGroups)
            {
                if (group == null || !group.enabled || !group.gameObject.activeInHierarchy) continue;
                if (single != null || rootSortingGroup != null && rootSortingGroup.enabled) return true;
                single = group;
            }
            if (single == null) return false;
            foreach (Source source in sources)
                if (source.Renderer != null && source.Renderer.enabled && !source.LogicalForceOff &&
                    source.Renderer.gameObject.activeInHierarchy && !source.Transform.IsChildOf(single.transform)) return true;
            return false;
        }

        private float Float(Material material, int id, float fallback)
        {
            if (properties.HasProperty(id)) return properties.GetFloat(id);
            return material.HasProperty(id) ? material.GetFloat(id) : fallback;
        }

        private void Remove(Source source)
        {
            if (!source.Submitted) return;
            owner?.RemoveBuildingDepthPart(entityId, source.Part);
            if (connectionLine) owner?.RemoveBuildingConnectionDepthPart(entityId, source.Part);
            projected.Remove(source.Renderer);
            if (source.Renderer != null)
                source.Renderer.forceRenderingOff = WorldPresentationProjection.IsSourceProjected(source.Renderer) ||
                    GameplayWorldRegistry.GetPresentationRequiredForceRenderingOff(source.Renderer, source.OriginalForceOff);
            source.Submitted = false;
            source.SubmittedRenderer = null;
            source.Highlighted = false;
        }
        internal void Release()
        {
            ReleaseSources();
            owner = null; view = null;
        }

        private void ReleaseSources()
        {
            UpdateCombustionWaterSurfacePresentation(false);
            bool restoreShadow = Item != null && IsProjected(Item.Sprite);
            foreach (Source source in sources) Remove(source);
            hasSubmittedSources = false;
            if (restoreShadow && Item != null && !Item.DestructionHandled && building != null && building.isActiveAndEnabled)
                building.RefreshLightOcclusion();
        }

        // 附属火焰保留原宿主生命周期，只临时调整整件分组与共享材质的水面层。
        private void UpdateCombustionWaterSurfacePresentation(bool submerged)
        {
            if (combustionEffects.Length == 0) return;
            if (submerged && !combustionSortingOverridden)
            {
                combustionSortingGroup = Item.GetComponent<SortingGroup>();
                if (combustionSortingGroup == null)
                {
                    combustionSortingGroup = Item.gameObject.AddComponent<SortingGroup>();
                    combustionSortingGroup.sortAtRoot = true;
                    WorldPresentationProjection.NotifySourceSortingStructureChanged(Item.transform);
                    ownsCombustionSortingGroup = true;
                }
                originalCombustionSortingLayer = combustionSortingGroup.sortingLayerID;
                originalCombustionSortingOrder = combustionSortingGroup.sortingOrder;
                originalCombustionSortingEnabled = !ownsCombustionSortingGroup && combustionSortingGroup.enabled;
                combustionSortingGroup.enabled = true;
                combustionSortingGroup.sortingLayerID = SortingLayer.NameToID(WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer);
                combustionSortingGroup.sortingOrder = 0;
                combustionSortingOverridden = true;
            }
            else if (!submerged && combustionSortingOverridden)
            {
                if (combustionSortingGroup != null)
                {
                    combustionSortingGroup.sortingLayerID = originalCombustionSortingLayer;
                    combustionSortingGroup.sortingOrder = originalCombustionSortingOrder;
                    combustionSortingGroup.enabled = originalCombustionSortingEnabled;
                }
                combustionSortingOverridden = false;
            }
            foreach (CombustionVisualEffect effect in combustionEffects)
                if (effect != null) effect.SetFlamePresentationMaterialOverride(submerged && owner != null
                    ? owner.ResolveBuildingOverlayMaterial(effect.FlameSourceMaterial, true) : null);
        }
    }

    /// <summary>未知 MOD Shader 和九宫格保留原表现，不把不支持的材质悄悄换成错误的 Lit Shader。</summary>
    private static ShaderKind Kind(Material material)
    {
        if (material == null) return ShaderKind.Unsupported;
        Shader shader = material.shader;
        if (shader == null) return ShaderKind.Unsupported;
        if (shaderKinds.TryGetValue(shader, out ShaderKind kind)) return kind;
        kind = shader.name switch
        {
            "Game/2D/Sprite-Lit-Master" or "Universal Render Pipeline/2D/Sprite-Lit-Default" => ShaderKind.Lit,
            "Sprites/Default" or "Universal Render Pipeline/2D/Sprite-Unlit-Default" => ShaderKind.Unlit,
            "Game/2D/Emissive-Sprite-Overlay" => ShaderKind.Emissive,
            _ => ShaderKind.Unsupported
        };
        shaderKinds.Add(shader, kind);
        return kind;
    }
    #endregion
}
