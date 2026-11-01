using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed partial class WorldRenderingManager
{
    #region 生物水面倒影

    private sealed class ActorWaterReflectionView
    {
        internal Item Owner;
        internal uint Generation;
        internal GameObject Root;
        internal SpriteRenderer Renderer;
        internal MaterialPropertyBlock Properties;
        internal WaterImmersionRenderEffect Immersion;
        internal bool InWater;
    }

    private static readonly int ReflectionDataId = Shader.PropertyToID("_ReflectionData");
    private const string ReflectionShaderName = "FlatWorld/2D/Actor Water Reflection";
    private readonly Dictionary<Item, ActorWaterReflectionView> actorWaterReflections =
        new(ItemReferenceComparer.Instance);
    private readonly HashSet<ActorWaterReflectionView> activeActorWaterReflections = new();
    private Material sharedActorWaterReflectionMaterial;

    /// <summary>真实可游泳水体通知表现层；退出水面时只隐藏，回收交给物品生命周期。</summary>
    public void SetActorWaterReflection(Item item, bool inWater)
    {
        if (item == null) return;
        if (!inWater)
        {
            if (actorWaterReflections.TryGetValue(item, out ActorWaterReflectionView inactive))
            {
                inactive.InWater = false;
                activeActorWaterReflections.Remove(inactive);
                if (inactive.Renderer != null) inactive.Renderer.enabled = false;
            }
            return;
        }
        if (!IsItemRenderingRegistered(item) && !RegisterItemRendering(item)) return;
        if (!actorWaterReflections.TryGetValue(item, out ActorWaterReflectionView view))
        {
            if (!IsReflectableActor(item)) return;
            view = CreateActorWaterReflection(item);
        }
        if (view == null) return;
        view.InWater = true;
        activeActorWaterReflections.Add(view);
    }

    private static bool IsReflectableActor(Item item)
    {
        if (item == null || item.Sprite == null || item.IsInPool || item.InHand || item.DestructionHandled)
            return false;
        if (item is Player) return true;
        if (item.itemData == null || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition))
            return false;
        // 水下的鱼本体已交给独立水下排序，不额外投一层水面镜像。
        return definition.IsActor && item.GetComponentInChildren<Mod_AquaticMovement>(true) == null;
    }

    private void RefreshActorWaterReflection(Item item)
    {
        if (item != null && actorWaterReflections.TryGetValue(item, out ActorWaterReflectionView view))
            view.Immersion = item.GetComponentInChildren<WaterImmersionRenderEffect>(true);
    }

    private ActorWaterReflectionView CreateActorWaterReflection(Item item)
    {
        if (sharedActorWaterReflectionMaterial == null)
        {
            Shader shader = Resources.Load<Shader>("Shaders/ActorWaterReflection");
            if (shader == null) throw new InvalidOperationException("缺少生物水面倒影 Shader：" + ReflectionShaderName);
            int waterQueue = -1;
            foreach (var profile in WorldRenderingConfigCatalog.Default.sorting.batchProfiles)
                if (profile.visualLayer == "Water")
                    waterQueue = WorldRenderingConfigCatalog.Default.sorting.batchRenderQueueBase + profile.renderQueueOffset;
            if (waterQueue < 0) throw new InvalidOperationException("缺少 Water 渲染队列。");
            sharedActorWaterReflectionMaterial = new Material(shader)
            {
                name = "ActorWaterReflection (Shared)",
                renderQueue = waterQueue + 1,
                hideFlags = HideFlags.HideAndDontSave
            };
        }
        Transform parent = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(item.gameObject.scene,
            RuntimeSceneObjectCategory.Rendering);
        var root = new GameObject(item.name + " WaterReflection") { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(parent, false);
        SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
        renderer.sharedMaterial = sharedActorWaterReflectionMaterial;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = 1;
        renderer.enabled = false;
        var view = new ActorWaterReflectionView
        {
            Owner = item, Generation = item.RuntimeGeneration, Root = root,
            Renderer = renderer, Properties = new MaterialPropertyBlock(),
            Immersion = item.GetComponentInChildren<WaterImmersionRenderEffect>(true)
        };
        actorWaterReflections.Add(item, view);
        return view;
    }

    /// <summary>跟随真正的显示 Sprite，而不是逻辑节点；动画换帧、翻面和镜头渲染同步发生。</summary>
    private void PresentActorWaterReflections(ScriptableRenderContext context, List<Camera> cameras)
    {
        using var scope = UnityEngine.Pool.ListPool<ActorWaterReflectionView>.Get(out var views);
        views.AddRange(activeActorWaterReflections);
        foreach (ActorWaterReflectionView view in views)
        {
            Item item = view.Owner;
            if (item == null || item.IsInPool || item.DestructionHandled ||
                item.RuntimeGeneration != view.Generation || view.Root == null)
            {
                ReleaseActorWaterReflection(item);
                continue;
            }
            SpriteRenderer source = item.Sprite;
            bool visible = view.InWater && source != null && source.sprite != null &&
                source.enabled && source.gameObject.activeInHierarchy && item.isActiveAndEnabled &&
                GameplayWorldRegistry.GetItemWorldKey(item) == GameplayWorldRegistry.LocalPresentationWorldKey;
            if (!visible)
            {
                view.Renderer.enabled = false;
                continue;
            }
            if (view.Root.scene != item.gameObject.scene)
            {
                Transform root = view.Root.transform;
                root.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(view.Root, item.gameObject.scene);
                root.SetParent(RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(item.gameObject.scene,
                    RuntimeSceneObjectCategory.Rendering), true);
            }
            Bounds bounds = source.bounds;
            if (bounds.size.y < 0.001f)
            {
                view.Renderer.enabled = false;
                continue;
            }
            WaterImmersionRenderEffect immersion = view.Immersion;
            float waterY = immersion != null && immersion.HasWorldWaterReference
                ? immersion.CurrentWaterY : bounds.min.y + bounds.size.y * 0.3f;
            float blend = immersion != null ? immersion.CurrentBlend : 1f;
            Vector3 sourcePosition = source.transform.position +
                GetVisualMotionOffsetForReflection(item, source.sprite);
            Transform reflection = view.Root.transform;
            reflection.SetPositionAndRotation(
                new Vector3(sourcePosition.x, waterY * 2f - sourcePosition.y, sourcePosition.z),
                Quaternion.Euler(0f, 0f, -source.transform.eulerAngles.z));
            Vector3 scale = source.transform.lossyScale;
            reflection.localScale = new Vector3(scale.x, -scale.y, scale.z);
            view.Renderer.sprite = source.sprite;
            view.Renderer.color = source.color;
            view.Renderer.flipX = source.flipX;
            view.Renderer.flipY = source.flipY;
            view.Properties.SetVector(ReflectionDataId,
                new Vector4(waterY, Mathf.Max(0.01f, bounds.max.y - waterY), blend, 0f));
            view.Renderer.SetPropertyBlock(view.Properties);
            view.Renderer.enabled = blend > 0.001f;
        }
    }

    private void ReleaseActorWaterReflection(Item item)
    {
        if (ReferenceEquals(item, null) || !actorWaterReflections.Remove(item, out ActorWaterReflectionView view))
            return;
        activeActorWaterReflections.Remove(view);
        if (view.Root != null)
        {
            if (Application.isPlaying) Destroy(view.Root);
            else DestroyImmediate(view.Root);
        }
    }

    private void ClearActorWaterReflections()
    {
        using var scope = UnityEngine.Pool.ListPool<Item>.Get(out var owners);
        owners.AddRange(actorWaterReflections.Keys);
        foreach (Item owner in owners) ReleaseActorWaterReflection(owner);
        activeActorWaterReflections.Clear();
        if (sharedActorWaterReflectionMaterial != null)
        {
            if (Application.isPlaying) Destroy(sharedActorWaterReflectionMaterial);
            else DestroyImmediate(sharedActorWaterReflectionMaterial);
            sharedActorWaterReflectionMaterial = null;
        }
    }

    #endregion
}
