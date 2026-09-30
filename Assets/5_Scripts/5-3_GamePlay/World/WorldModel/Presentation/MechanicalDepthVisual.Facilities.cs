using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed partial class MechanicalDepthVisual
{
    #region 设施灯光与内容图层
    private readonly List<PartVisual> facilityParts = new();
    private Light2D facilityLight;

    /// <summary>灯光保留 Unity 专用代理，晾架内容与主体一起合入脚点行。</summary>
    internal void UpdateFacility(MachineEntity entity)
    {
        if (entity.Definition.LogicId == "furnace") UpdateFurnaceLight(entity);
        if (entity.Definition.LogicId == "drying") UpdateDryingSprites(entity);
    }

    private void UpdateFurnaceLight(MachineEntity entity)
    {
        bool burning = entity.Logic is FurnaceLogic runtime ? runtime.IsBurning
            : MachinePersistence.Read<FurnaceRuntimeState>(entity.Snapshot, "furnace") is FurnaceRuntimeState saved &&
              saved.Smelting.IsSmelting && saved.Fuel.Fuel.x > .01f;
        if (!burning) return;
        var fuel = entity.Definition.Content?.Find<Mod_Fuel>();
        var lighting = entity.Definition.Content?.Find<Mod_LightSource>();
        Light2D template = (fuel?.Authoring as Mod_Fuel)?.fuelLight ??
            (lighting?.Authoring as Mod_LightSource)?.TargetLight ?? fuel?.Authoring.GetComponentInChildren<Light2D>(true);
        if (template == null && lighting == null) return;
        if (facilityLight == null)
        {
            var child = new GameObject("MachineLight");
            child.transform.SetParent(transform, false);
            facilityLight = child.AddComponent<Light2D>();
            facilityLight.lightType = Light2D.LightType.Point;
        }
        facilityLight.transform.localPosition = template != null ? template.transform.localPosition : Vector3.zero;
        facilityLight.transform.rotation = Quaternion.identity;
        var config = lighting?.Data("Data", ((Mod_LightSource)lighting.Authoring).Data);
        facilityLight.color = lighting?.Value("lightColor", template != null ? template.color : Color.white) ??
            (template != null ? template.color : Color.white);
        facilityLight.intensity = config?.Intensity ?? (fuel?.Authoring is Mod_Fuel source ? source.lightBaseIntensity : 0f);
        facilityLight.pointLightOuterRadius = config?.Range ?? (template != null ? template.pointLightOuterRadius : 0f);
        facilityLight.pointLightInnerRadius = config?.InnerRadius ?? (template != null ? template.pointLightInnerRadius : 0f);
        if (template != null)
        {
            facilityLight.blendStyleIndex = template.blendStyleIndex;
            facilityLight.shadowsEnabled = template.shadowsEnabled;
            facilityLight.shadowIntensity = template.shadowIntensity;
            Light2DSortingLayerUtility.SetLightLayers(facilityLight, Light2DSortingLayerUtility.GetLightLayers(template));
        }
        else Light2DSortingLayerUtility.SetLightLayers(facilityLight,
            Light2DSortingLayerUtility.ResolveLayerIds(((Mod_LightSource)lighting.Authoring).TargetSortingLayers));
        facilityLight.enabled = facilityLight.intensity > 0f && facilityLight.pointLightOuterRadius > 0f;
    }

    private void UpdateDryingSprites(MachineEntity entity)
    {
        var config = entity.Definition.Content?.Find<Meatrack>();
        if (config?.Authoring is not Meatrack source) return;
        var runtime = entity.Logic as DryingRackLogic;
        SlotProcessingState state = runtime?.State ?? MachinePersistence.Read<SlotProcessingState>(entity.Snapshot, "drying");
        if (state?.Inventory?.itemSlots == null) return;
        int count = state.Inventory.itemSlots.Count;
        state.EnsureSlots(count);
        for (int i = 0; i < count; i++)
        {
            ItemData item = state.Inventory.itemSlots[i].itemData;
            if (item == null) continue;
            MeatrackDryingRule rule = runtime?.GetRule(item);
            Sprite sprite = rule?.DisplaySprite;
            Material material = parts[0]?.Material;
            if (GameRes.ExistingInstance.TryGetItemDefinition(item.IDName, out var itemDefinition))
            {
                if (sprite == null) sprite = itemDefinition.Sprite;
                if (itemDefinition.Material != null) material = itemDefinition.Material;
            }
            if (sprite == null || material == null) continue;
            Vector3 position = config.Value("VisualAnchorOffset", source.VisualAnchorOffset) +
                Vector3.right * ((i - (count - 1) * .5f) * config.Value("VisualSlotSpacing", source.VisualSlotSpacing));
            SetFacilitySprite(i * 2, sprite, material, position, source.ItemSpriteSortingOrder, Color.white);
            Sprite smoked = rule?.SmokedStateSprite ?? source.DefaultSmokeStateSprite;
            if (smoked == null || rule == null) continue;
            float progress = Mathf.Clamp01(state.Elapsed[i] / Mathf.Max(.01f, rule.RequiredDryingSeconds));
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
