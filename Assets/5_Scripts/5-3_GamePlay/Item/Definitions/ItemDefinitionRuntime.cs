using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>把运行时定义应用到共享外壳实例。</summary>
public static class ItemDefinitionRuntime
{
    public static void ConfigureInstance(GameRes gameRes, RuntimeItemDefinition definition, Item item, ItemData itemData)
    {
        if (gameRes == null || definition == null || item == null || itemData == null)
            return;

        item.itemData = itemData;
        item.gameObject.name = definition.Id;
        ApplyVisual(definition, item);
        EnsureModuleComponents(gameRes, definition, item, itemData);
    }

    private static void ApplyVisual(RuntimeItemDefinition definition, Item item)
    {
        if (definition.Sprite == null)
            return;

        SpriteRenderer renderer = null;
        if (!string.IsNullOrWhiteSpace(definition.RendererPath))
        {
            Transform target = item.transform.Find(definition.RendererPath);
            renderer = target != null ? target.GetComponent<SpriteRenderer>() : null;
        }
        renderer ??= item.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(candidate => candidate.sprite != null);
        renderer ??= item.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null)
            throw new MissingComponentException($"物品 {definition.Id} 的外壳缺少 SpriteRenderer");

        renderer.sprite = definition.Sprite;
        ItemVisualDefinitionDto visual = definition.Visual;
        if (visual?.RendererLocalPosition != null)
            renderer.transform.localPosition = visual.RendererLocalPosition.Value;
        if (visual?.RendererLocalEulerAngles != null)
            renderer.transform.localEulerAngles = visual.RendererLocalEulerAngles.Value;
        if (visual?.RendererLocalScale != null)
            renderer.transform.localScale = visual.RendererLocalScale.Value;
        if (visual?.Color != null)
            renderer.color = visual.Color.Value;
        if (visual?.FlipX != null)
            renderer.flipX = visual.FlipX.Value;
        if (visual?.FlipY != null)
            renderer.flipY = visual.FlipY.Value;
        if (!string.IsNullOrWhiteSpace(visual?.SortingLayerName))
            renderer.sortingLayerName = visual.SortingLayerName;
        if (visual?.SortingOrder != null)
            renderer.sortingOrder = visual.SortingOrder.Value;

        ApplyCollider(item, visual?.Collider);
        item.Sprite = renderer;
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
        var available = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Module module in item.GetComponentsInChildren<Module>(true))
        {
            string id = ResolveModuleId(module);
            if (string.IsNullOrWhiteSpace(id))
                continue;
            available[id] = available.TryGetValue(id, out int count) ? count + 1 : 1;
        }

        foreach (KeyValuePair<string, ModuleData> pair in
                 itemData.ModuleDataDic ?? new Dictionary<string, ModuleData>())
        {
            string stableName = pair.Key;
            ModuleData moduleData = pair.Value;
            if (moduleData == null || string.IsNullOrWhiteSpace(moduleData.ID))
                continue;
            if (available.TryGetValue(moduleData.ID, out int count) && count > 0)
            {
                available[moduleData.ID] = count - 1;
                continue;
            }

            string prefabId = definition.GetModulePrefabId(stableName, moduleData.ID);
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

    private static string ResolveModuleId(Module module)
    {
        if (!string.IsNullOrWhiteSpace(module?._Data?.ID))
            return module._Data.ID;
        if (module == null)
            return null;
        return string.IsNullOrWhiteSpace(module.gameObject.name) ? module.GetType().Name : module.gameObject.name;
    }
}
