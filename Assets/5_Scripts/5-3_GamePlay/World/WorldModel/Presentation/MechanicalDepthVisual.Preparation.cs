using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed partial class MechanicalDepthVisual
{
    #region 未注册的地面表现候选
    internal static MechanicalDepthVisual CreatePrepared(Transform parent, MachineDefinition definition, RuntimeItemDefinition item)
    {
        var root = new GameObject("PreparedMechanicalInteraction") { hideFlags = HideFlags.DontSave };
        root.SetActive(false);
        root.transform.SetParent(parent, false);
        var visual = root.AddComponent<MechanicalDepthVisual>();
        try
        {
            for (int i = 0; i < visual.parts.Length; i++) visual.parts[i] = new PartVisual { Id = i };
            int facilityCount = definition.BatteryIndicator != null ? BatteryPartStart + BatteryChargePresentation.CellCount :
                definition.LogicId == "cultivator" ? CultivatorPartStart + 16 : definition.LogicId == "drying" ? 16 : 0;
            for (int i = 0; i < facilityCount; i++) visual.facilityParts.Add(new PartVisual { Id = 4 + i });
            if (definition.Lighting != null || definition.Content.Find<Mod_Fuel>() != null)
            {
                var child = new GameObject("MachineLight");
                child.transform.SetParent(root.transform, false);
                visual.facilityLight = child.AddComponent<Light2D>();
                visual.facilityLight.lightType = Light2D.LightType.Point;
                visual.facilityLight.enabled = false;
            }
            if (definition.Lighting?.EmissiveSprite == true)
            {
                var child = new GameObject("MachineLightEmission");
                child.transform.SetParent(root.transform, false);
                visual.poweredEmission = child.AddComponent<SpriteRenderer>();
                MachineLightPresentation.ConfigureEmission(visual.poweredEmission, item.Sprite, Color.white, definition.Lighting, 0f);
            }
            return visual;
        }
        catch
        {
            visual.Dispose();
            throw;
        }
    }

    internal void AdoptPrepared(ChunkTilemapRenderer renderer, Vector3 position, int id, int layer)
    {
        owner = renderer; entityId = id; occupancy = layer;
        transform.SetParent(renderer.transform, false);
        transform.position = position;
        // 候选只有静态缓冲，此时激活仍没有交互目标、机器事件或可见部件。
        gameObject.SetActive(true);
    }
    #endregion
}
