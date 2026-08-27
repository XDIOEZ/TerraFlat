using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>把运行时定义应用到共享外壳实例。</summary>
public static class ItemDefinitionRuntime
{
    public static void ConfigureInstance(GameRes gameRes, RuntimeItemDefinition definition, Item item, ItemData itemData)
    {
        if (gameRes == null || definition == null || item == null || itemData == null)
            return;

        item.BindData(itemData);
        item.gameObject.name = definition.Id;
        ApplyVisual(definition, item);
        EnsureModuleComponents(gameRes, definition, item, itemData);
    }

    private static void ApplyVisual(RuntimeItemDefinition definition, Item item)
    {
        ItemVisualDefinitionDto visual = definition.Visual;
        if (visual == null)
            return;

        bool needsRenderer = definition.Sprite != null ||
                             visual.RendererLocalPosition.HasValue ||
                             visual.RendererLocalEulerAngles.HasValue ||
                             visual.RendererLocalScale.HasValue ||
                             visual.Color.HasValue ||
                             visual.FlipX.HasValue ||
                             visual.FlipY.HasValue ||
                             !string.IsNullOrWhiteSpace(visual.SortingLayerName) ||
                             visual.SortingOrder.HasValue;
        SpriteRenderer renderer = null;
        if (needsRenderer && !string.IsNullOrWhiteSpace(definition.RendererPath))
        {
            Transform target = item.transform.Find(definition.RendererPath);
            renderer = target != null ? target.GetComponent<SpriteRenderer>() : null;
        }
        if (needsRenderer)
        {
            renderer ??= item.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(candidate => candidate.sprite != null);
            renderer ??= item.GetComponentInChildren<SpriteRenderer>(true);
            if (renderer == null)
                throw new MissingComponentException($"物品 {definition.Id} 的外壳缺少 SpriteRenderer");

            if (definition.Sprite != null)
                renderer.sprite = definition.Sprite;
            // 世界物品统一以 Sprite 导入 Pivot 作为透明排序锚点，通用外壳不得退回几何中心排序。
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            if (visual.RendererLocalPosition.HasValue)
                renderer.transform.localPosition = visual.RendererLocalPosition.Value;
            if (visual.RendererLocalEulerAngles.HasValue)
                renderer.transform.localEulerAngles = visual.RendererLocalEulerAngles.Value;
            if (visual.RendererLocalScale.HasValue)
                renderer.transform.localScale = visual.RendererLocalScale.Value;
            if (visual.Color.HasValue)
                renderer.color = visual.Color.Value;
            if (visual.FlipX.HasValue)
                renderer.flipX = visual.FlipX.Value;
            if (visual.FlipY.HasValue)
                renderer.flipY = visual.FlipY.Value;
            if (!string.IsNullOrWhiteSpace(visual.SortingLayerName))
                renderer.sortingLayerName = visual.SortingLayerName;
            if (visual.SortingOrder.HasValue)
                renderer.sortingOrder = visual.SortingOrder.Value;

            item.Sprite = renderer;
        }

        ApplyAnimator(item, visual.AnimatorPath, definition.AnimatorController, visual.AnimationState);
        ApplyCollider(item, visual.Collider);
    }

    /// <summary>绑定 AnimatorController，并按 JSON 指定状态初始化动画机。</summary>
    private static void ApplyAnimator(
        Item item,
        string animatorPath,
        RuntimeAnimatorController controller,
        string animationState)
    {
        if (controller == null)
            return;

        Animator animator = null;
        if (!string.IsNullOrWhiteSpace(animatorPath))
        {
            Transform target = item.transform.Find(animatorPath);
            animator = target != null ? target.GetComponent<Animator>() : null;
        }
        animator ??= item.GetComponentInChildren<Animator>(true);
        if (animator == null)
            throw new MissingComponentException($"物品 {item.itemData?.IDName} 的外壳缺少 Animator");

        animator.runtimeAnimatorController = controller;
        if (!string.IsNullOrWhiteSpace(animationState))
            PlayInitialAnimatorState(item, animator, animationState);
    }

    /// <summary>直接按状态机状态名播放初始动画，不依赖 Sprite 子资源切片。</summary>
    private static void PlayInitialAnimatorState(Item item, Animator animator, string animationState)
    {
        string stateName = animationState.Trim();
        if (animator.layerCount <= 0)
            throw new InvalidOperationException($"物品 {item.itemData?.IDName} 的 Animator 没有可用状态层");

        int layer = 0;
        int stateHash = Animator.StringToHash(stateName);
        if (!animator.HasState(layer, stateHash))
        {
            string fullPath = $"{animator.GetLayerName(layer)}.{stateName}";
            stateHash = Animator.StringToHash(fullPath);
            if (!animator.HasState(layer, stateHash))
            {
                throw new InvalidDataException(
                    $"物品 {item.itemData?.IDName} 的 Animator 找不到状态：{stateName}");
            }
        }

        animator.Play(stateHash, layer, 0f);
        animator.Update(0f);
    }

    private static void ApplyCollider(Item item, ItemColliderDefinitionDto definition)
    {
        if (definition == null)
            return;

        Transform target = string.IsNullOrWhiteSpace(definition.Path)
            ? item.transform
            : item.transform.Find(definition.Path);
        if (target == null)
            throw new MissingComponentException($"物品 {item.itemData?.IDName} 找不到碰撞体路径：{definition.Path}");

        Collider2D collider = definition.Type switch
        {
            nameof(BoxCollider2D) => target.GetComponent<BoxCollider2D>(),
            nameof(CircleCollider2D) => target.GetComponent<CircleCollider2D>(),
            nameof(CapsuleCollider2D) => target.GetComponent<CapsuleCollider2D>(),
            nameof(PolygonCollider2D) => target.GetComponent<PolygonCollider2D>(),
            _ => target.GetComponent<Collider2D>()
        };
        if (collider == null)
            throw new MissingComponentException(
                $"物品 {item.itemData?.IDName} 的外壳缺少碰撞体 {definition.Type}（{definition.Path}）");

        if (definition.Enabled.HasValue) collider.enabled = definition.Enabled.Value;
        if (definition.IsTrigger.HasValue) collider.isTrigger = definition.IsTrigger.Value;
        if (definition.Offset.HasValue) collider.offset = definition.Offset.Value;

        switch (collider)
        {
            case BoxCollider2D box when definition.Size.HasValue:
                box.size = definition.Size.Value;
                if (definition.EdgeRadius.HasValue)
                    box.edgeRadius = definition.EdgeRadius.Value;
                break;
            case CircleCollider2D circle when definition.Radius.HasValue:
                circle.radius = definition.Radius.Value;
                break;
            case CapsuleCollider2D capsule:
                if (definition.Size.HasValue) capsule.size = definition.Size.Value;
                if (definition.Direction.HasValue)
                    capsule.direction = (CapsuleDirection2D)definition.Direction.Value;
                break;
            case PolygonCollider2D polygon when definition.Points != null:
                polygon.pathCount = 1;
                polygon.SetPath(0, definition.Points);
                break;
        }
    }

    private static void EnsureModuleComponents(
        GameRes gameRes,
        RuntimeItemDefinition definition,
        Item item,
        ItemData itemData)
    {
        List<Module> available = item.GetComponentsInChildren<Module>(true)
            .Where(module => module != null)
            .ToList();

        foreach (KeyValuePair<string, ModuleData> pair in
                 itemData.ModuleDataDic ?? new Dictionary<string, ModuleData>())
        {
            string stableName = pair.Key;
            ModuleData moduleData = pair.Value;
            if (moduleData == null || string.IsNullOrWhiteSpace(moduleData.ID))
                continue;

            string prefabId = definition.GetModulePrefabId(stableName, moduleData.ID);
            int embeddedIndex = available.FindIndex(module =>
                module.MatchesPersistedId(moduleData.ID) ||
                module.MatchesPersistedId(prefabId));
            if (embeddedIndex >= 0)
            {
                available.RemoveAt(embeddedIndex);
                continue;
            }

            GameObject moduleObject = gameRes.InstantiatePrefab(prefabId, parent: item.transform);
            Module module = moduleObject?.GetComponentInChildren<Module>(true);
            if (module == null)
                throw new MissingComponentException(
                    $"物品 {itemData.IDName} 无法实例化模块：{moduleData.ID}（Prefab={prefabId}）");
            moduleObject.name = prefabId;
            moduleObject.transform.localPosition = Vector3.zero;
            moduleObject.transform.localRotation = Quaternion.identity;
            moduleObject.transform.localScale = Vector3.one;
        }
    }
}
