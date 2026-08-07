using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class DimensionPortalAnchor
{
    public string AnchorId;
    public string SurfaceWorldKey;
    public int SurfaceEntranceGuid;
    public Vector3 SurfaceEntrancePosition;
    public string CaveWorldKey;
    public int CaveExitGuid;
    public Vector3 CaveExitPosition;
}

public static class DimensionTravelProgressStore
{
    private const string NamespaceKey = "flatworld.dimensions";
    private const string LastPositionsKey = "lastPositions";
    private const string PortalAnchorsKey = "portalAnchors";
    private const string ActivePortalAnchorKey = "activePortalAnchor";

    private static readonly Vector2Int[] CaveExitOffsets =
    {
        Vector2Int.zero,
        new(2, 0),
        new(-2, 0),
        new(0, 2),
        new(0, -2),
        new(2, 2),
        new(-2, 2),
        new(2, -2),
        new(-2, -2),
        new(3, 0),
        new(-3, 0),
        new(0, 3),
        new(0, -3)
    };

    #region 最后位置

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

    #endregion

    #region 入口锚点

    public static DimensionPortalAnchor GetOrCreateCaveAnchor(
        Data_Player playerData,
        WorldAddress surfaceAddress,
        Item surfaceEntrance,
        WorldAddress caveAddress,
        DimensionDefinition caveDefinition)
    {
        if (playerData == null || surfaceEntrance?.itemData == null || !surfaceAddress.IsSurface ||
            caveAddress.DimensionId != WorldAddress.CaveDimensionId)
        {
            return null;
        }

        int entranceGuid = surfaceEntrance.itemData.Guid;
        string anchorId = BuildAnchorId(surfaceAddress.WorldKey, entranceGuid);
        JObject dimensionData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
        JObject anchors = dimensionData[PortalAnchorsKey] as JObject ?? new JObject();
        DimensionPortalAnchor anchor = ParseAnchor(anchorId, anchors[anchorId] as JObject) ?? new DimensionPortalAnchor
        {
            AnchorId = anchorId,
            CaveExitGuid = GenerateStableGuid(caveAddress.WorldKey, entranceGuid, "CaveExit"),
            CaveExitPosition = ResolveCaveExitPosition(caveDefinition, entranceGuid)
        };

        anchor.SurfaceWorldKey = surfaceAddress.WorldKey;
        anchor.SurfaceEntranceGuid = entranceGuid;
        anchor.SurfaceEntrancePosition = surfaceEntrance.transform.position;
        anchor.CaveWorldKey = caveAddress.WorldKey;
        anchors[anchorId] = SerializeAnchor(anchor);
        dimensionData[PortalAnchorsKey] = anchors;
        dimensionData[ActivePortalAnchorKey] = anchorId;
        ItemSpecialDataJsonStore.WriteNamespace(playerData, NamespaceKey, dimensionData);
        return anchor;
    }

    public static bool TryGetAnchorByCaveExit(
        Data_Player playerData,
        WorldAddress caveAddress,
        Item caveExit,
        out DimensionPortalAnchor anchor)
    {
        anchor = null;
        if (playerData == null || caveExit?.itemData == null || caveAddress.DimensionId != WorldAddress.CaveDimensionId)
            return false;

        JObject dimensionData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
        if (dimensionData[PortalAnchorsKey] is not JObject anchors)
            return false;

        foreach (JProperty property in anchors.Properties())
        {
            DimensionPortalAnchor candidate = ParseAnchor(property.Name, property.Value as JObject);
            if (candidate == null || candidate.CaveExitGuid != caveExit.itemData.Guid ||
                candidate.CaveWorldKey != caveAddress.WorldKey)
            {
                continue;
            }

            candidate.CaveExitPosition = caveExit.transform.position;
            anchors[property.Name] = SerializeAnchor(candidate);
            dimensionData[PortalAnchorsKey] = anchors;
            dimensionData[ActivePortalAnchorKey] = property.Name;
            ItemSpecialDataJsonStore.WriteNamespace(playerData, NamespaceKey, dimensionData);
            anchor = candidate;
            return true;
        }

        return false;
    }

    public static void UpdateCaveExit(Data_Player playerData, DimensionPortalAnchor anchor, Item caveExit)
    {
        if (playerData == null || anchor == null || caveExit?.itemData == null || string.IsNullOrWhiteSpace(anchor.AnchorId))
            return;

        anchor.CaveExitGuid = caveExit.itemData.Guid;
        anchor.CaveExitPosition = caveExit.transform.position;
        JObject dimensionData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
        JObject anchors = dimensionData[PortalAnchorsKey] as JObject ?? new JObject();
        anchors[anchor.AnchorId] = SerializeAnchor(anchor);
        dimensionData[PortalAnchorsKey] = anchors;
        dimensionData[ActivePortalAnchorKey] = anchor.AnchorId;
        ItemSpecialDataJsonStore.WriteNamespace(playerData, NamespaceKey, dimensionData);
    }

    private static string BuildAnchorId(string surfaceWorldKey, int entranceGuid)
    {
        return $"{surfaceWorldKey}:{entranceGuid}";
    }

    private static Vector3 ResolveCaveExitPosition(DimensionDefinition caveDefinition, int entranceGuid)
    {
        Vector3 center = caveDefinition?.DefaultSpawnPosition ?? new Vector3(0.5f, 0.5f, 0f);
        int index = (int)((uint)GenerateStableGuid("cave-offset", entranceGuid, "slot") % CaveExitOffsets.Length);
        Vector2Int offset = CaveExitOffsets[index];
        return new Vector3(
            Mathf.Floor(center.x) + 0.5f + offset.x,
            Mathf.Floor(center.y) + 0.5f + offset.y,
            0f);
    }

    private static int GenerateStableGuid(string worldKey, int sourceGuid, string salt)
    {
        unchecked
        {
            uint hash = 2166136261u;
            string key = $"{worldKey}|{sourceGuid}|{salt}";
            for (int i = 0; i < key.Length; i++)
                hash = (hash ^ key[i]) * 16777619u;
            int result = (int)hash;
            return result == 0 ? 1 : result;
        }
    }

    private static JObject SerializeAnchor(DimensionPortalAnchor anchor)
    {
        return new JObject
        {
            ["surfaceWorldKey"] = anchor.SurfaceWorldKey,
            ["surfaceEntranceGuid"] = anchor.SurfaceEntranceGuid,
            ["surfaceEntrancePosition"] = SerializePosition(anchor.SurfaceEntrancePosition),
            ["caveWorldKey"] = anchor.CaveWorldKey,
            ["caveExitGuid"] = anchor.CaveExitGuid,
            ["caveExitPosition"] = SerializePosition(anchor.CaveExitPosition)
        };
    }

    private static DimensionPortalAnchor ParseAnchor(string anchorId, JObject value)
    {
        if (value == null || !TryParsePosition(value["surfaceEntrancePosition"] as JObject, out Vector3 surfacePosition) ||
            !TryParsePosition(value["caveExitPosition"] as JObject, out Vector3 cavePosition))
        {
            return null;
        }

        string surfaceWorldKey = value.Value<string>("surfaceWorldKey");
        string caveWorldKey = value.Value<string>("caveWorldKey");
        int? surfaceGuid = value.Value<int?>("surfaceEntranceGuid");
        int? caveGuid = value.Value<int?>("caveExitGuid");
        if (string.IsNullOrWhiteSpace(surfaceWorldKey) || string.IsNullOrWhiteSpace(caveWorldKey) ||
            !surfaceGuid.HasValue || !caveGuid.HasValue)
        {
            return null;
        }

        return new DimensionPortalAnchor
        {
            AnchorId = anchorId,
            SurfaceWorldKey = surfaceWorldKey,
            SurfaceEntranceGuid = surfaceGuid.Value,
            SurfaceEntrancePosition = surfacePosition,
            CaveWorldKey = caveWorldKey,
            CaveExitGuid = caveGuid.Value,
            CaveExitPosition = cavePosition
        };
    }

    private static JObject SerializePosition(Vector3 position)
    {
        return new JObject
        {
            ["x"] = position.x,
            ["y"] = position.y,
            ["z"] = position.z
        };
    }

    private static bool TryParsePosition(JObject value, out Vector3 position)
    {
        position = default;
        if (value == null)
            return false;

        float? x = value.Value<float?>("x");
        float? y = value.Value<float?>("y");
        float? z = value.Value<float?>("z");
        if (!x.HasValue || !y.HasValue || !z.HasValue ||
            !IsFinite(x.Value) || !IsFinite(y.Value) || !IsFinite(z.Value))
        {
            return false;
        }

        position = new Vector3(x.Value, y.Value, z.Value);
        return true;
    }

    #endregion

    #region 旧版坐标入口兼容

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

        planetAnchors[anchorKey] = SerializePosition(position);
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
            if (property.Value is JObject storedPosition &&
                TryParsePosition(storedPosition, out Vector3 position))
            {
                result.Add(position);
            }
        }

        return result;
    }

    #endregion

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
