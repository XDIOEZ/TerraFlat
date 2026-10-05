using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed partial class MechanicalDepthVisual
{
    #region 设施灯光与内容图层
    private static readonly Color CombustionLightColor = new(1f, 0.29712662f, 0f, 1f);
    private readonly List<PartVisual> facilityParts = new();
    private Light2D facilityLight;

    /// <summary>灯光保留 Unity 专用代理，晾架内容与主体一起合入脚点行。</summary>
    internal void UpdateFacility(MachineEntity entity)
    {
        if (entity.Definition.Content?.Find<Mod_Fuel>() != null) UpdateCombustionLight(entity);
        if (entity.Definition.LogicId == "drying") UpdateDryingSprites(entity);
    }

    private void UpdateCombustionLight(MachineEntity entity)
    {
        bool burning = entity.Logic?.IsBurning == true;
        if (entity.Logic == null && entity.Definition.LogicId == "furnace")
            burning = MachinePersistence.Read<FurnaceRuntimeState>(entity.Snapshot, "furnace") is FurnaceRuntimeState saved &&
                saved.Smelting.IsSmelting && saved.Fuel.Fuel.x > .01f;
        if (!burning) return;

        var fuel = entity.Definition.Content?.Find<Mod_Fuel>();
        var lighting = entity.Definition.Content?.Find<Mod_LightSource>();
        var fuelAuthoring = fuel?.Authoring as Mod_Fuel;
        var lightingAuthoring = lighting?.Authoring as Mod_LightSource;
        Light2D template = fuelAuthoring?.fuelLight ??
            lightingAuthoring?.TargetLight ?? fuel?.Authoring.GetComponentInChildren<Light2D>(true);
        if (facilityLight == null)
        {
            var child = new GameObject("MachineLight");
            child.transform.SetParent(transform, false);
            facilityLight = child.AddComponent<Light2D>();
            facilityLight.lightType = Light2D.LightType.Point;
        }

        facilityLight.transform.localPosition = template != null ? template.transform.localPosition : Vector3.zero;
        facilityLight.transform.rotation = Quaternion.identity;
        var config = lightingAuthoring != null ? lighting.Data("Data", lightingAuthoring.Data) : null;
        // 燃烧工作方块统一使用火把的橙红火光颜色。
        facilityLight.color = CombustionLightColor;
        facilityLight.intensity = config?.Intensity ?? fuelAuthoring?.lightBaseIntensity ?? 1f;
        facilityLight.pointLightOuterRadius = config?.Range ?? (template != null ? template.pointLightOuterRadius : 8f);
        facilityLight.pointLightInnerRadius = config?.InnerRadius ?? (template != null ? template.pointLightInnerRadius : .1f);
        if (template != null)
        {
            facilityLight.blendStyleIndex = template.blendStyleIndex;
            facilityLight.shadowsEnabled = template.shadowsEnabled;
            facilityLight.shadowIntensity = template.shadowIntensity;
            Light2DSortingLayerUtility.SetLightLayers(facilityLight, Light2DSortingLayerUtility.GetLightLayers(template));
        }
        else
        {
            facilityLight.shadowsEnabled = true;
            facilityLight.shadowIntensity = .75f;
            Light2DSortingLayerUtility.SetLightLayers(facilityLight,
                Light2DSortingLayerUtility.ResolveLayerIds(lightingAuthoring?.TargetSortingLayers));
        }
        facilityLight.enabled = facilityLight.intensity > 0f && facilityLight.pointLightOuterRadius > 0f;
    }

    private void UpdateDryingSprites(MachineEntity entity)
    {
        var config = entity.Definition.Content?.Find<Mod_Meatrack>();
        if (config?.Authoring is not Mod_Meatrack source) return;
        var runtime = entity.Logic as DryingRackLogic;
        SlotProcessingState state = runtime?.State ?? MachinePersistence.Read<SlotProcessingState>(entity.Snapshot, "drying");
        if (state?.Inventory?.itemSlots == null) return;
        int count = state.Inventory.itemSlots.Count;
        state.EnsureSlots(count);
        for (int i = 0; i < count; i++)
        {
            ItemData item = state.Inventory.itemSlots[i].itemData;
            if (item == null) continue;
            Sprite sprite = null;
            Material material = parts[0]?.Material;
            if (GameRes.ExistingInstance.TryGetItemDefinition(item.IDName, out var itemDefinition))
            {
                sprite = itemDefinition.Sprite;
                if (itemDefinition.Material != null) material = itemDefinition.Material;
            }
            if (sprite == null || material == null) continue;
            Vector3 position = config.Value("VisualAnchorOffset", source.VisualAnchorOffset) +
                Vector3.right * ((i - (count - 1) * .5f) * config.Value("VisualSlotSpacing", source.VisualSlotSpacing));
            SetFacilitySprite(i * 2, sprite, material, position, source.ItemSpriteSortingOrder, Color.white);
            Sprite smoked = source.DefaultSmokeStateSprite;
            bool canDry = runtime?.CanDry(item) ?? ItemMatterRuntime.CanMoistureTransform(item);
            if (smoked == null || !canDry) continue;
            float progress = runtime?.GetProgress(i) ?? ItemMatterRuntime.GetMoistureProgress01(item);
            SetFacilitySprite(i * 2 + 1, smoked, material, position, source.SmokeSpriteSortingOrder,
                Color.Lerp(source.SmokeStartColor, source.SmokeDoneColor, progress));
        }
    }

    private void SetFacilitySprite(int index, Sprite sprite, Material material, Vector3 position, int order, Color color)
    {
        while (facilityParts.Count <= index) facilityParts.Add(null);
        PartVisual part = facilityParts[index] ??= new PartVisual { Id = 4 + index };
        part.Sprite = sprite; part.Material = material; part.Offset = position;
        part.Rotation = Quaternion.identity; part.Scale = Vector3.one;
        part.Order = occupancy * 32 + order; part.Tint = color;
        part.Animation = Vector4.zero; part.Touched = part.Visible = true;
        Submit(part);
    }
    #endregion
}
