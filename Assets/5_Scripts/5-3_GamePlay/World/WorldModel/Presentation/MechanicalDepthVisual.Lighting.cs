using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed partial class MechanicalDepthVisual
{
    #region 通电设施的照明表现
    private MachineEntity poweredLightEntity;
    private SpriteRenderer poweredEmission;
    private bool poweredLightTouched;

    private void BeginLightingUpdate()
    {
        poweredLightTouched = false;
        if (poweredEmission != null) poweredEmission.transform.SetParent(transform, true);
    }

    private void EndLightingUpdate()
    {
        if (!poweredLightTouched && poweredEmission != null) poweredEmission.enabled = false;
    }

    private void UpdatePoweredLight(MachineEntity entity)
    {
        poweredLightEntity = entity?.Definition.Lighting != null ? entity : null;
        poweredLightTouched = poweredLightEntity != null;
        float power = ElectricLightLogic.GetPowerFraction(poweredLightEntity);
        if (poweredLightEntity == null || power <= 0f)
        {
            if (poweredEmission != null) poweredEmission.enabled = false;
            return;
        }
        if (facilityLight == null)
        {
            var child = new GameObject("MachineLight");
            child.transform.SetParent(transform, false);
            facilityLight = child.AddComponent<Light2D>();
        }
        MachineLightDefinition lighting = poweredLightEntity.Definition.Lighting;
        facilityLight.transform.localPosition = lighting.LocalPosition;
        facilityLight.transform.rotation = Quaternion.identity;
        MachineLightPresentation.ConfigureLight(facilityLight, lighting, power);

        PartVisual body = parts[0];
        Transform sortingRoot = body != null ? owner?.GetBuildingDepthRenderer(submerged).GetPartSortingRoot(
            ChunkDepthMeshRenderer.MachineDomain, entityId, body.Id) : null;
        if (!lighting.EmissiveSprite || sortingRoot == null)
        {
            if (poweredEmission != null) poweredEmission.enabled = false;
            return;
        }
        if (poweredEmission == null)
        {
            var child = new GameObject("MachineLightEmission");
            child.transform.SetParent(transform, false);
            poweredEmission = child.AddComponent<SpriteRenderer>();
        }
        // 发光玻璃跟随主体脚点排序，保留贴图 Alpha，不把金属底座一起提亮。
        poweredEmission.transform.SetParent(sortingRoot, true);
        poweredEmission.transform.SetPositionAndRotation(transform.position + body.Offset, body.Rotation);
        poweredEmission.transform.localScale = body.Scale;
        poweredEmission.sortingLayerID = sortingRoot.GetComponent<SortingGroup>().sortingLayerID;
        poweredEmission.sortingOrder = body.Order + 1;
        MachineLightPresentation.ConfigureEmission(poweredEmission, body.Sprite, body.Tint, lighting, power);
        poweredEmission.sharedMaterial = owner.ResolveBuildingOverlayMaterialForSubmergedSurface(poweredEmission.sharedMaterial, submerged);
    }

    private void ReleaseLighting()
    {
        poweredLightEntity = null;
        if (poweredEmission == null) return;
        poweredEmission.enabled = false;
        if (Application.isPlaying) Destroy(poweredEmission.gameObject);
        else DestroyImmediate(poweredEmission.gameObject);
        poweredEmission = null;
    }
    #endregion
}

/// <summary>地表与船体共用照明表现参数，供电真值始终由领域逻辑提供。</summary>
internal static class MachineLightPresentation
{
    #region 通用光源与自发光玻璃
    private static Material emissiveMaterial;
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ClipLocalYId = Shader.PropertyToID("_ClipLocalY");
    private static readonly MaterialPropertyBlock EmissiveProperties = new();

    internal static Sprite ResolveBodySprite(MachineEntity entity, RuntimeItemDefinition item)
        => ElectricLightLogic.GetPowerFraction(entity) > 0f &&
           item.TryGetVisualStateSprite(entity.Definition.Lighting.LitSpriteState, out Sprite lit)
            ? lit : item.Sprite;

    internal static void ConfigureLight(Light2D light, MachineLightDefinition definition, float power)
    {
        light.lightType = Light2D.LightType.Point;
        light.color = definition.Color;
        light.intensity = definition.Intensity * Mathf.Clamp01(power);
        light.pointLightOuterRadius = definition.Radius;
        light.pointLightInnerRadius = definition.InnerRadius;
        light.shadowsEnabled = true;
        light.shadowIntensity = 1f;
        Light2DSortingLayerUtility.SetLightLayers(light, Light2DSortingLayerUtility.ResolveLayerIds(null));
        light.enabled = light.intensity > 0f;
    }

    internal static void ConfigureEmission(SpriteRenderer renderer, Sprite sprite, Color tint,
        MachineLightDefinition definition, float power)
    {
        if (emissiveMaterial == null)
        {
            Shader shader = Resources.Load<Shader>("Shaders/EmissiveSpriteOverlay");
            if (shader == null) throw new System.InvalidOperationException("缺少电灯自发光覆盖 Shader。");
            emissiveMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        renderer.sprite = sprite;
        renderer.color = tint * new Color(power, power, power, 1f);
        renderer.sharedMaterial = emissiveMaterial;
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        EmissiveProperties.Clear();
        EmissiveProperties.SetTexture(MainTexId, sprite.texture);
        EmissiveProperties.SetFloat(ClipLocalYId, definition.EmissiveStartLocalY);
        renderer.SetPropertyBlock(EmissiveProperties);
        renderer.enabled = definition.EmissiveSprite && power > 0f;
    }
    #endregion
}
