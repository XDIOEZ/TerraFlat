using System;
using System.Collections.Generic;
using UnityEngine;

[MemoryPack.MemoryPackable]
public sealed partial class MachinePowerIndicatorDefinition
{
    #region 供电指示灯配置
    public Vector4 PixelRect;
    public Color UnpoweredTint = new(.16f, .16f, .16f, 1f);
    #endregion
}

public sealed partial class MechanicalDepthVisual
{
    #region 可配置供电指示灯
    private const int PowerIndicatorPart = 35;
    private static readonly Dictionary<(Sprite, Vector4), Sprite> powerIndicatorSprites = new();
    static MechanicalDepthVisual() => SharedSpriteMeshCache.Clearing += ClearPowerIndicatorSprites;

    private void UpdatePowerIndicator(MachineEntity entity)
    {
        MachinePowerIndicatorDefinition indicator = entity?.Definition.PowerIndicator;
        PartVisual body = parts[0];
        if (indicator == null || body?.Sprite == null || entity.ElectricalSuppliedWatts > 0f) return;
        Vector4 rect = indicator.PixelRect;
        if (rect.z <= 0f || rect.w <= 0f) return;
        Sprite source = body.Sprite;
        var key = (source, rect);
        if (!powerIndicatorSprites.TryGetValue(key, out Sprite sprite) || sprite == null)
        {
            Rect pixels = source.rect;
            sprite = Sprite.Create(source.texture,
                new Rect(pixels.x + rect.x, pixels.y + pixels.height - rect.y - rect.w, rect.z, rect.w),
                new Vector2(.5f, .5f), source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.DontSave;
            powerIndicatorSprites[key] = sprite;
        }
        Vector3 offset = new((rect.x + rect.z * .5f - source.pivot.x) / source.pixelsPerUnit,
            (source.rect.height - rect.y - rect.w * .5f - source.pivot.y) / source.pixelsPerUnit);
        SetFacilitySprite(PowerIndicatorPart, sprite, body.Material,
            body.Offset + body.Rotation * Vector3.Scale(offset, body.Scale), 4,
            indicator.UnpoweredTint, body.Scale, body.Rotation);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearPowerIndicatorSprites()
    {
        foreach (Sprite sprite in powerIndicatorSprites.Values)
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
        powerIndicatorSprites.Clear();
    }
    #endregion
}
