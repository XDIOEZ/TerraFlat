using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>把运行时定义应用到共享外壳实例。</summary>
public static class ItemDefinitionRuntime
{
    #region 原位配置更新

    /// <summary>
    /// 更新已有模块的显式配置与未被玩法接管的 Sprite/材质，不替换 ItemData、模块集合或调用 Load。
    /// 新增/删除模块和外壳结构由后续新实例使用；已有实例的身份、库存和模块运行态继续保留。
    /// </summary>
    public static void RefreshLiveConfiguration(Item item, RuntimeItemDefinition previous, RuntimeItemDefinition current)
    {
        if (item == null || previous == null || current == null) return;
        var updates = new List<(Module Module, string Name, string Json)>();
        foreach (KeyValuePair<string, Module> pair in item.Mods)
        {
            Module module = pair.Value;
            if (module == null || !current.TryGetModuleParameters(pair.Key, out string json)) continue;
            previous.TryGetModuleParameters(pair.Key, out string oldJson);
            string moduleId = module._Data?.ID;
            if (json == oldJson || current.GetModulePrefabId(pair.Key, moduleId) != previous.GetModulePrefabId(pair.Key, moduleId)) continue;
            json = GetChangedModuleParameters(oldJson, json);
            if (json == null) continue;
            ModuleJsonConfigurator.Validate(module, current.Id, pair.Key, moduleId, json);
            updates.Add((module, pair.Key, json));
        }
        foreach (var update in updates)
        {
            update.Module.ApplyResourceConfiguration(current.Id, update.Name, update.Json);
        }
        SpriteRenderer renderer = item.Sprite;
        if (renderer != null)
        {
            if (current.Sprite != null && renderer.sprite == previous.Sprite) renderer.sprite = current.Sprite;
            if (current.Material != null && renderer.sharedMaterial == previous.Material) renderer.sharedMaterial = current.Material;
        }
        item.MarkModuleScheduleDirty();
        item.NotifyRuntimeStructureChanged();
    }

    /// <summary>仅写发生变化的显式字段，未修改的集合与内部运行态不能随完整 JSON 被重新构造。</summary>
    private static string GetChangedModuleParameters(string previous, string current)
    {
        if (string.IsNullOrWhiteSpace(current)) return null;
        JObject before = string.IsNullOrWhiteSpace(previous) ? new JObject() : JObject.Parse(previous);
        JObject changes = JObject.Parse(current);
        foreach (JProperty property in changes.Properties().ToArray())
            if (JToken.DeepEquals(before[property.Name], property.Value)) property.Remove();
        return changes.HasValues ? changes.ToString(Formatting.None) : null;
    }

    #endregion

    private static readonly int PlayerOccluderId = Shader.PropertyToID("_PlayerOccluder");
    private static readonly MaterialPropertyBlock VisualPropertyBlock = new();

    public static void ConfigureInstance(GameRes gameRes, RuntimeItemDefinition definition, Item item, ItemData itemData)
    {
        if (gameRes == null || definition == null || item == null || itemData == null)
            return;

        item.BindData(itemData);
        item.gameObject.name = definition.Id;
        ApplyVisual(definition, item);
        EnsureModuleComponents(gameRes, definition, item, itemData);
    }

    /// <summary>
    /// 将存档中的物品实例状态重新挂到当前 ItemDefinition 上。
    /// 当前 JSON/Prefab 决定静态配置和模块组合，存档只保留数量、耐久比例、GUID、位置及模块运行态。
    /// </summary>
    public static ItemData RebasePersistedData(GameRes gameRes, ItemData persistedData)
    {
        if (gameRes == null || persistedData == null || string.IsNullOrWhiteSpace(persistedData.IDName))
            return persistedData;

        ItemData rebasedData = persistedData;
        string definitionId = Mod_HandDrill.ResolveCarrierDefinition(persistedData);
        if (gameRes.TryGetItemDefinition(definitionId, out RuntimeItemDefinition definition))
        {
            ItemData currentData = definition.CreateItemData();
            RestoreItemInstanceState(currentData, persistedData);
            RestoreModuleRuntimeState(currentData.ModuleDataDic, persistedData.ModuleDataDic);
            Mod_HandDrill.RestoreRuntimeDurability(currentData);
            rebasedData = currentData;
        }

        RebaseNestedPersistedItems(gameRes, rebasedData);
        return rebasedData;
    }

    /// <summary>只刷新物品内部库存中的 ItemData；用于 Player 等不由 ItemDefinition 创建的根数据。</summary>
    public static void RebaseNestedPersistedItems(GameRes gameRes, ItemData itemData)
    {
        if (gameRes == null || itemData == null)
            return;

        if (itemData is Data_Player playerData)
            RebaseInventoryDictionary(gameRes, playerData._inventoryData);

        if (itemData.ModuleDataDic == null)
            return;

        foreach (ModuleData moduleData in itemData.ModuleDataDic.Values)
        {
            if (moduleData is Inventory_ModuleData inventoryModuleData)
                RebaseInventoryDictionary(gameRes, inventoryModuleData.Data);
        }
    }

    /// <summary>保留实例态；定义字段保持 currentData 的当前版本配置。</summary>
    private static void RestoreItemInstanceState(ItemData currentData, ItemData persistedData)
    {
        float durabilityRatio = persistedData.MaxDurability > 0f
            ? Mathf.Clamp01(persistedData.Durability / persistedData.MaxDurability)
            : 1f;

        currentData.Guid = persistedData.Guid;
        currentData.ItemSpecialData = persistedData.ItemSpecialData;
        currentData.inHand = persistedData.inHand;
        currentData.transform = persistedData.transform ?? currentData.transform;
        currentData.FactionId = persistedData.FactionId;
        CraftedDurabilityQuality.RestorePersistedMultiplier(currentData, persistedData);

        if (currentData.Stack != null && persistedData.Stack != null)
            currentData.Stack.Amount = persistedData.Stack.Amount;

        if (currentData.MaxDurability > 0f)
            currentData.Durability = currentData.MaxDurability * durabilityRatio;
    }

    /// <summary>
    /// 当前定义决定模块集合、稳定 ID、启用状态和类型；匹配模块只恢复其运行时负载。
    /// 删除的旧模块不会被存档重新实例化，新模块直接使用当前定义默认状态。
    /// </summary>
    private static void RestoreModuleRuntimeState(
        Dictionary<string, ModuleData> currentModules,
        IReadOnlyDictionary<string, ModuleData> persistedModules)
    {
        if (currentModules == null || persistedModules == null)
            return;

        foreach (KeyValuePair<string, ModuleData> pair in currentModules)
        {
            ModuleData current = pair.Value;
            if (current == null ||
                !persistedModules.TryGetValue(pair.Key, out ModuleData persisted) ||
                persisted == null ||
                persisted.GetType() != current.GetType())
            {
                continue;
            }

            switch (current)
            {
                case Ex_ModData currentJson when persisted is Ex_ModData persistedJson:
                    currentJson.BitData = persistedJson.BitData;
                    break;

                case Ex_ModData_MemoryPackable currentBinary when persisted is Ex_ModData_MemoryPackable persistedBinary:
                    currentBinary.BitData = persistedBinary.BitData == null
                        ? null
                        : (byte[])persistedBinary.BitData.Clone();
                    break;

                case CollectableModuleData currentCollectable when persisted is CollectableModuleData persistedCollectable:
                    currentCollectable.CurrentStock = persistedCollectable.CurrentStock;
                    currentCollectable.IsInitialized = persistedCollectable.IsInitialized;
                    break;

                case Inventory_ModuleData currentInventory when persisted is Inventory_ModuleData persistedInventory:
                    currentInventory.Data = persistedInventory.Data ?? currentInventory.Data;
                    currentInventory.PanleRectPosition = persistedInventory.PanleRectPosition;
                    currentInventory.BasePanelIsOpen = persistedInventory.BasePanelIsOpen;
                    break;

                case ModData_FoodData currentFood when persisted is ModData_FoodData persistedFood:
                    currentFood.MechanicStates = persistedFood.MechanicStates ?? new List<FoodMechanicStateData>();
                    if (currentFood.FoodData != null && persistedFood.FoodData != null)
                        currentFood.FoodData.PanelPosition = persistedFood.FoodData.PanelPosition;
                    break;
            }
        }
    }

    /// <summary>递归把库存里的物品配置提升到当前定义，库存位置和数量等实例状态保持不变。</summary>
    private static void RebaseInventoryDictionary(GameRes gameRes, IDictionary<string, Inventory_Data> inventories)
    {
        if (inventories == null)
            return;

        foreach (Inventory_Data inventoryData in inventories.Values)
        {
            if (inventoryData?.itemSlots == null)
                continue;

            for (int i = 0; i < inventoryData.itemSlots.Count; i++)
            {
                ItemSlot slot = inventoryData.itemSlots[i];
                if (slot?.itemData == null)
                    continue;
                slot.itemData = RebasePersistedData(gameRes, slot.itemData);
            }
        }
    }

    private static void ApplyVisual(RuntimeItemDefinition definition, Item item)
    {
        ItemVisualDefinitionDto visual = definition.Visual;
        if (visual == null)
            return;

        bool needsRenderer = definition.Sprite != null ||
                             definition.Material != null ||
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
            if (definition.Material != null)
                renderer.sharedMaterial = definition.Material;
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

            ApplyPlayerOccluderFlag(item, renderer);
            item.Sprite = renderer;
        }

        ApplyAnimator(item, visual.AnimatorPath, definition.AnimatorController, visual.AnimationState);
        ApplyCollider(item, visual.Collider);
    }

    /// <summary>按 Tree 标签写入局部遮挡开关；对象池复用时也会明确复位，避免共享外壳串状态。</summary>
    private static void ApplyPlayerOccluderFlag(Item item, SpriteRenderer renderer)
    {
        bool occludesPlayer = item.itemData?.Tags != null && item.itemData.Tags.ContainsTag(Tag.Tree);

        VisualPropertyBlock.Clear();
        renderer.GetPropertyBlock(VisualPropertyBlock);
        VisualPropertyBlock.SetFloat(PlayerOccluderId, occludesPlayer ? 1f : 0f);
        renderer.SetPropertyBlock(VisualPropertyBlock);
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
