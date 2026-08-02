using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class DimensionTravelProgressStore
{
    private const string NamespaceKey = "flatworld.dimensions";
    private const string LastPositionsKey = "lastPositions";
    private const string PortalAnchorsKey = "portalAnchors";

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

    public static bool AddPortalAnchor(Data_Player playerData, string planetId, Vector3 position)
    {
        if (playerData == null ||
            string.IsNullOrWhiteSpace(planetId) ||
            !IsFinite(position.x) ||
            !IsFinite(position.y) ||
            !IsFinite(position.z))
        {
            return false;
        }

        string normalizedPlanetId = new WorldAddress(
            planetId,
            WorldAddress.SurfaceDimensionId).PlanetId;
        string anchorKey = $"{Mathf.FloorToInt(position.x)}:{Mathf.FloorToInt(position.y)}";
        JObject dimensionData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
        JObject allAnchors = dimensionData[PortalAnchorsKey] as JObject ?? new JObject();
        JObject planetAnchors = allAnchors[normalizedPlanetId] as JObject ?? new JObject();
        if (planetAnchors[anchorKey] != null)
            return false;

        planetAnchors[anchorKey] = WritePosition(position);
        allAnchors[normalizedPlanetId] = planetAnchors;
        dimensionData[PortalAnchorsKey] = allAnchors;
        ItemSpecialDataJsonStore.WriteNamespace(playerData, NamespaceKey, dimensionData);
        return true;
    }

    public static List<Vector3> GetPortalAnchors(Data_Player playerData, string planetId)
    {
        List<Vector3> result = new();
        if (playerData == null || string.IsNullOrWhiteSpace(planetId))
            return result;

        string normalizedPlanetId = new WorldAddress(
            planetId,
            WorldAddress.SurfaceDimensionId).PlanetId;
        JObject dimensionData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
        if (dimensionData[PortalAnchorsKey] is not JObject allAnchors ||
            allAnchors[normalizedPlanetId] is not JObject planetAnchors)
        {
            return result;
        }

        foreach (JProperty property in planetAnchors.Properties())
        {
            if (property.Value is JObject storedPosition && TryReadPosition(storedPosition, out Vector3 position))
                result.Add(position);
        }

        return result;
    }

    private static JObject WritePosition(Vector3 position)
    {
        return new JObject
        {
            ["x"] = position.x,
            ["y"] = position.y,
            ["z"] = position.z
        };
    }

    private static bool TryReadPosition(JObject storedPosition, out Vector3 position)
    {
        position = default;
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

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
