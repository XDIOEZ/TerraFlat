using System.Collections.Generic;
using UnityEngine;

/// <summary>燃料资格与点火来源由炉体和组合燃烧设备共用。</summary>
public static class FurnaceFuelRules
{
    #region 燃料与点火资格
    public static bool IsTinder(ItemData item, List<string> ids = null, List<string> tags = null)
        => item != null && ((ids?.Contains(item.IDName) ?? item.IDName == "FireSeed") ||
            item.Tags != null && (tags != null ? item.Tags.ContainsAnyTag(tags) : item.Tags.ContainsTag(Tag.CombustionTinder)));

    public static bool TryResolve(ItemData item, bool tinder, float ignitionFuel, float ignitionTemperature,
        out float value, out float temperature)
    {
        value = temperature = 0f;
        if (item?.Stack == null || item.Stack.Amount < 1f || item.Tags?.ContainsTag(Tag.CombustionFuel) != true ||
            !Mod_Fuel.TryResolveItemData(item, out FuelData offered)) return false;
        value = tinder ? Mathf.Min(offered.Fuel.x, ignitionFuel) : offered.Fuel.x;
        temperature = tinder ? Mathf.Min(offered.MaxTemperature, ignitionTemperature) : offered.MaxTemperature;
        return MachineDefinition.Positive(value) && MachineDefinition.Positive(temperature);
    }

    public static bool HasHeldIgnition(Player actor)
    {
        if (actor == null) return false;
        Item held = actor.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem;
        return held != null && held.InHand && held.Owner == actor &&
            held.itemMods?.GetMod_ByID<Mod_Combustion>(Mod_Combustion.ModuleId)?.IsActivelyBurning == true;
    }
    #endregion
}
