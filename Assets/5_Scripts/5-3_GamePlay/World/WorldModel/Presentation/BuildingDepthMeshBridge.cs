using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

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
        internal readonly bool OriginalForceOff;
        internal readonly int Part;
        internal bool Submitted, Highlighted;
        internal Sprite LastSprite;
        internal Source(SpriteRenderer renderer, int part)
        { Renderer = renderer; OriginalForceOff = renderer.forceRenderingOff; Part = part; }
    }

    private sealed class Binding
    {
        internal readonly Item Item;
        private readonly Mod_Building building;
        private readonly List<Source> sources = new();
        private readonly MaterialPropertyBlock properties = new();
        private readonly int entityId;
        private ChunkView view;
        private ChunkTilemapRenderer owner;

        internal Binding(Item item, Mod_Building building)
        {
            Item = item; this.building = building; entityId = item.GetInstanceID();
            var renderers = new List<SpriteRenderer>();
            item.GetComponentsInChildren(true, renderers);
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer.gameObject.name == "Interaction Outline" ||
                    renderer.GetComponentInParent<Item>() != item || renderer.GetComponentInParent<Canvas>() != null) continue;
                sources.Add(new Source(renderer, sources.Count));
            }
        }

        internal void Present()
        {
            if (building == null || !Item.isActiveAndEnabled || Item.InHand || !building.IsInstalled() ||
                ChunkMgr.ExistingInstance == null ||
                !ChunkMgr.ExistingInstance.TryGetRuntimeChunkView(Item.transform.position, out ChunkView currentView))
            { Release(); return; }
            // 多部件建筑整件选择后端，不能把一个未知 MOD 图层和主体拆到两个同深度排序单元。
            foreach (Source source in sources)
            {
                SpriteRenderer renderer = source.Renderer;
                if (renderer == null || !renderer.enabled || renderer.sprite == null || source.OriginalForceOff) continue;
                if (renderer.drawMode != SpriteDrawMode.Simple || renderer.maskInteraction != SpriteMaskInteraction.None ||
                    Kind(renderer.sharedMaterial) == ShaderKind.Unsupported)
                { Release(); return; }
            }
            if (view != currentView || owner == null)
            {
                Release();
                view = currentView;
                owner = currentView.GetComponentInChildren<ChunkTilemapRenderer>(true);
            }
            if (owner == null || !owner.IsBatchPresentationRegistered) { Release(); return; }
            int baseOrder = Item.Sprite != null ? Item.Sprite.sortingOrder : 0;
            bool shadowChanged = false;
            foreach (Source source in sources)
            {
                SpriteRenderer renderer = source.Renderer;
                if (renderer == null || source.OriginalForceOff || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy || renderer.sprite == null || renderer.sharedMaterial == null)
                { Remove(source); continue; }
                Material material = renderer.sharedMaterial;
                ShaderKind shader = Kind(material);
                if (shader == ShaderKind.Unsupported || renderer.drawMode != SpriteDrawMode.Simple)
                { Remove(source); continue; }
                properties.Clear();
                renderer.GetPropertyBlock(properties);
                Sprite sprite = renderer.sprite;
                SharedSpriteMeshCache.Geometry geometry = SharedSpriteMeshCache.GetGeometry(sprite);
                Vector4 crop = Vector4.zero;
                if (shader == ShaderKind.Emissive)
                    crop = new Vector4(Float(material, ClipLocalId, .38f), 0f, 0f, 1f);
                else
                {
                    float amount = Float(material, BodyClipId, 0f);
                    if (amount > 0f)
                        crop = new Vector4(Mathf.Lerp(Float(material, BodyMinId, geometry.Bounds.min.y),
                            Float(material, BodyMaxId, geometry.Bounds.max.y), amount), 0f, 0f, 1f);
                }
                Vector4 state = new(Float(material, HitFlashId, 0f), Float(material, SnowId, 0f),
                    Float(material, DissolveId, 0f), 1f);
                Vector3 flip = new(renderer.flipX ? -1f : 1f, renderer.flipY ? -1f : 1f, 1f);
                owner.DepthMesh.Set(ChunkDepthMeshRenderer.BuildingDomain, entityId, source.Part,
                    sprite, material, renderer.transform.localToWorldMatrix * Matrix4x4.Scale(flip),
                    Item.transform.position, renderer.sortingOrder - baseOrder, renderer.color, crop,
                    occluder: Float(material, OccluderId, 0f) > .5f, highlighted: source.Highlighted, state: state);
                if (renderer == Item.Sprite && (!source.Submitted || source.LastSprite != sprite)) shadowChanged = true;
                source.LastSprite = sprite;
                source.Submitted = true;
                projected[renderer] = source;
                renderer.forceRenderingOff = true;
            }
            if (shadowChanged) building.RefreshLightOcclusion();
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
            projected.Remove(source.Renderer);
            if (source.Renderer != null) source.Renderer.forceRenderingOff = source.OriginalForceOff;
            source.Submitted = false;
            source.Highlighted = false;
        }
        internal void Release()
        {
            bool restoreShadow = Item != null && IsProjected(Item.Sprite);
            foreach (Source source in sources) Remove(source);
            owner = null; view = null;
            if (restoreShadow && Item != null && !Item.DestructionHandled && building != null && building.isActiveAndEnabled)
                building.RefreshLightOcclusion();
        }
    }

    /// <summary>未知 MOD Shader 和九宫格保留原表现，不把不支持的材质悄悄换成错误的 Lit Shader。</summary>
    private static ShaderKind Kind(Material material)
    {
        if (material == null || material.shader == null) return ShaderKind.Unsupported;
        Shader shader = material.shader;
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
