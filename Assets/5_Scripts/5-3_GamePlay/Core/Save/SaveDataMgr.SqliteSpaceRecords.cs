using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlatWorld.Persistence;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public partial class SaveDataMgr
{
    #region 星区实体的独立持久记录
    private static readonly (string field, string identity)[] SpaceEntityContracts =
    {
        ("Ships", "ShipId"), ("Flights", "ShipId"), ("Devices", "PieceId"),
        ("Passengers", "ProfileId"), ("Navigation", "ShipId"), ("Docking", "PairId"),
        ("LooseItems", "Id"), ("OreAsteroids", "AsteroidId"), ("GroundAtmospheres", "WorldKey"),
        ("Elevators", "ElevatorId"), ("LocalWorldStates", "WorldKey")
    };

    // 大型会话集合按现有身份分行，头记录保留时钟、目录与未知扩展属性。
    private static void AppendSqliteSpaceRecords(List<SaveArchiveRecord> records, string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        JObject header = JObject.Parse(json);
        foreach (var contract in SpaceEntityContracts)
        {
            if (header[contract.field] is not JArray entries) continue;
            foreach (JObject entry in entries)
            {
                string id = entry.Value<string>(contract.identity);
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("太空实体缺少稳定身份：" + contract.field);
                string world = entry.Value<string>("WorldKey") ?? entry.Value<string>("SurfaceWorldKey") ??
                    (entry.Value<string>("ShipId") is string ship ? "ship:" + ship : "");
                records.Add(ArchiveRecord("entity", world, "space:" + contract.field + ":" + id,
                    Encoding.UTF8.GetBytes(entry.ToString(Formatting.None))));
            }
            header[contract.field] = new JArray();
        }
        records.Add(ArchiveRecord("scope", "", "space", Encoding.UTF8.GetBytes(header.ToString(Formatting.None))));
    }

    private static string RestoreSqliteSpaceRecords(string json, IReadOnlyList<SaveArchiveRecord> records)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        JObject header = JObject.Parse(json);
        foreach (var contract in SpaceEntityContracts)
        {
            string prefix = "space:" + contract.field + ":";
            var entries = new JArray();
            foreach (var record in records)
                if (record.SchemaId == "flatworld.space" && record.Key.StartsWith(prefix, StringComparison.Ordinal))
                    entries.Add(JObject.Parse(Encoding.UTF8.GetString(record.Payload)));
            header[contract.field] = entries;
        }
        return header.ToString(Formatting.None);
    }

    private static string CaptureBootstrapSpaceMetadata(string json, string worldKey)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        JObject header = JObject.Parse(json);
        foreach (var contract in SpaceEntityContracts) header[contract.field] = new JArray();
        if (header["Sectors"] is JArray sectors && sectors.Count > 0)
        {
            JToken selected = sectors[0];
            foreach (JObject sector in sectors)
                if (sector["Bodies"] is JArray bodies)
                    foreach (JObject body in bodies)
                        if (body.Value<string>("PlanetId") == worldKey) selected = sector;
            header["Sectors"] = new JArray(selected.DeepClone());
        }
        header["GeneratedOreAsteroidSectors"] = new JArray();
        return header.ToString(Formatting.None);
    }
    #endregion
}
