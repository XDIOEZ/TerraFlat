using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class DimensionTravelProgressStore
{
    private const string NamespaceKey = "flatworld.dimensions";
    private const string LastPositionsKey = "lastPositions";

    public static bool TryGetLastPosition(Data_Player playerData, WorldAddress address, out Vector3 position)
    {
        position = default;
        if (playerData == null || !address.IsValid)
            return false;

        JObject dimensionData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
        if (dimensionData[LastPositionsKey] is not JObject positions ||
            positions[address.WorldKey] is not JObject storedPosition)
        {
            return false;
        }

        float? x = storedPosition.Value<float?>("x");
        float? y = storedPosition.Value<float?>("y");
        float? z = storedPosition.Value<float?>("z");
        if (!x.HasValue || !y.HasValue || !z.HasValue ||
            !IsFinite(x.Value) || !IsFinite(y.Value) || !IsFinite(z.Value))
        {
            return false;
        }

        position = new Vector3(x.Value, y.Value, z.Value);
        return true;
    }

    public static void SetLastPosition(Data_Player playerData, WorldAddress address, Vector3 position)
    {
        if (playerData == null || !address.IsValid || !IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
            return;

        JObject dimensionData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
        JObject positions = dimensionData[LastPositionsKey] as JObject ?? new JObject();
        positions[address.WorldKey] = new JObject
        {
            ["x"] = position.x,
            ["y"] = position.y,
            ["z"] = position.z
        };
        dimensionData[LastPositionsKey] = positions;
        ItemSpecialDataJsonStore.WriteNamespace(playerData, NamespaceKey, dimensionData);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
