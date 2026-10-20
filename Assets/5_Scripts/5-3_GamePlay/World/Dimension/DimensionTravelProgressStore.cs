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

public static partial class DimensionTravelProgressStore
{
    private const string NamespaceKey = "flatworld.dimensions";
    private const string LastPositionsKey = "lastPositions";
    private const string PortalAnchorsKey = "portalAnchors";

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

        return GetPlayerReadCache(playerData).LastPositions.TryGetValue(address.WorldKey, out position);
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
        InvalidatePlayerReadCache(playerData);
    }

    #endregion

    #region 入口锚点

    public static DimensionPortalAnchor GetOrCreateCaveAnchor(
        GameSaveData save,
        WorldAddress surfaceAddress,
        Item surfaceEntrance,
        WorldAddress caveAddress,
        DimensionDefinition caveDefinition,
        bool commit = true)
    {
        if (save == null || surfaceEntrance?.itemData == null || !surfaceAddress.IsSurface ||
            caveAddress.DimensionId != WorldAddress.CaveDimensionId)
        {
            return null;
        }

        int entranceGuid = surfaceEntrance.itemData.Guid;
        Vector3 entrancePosition = WorldLocalPresentation.ToLogical(surfaceEntrance.transform.position);
        string anchorId = BuildAnchorId(surfaceAddress.WorldKey, entranceGuid);
        JObject dimensionData = ItemSpecialDataJsonStore.ReadRoot(save.DimensionStateJson);
        JObject anchors = dimensionData[PortalAnchorsKey] as JObject ?? new JObject();
        // 重建只更换地表实体 GUID，不更换这个格子已经绑定的矿洞出口。
        DimensionPortalAnchor anchor = ParseAnchor(anchorId, anchors[anchorId] as JObject);
        if (anchor == null)
        {
            foreach (JProperty property in anchors.Properties())
            {
                DimensionPortalAnchor candidate = ParseAnchor(property.Name, property.Value as JObject);
                if (candidate == null || candidate.SurfaceWorldKey != surfaceAddress.WorldKey ||
                    candidate.CaveWorldKey != caveAddress.WorldKey ||
                    Vector2Int.FloorToInt(candidate.SurfaceEntrancePosition) != Vector2Int.FloorToInt(entrancePosition))
                    continue;
                anchor = candidate;
                anchorId = candidate.AnchorId;
                break;
            }
        }
        anchor ??= new DimensionPortalAnchor
        {
            AnchorId = anchorId,
            CaveExitGuid = GenerateStableGuid(caveAddress.WorldKey, entranceGuid, "CaveExit"),
            CaveExitPosition = ResolveCaveExitPosition(caveDefinition, entranceGuid)
        };

        anchor.SurfaceWorldKey = surfaceAddress.WorldKey;
        anchor.SurfaceEntranceGuid = entranceGuid;
        anchor.SurfaceEntrancePosition = entrancePosition;
        anchor.CaveWorldKey = caveAddress.WorldKey;
        anchors[anchorId] = SerializeAnchor(anchor);
        dimensionData[PortalAnchorsKey] = anchors;
        if (commit) save.DimensionStateJson = dimensionData.ToString(Newtonsoft.Json.Formatting.None);
        return anchor;
    }

    public static bool TryGetAnchorByCaveExit(
        GameSaveData save,
        WorldAddress caveAddress,
        Item caveExit,
        out DimensionPortalAnchor anchor)
    {
        anchor = null;
        if (save == null || caveExit?.itemData == null || caveAddress.DimensionId != WorldAddress.CaveDimensionId)
            return false;

        JObject dimensionData = ItemSpecialDataJsonStore.ReadRoot(save.DimensionStateJson);
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

            candidate.CaveExitPosition = WorldLocalPresentation.ToLogical(caveExit.transform.position);
            anchor = candidate;
            return true;
        }

        return false;
    }

    public static void UpdateCaveExit(GameSaveData save, DimensionPortalAnchor anchor, Item caveExit)
    {
        if (save == null || anchor == null || caveExit?.itemData == null || string.IsNullOrWhiteSpace(anchor.AnchorId))
            return;

        anchor.CaveExitGuid = caveExit.itemData.Guid;
        anchor.CaveExitPosition = WorldLocalPresentation.ToLogical(caveExit.transform.position);
        JObject dimensionData = ItemSpecialDataJsonStore.ReadRoot(save.DimensionStateJson);
        JObject anchors = dimensionData[PortalAnchorsKey] as JObject ?? new JObject();
        anchors[anchor.AnchorId] = SerializeAnchor(anchor);
        dimensionData[PortalAnchorsKey] = anchors;
        save.DimensionStateJson = dimensionData.ToString(Newtonsoft.Json.Formatting.None);
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

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
