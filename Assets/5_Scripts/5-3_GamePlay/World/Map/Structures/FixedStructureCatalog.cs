using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace FlatWorld.Structures
{
    #region 固定结构配置模型

    public sealed class FixedStructureDefinition
    {
        [JsonProperty(Required = Required.Always)] public string Id { get; set; }
        [JsonProperty(Required = Required.Always)] public string DisplayName { get; set; }
        [JsonProperty(Required = Required.Always)] public string Kind { get; set; }
        [JsonProperty(Required = Required.Always)] public int Width { get; set; }
        [JsonProperty(Required = Required.Always)] public int Height { get; set; }
        public int Clearance { get; set; } = 1;
        public int SearchRadius { get; set; } = 24;
        public FixedStructureGas CabinGas { get; set; }
        [JsonProperty(Required = Required.Always)] public List<FixedStructureStamp> Items { get; set; }
        [JsonIgnore] public List<FixedStructureMember> Members { get; } = new();
    }

    public sealed class FixedStructureStamp
    {
        [JsonProperty(Required = Required.Always)] public string MemberId { get; set; }
        [JsonProperty(Required = Required.Always)] public string ItemId { get; set; }
        [JsonProperty(Required = Required.Always)] public int X { get; set; }
        [JsonProperty(Required = Required.Always)] public int Y { get; set; }
        public int Width { get; set; } = 1;
        public int Height { get; set; } = 1;
        public int QuarterTurns { get; set; }
        public FixedStructureResources Resources { get; set; }
    }

    public sealed class FixedStructureMember
    {
        public string MemberId { get; set; }
        public string ItemId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int QuarterTurns { get; set; }
        public FixedStructureResources Resources { get; set; }
    }

    public sealed class FixedStructureResources
    {
        public double? BatteryChargeRatio { get; set; }
        public FixedStructureGas Gas { get; set; }
        public Dictionary<string, decimal> Liquids { get; set; }
        public List<FixedStructureInventory> Inventories { get; set; }
    }

    public sealed class FixedStructureGas
    {
        [JsonProperty(Required = Required.Always)] public double PressureKPa { get; set; }
        public double TemperatureCelsius { get; set; } = 20d;
        [JsonProperty(Required = Required.Always)] public Dictionary<string, double> Components { get; set; }
    }

    public sealed class FixedStructureInventory
    {
        [JsonProperty(Required = Required.Always)] public string Name { get; set; }
        [JsonProperty(Required = Required.Always)] public List<FixedStructureInventoryItem> Items { get; set; }
    }

    public sealed class FixedStructureInventoryItem
    {
        [JsonProperty(Required = Required.Always)] public string ItemId { get; set; }
        [JsonProperty(Required = Required.Always)] public decimal Amount { get; set; }
    }

    #endregion

    public static class FixedStructureCatalog
    {
        #region 跨平台读取与严格解析

        public const string ConfigPath = "GameConfig/Structures/fixed-structures.json";
        public const int MaximumSize = 32;
        public const int MaximumSearchRadius = 64;
        public const int MaximumExpandedMembers = 4096;

        private sealed class Document
        {
            [JsonProperty(Required = Required.Always)] public int Version { get; set; }
            [JsonProperty(Required = Required.Always)] public List<FixedStructureDefinition> Structures { get; set; }
        }

        // 目录只负责解析和范围检查，资源身份与设备容量由实际生成方核实。
        public static IEnumerator LoadAsync(Action<IReadOnlyList<FixedStructureDefinition>> completed,
            Action<Exception> failed)
        {
            string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, ConfigPath);
            string json = null;
            Exception error = null;
            yield return StreamingAssetsTextLoader.ReadAllTextAsync(path, value => json = value, value => error = value);
            if (error != null) { failed?.Invoke(error); yield break; }
            yield return StreamingAssetsTextLoader.RunPureDataAsync(() => Parse(json), completed, failed);
        }

        public static IReadOnlyList<FixedStructureDefinition> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("固定结构目录为空。");
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                MissingMemberHandling = MissingMemberHandling.Error,
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                MaxDepth = 32
            };
            using var input = new StringReader(json);
            using var reader = new JsonTextReader(input) { MaxDepth = 32 };
            JObject root = JObject.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (reader.Read()) throw new InvalidDataException("固定结构目录包含多段 JSON。");
            Document document = root.ToObject<Document>(JsonSerializer.Create(settings));
            if (document == null || document.Version != 1 || document.Structures == null || document.Structures.Count == 0)
                throw new InvalidDataException("固定结构目录版本或结构列表无效。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken value in root["structures"] ?? throw new InvalidDataException("固定结构列表缺失。"))
            {
                if (value is not JObject definitionJson) throw new InvalidDataException("固定结构必须是 JSON 对象。");
                foreach (JProperty property in definitionJson.Properties())
                    if (string.Equals(property.Name, "members", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("members 是展开结果，请通过 items 定义结构。");
            }
            foreach (FixedStructureDefinition definition in document.Structures)
            {
                if (definition == null || !ValidId(definition.Id) || !ids.Add(definition.Id) ||
                    string.IsNullOrWhiteSpace(definition.DisplayName) || !ValidId(definition.Kind) ||
                    definition.Width < 1 || definition.Width > MaximumSize ||
                    definition.Height < 1 || definition.Height > MaximumSize ||
                    definition.Clearance < 0 || definition.Clearance > MaximumSize ||
                    definition.SearchRadius < 0 || definition.SearchRadius > MaximumSearchRadius ||
                    definition.Items == null || definition.Items.Count == 0)
                    throw new InvalidDataException("固定结构身份、尺寸、选址范围或成员列表无效。");
                ValidateGas(definition.CabinGas, definition.Id);
                Expand(definition);
            }
            return document.Structures.AsReadOnly();
        }

        #endregion

        #region 矩形成员展开与资源形状校验

        private static void Expand(FixedStructureDefinition definition)
        {
            var stampIds = new HashSet<string>(StringComparer.Ordinal);
            var memberIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FixedStructureStamp stamp in definition.Items)
            {
                if (stamp == null || !ValidId(stamp.MemberId) || !stampIds.Add(stamp.MemberId) || !ValidId(stamp.ItemId) ||
                    stamp.X < 0 || stamp.Y < 0 || stamp.Width < 1 || stamp.Height < 1 ||
                    stamp.Width > definition.Width || stamp.Height > definition.Height ||
                    stamp.X > definition.Width - stamp.Width || stamp.Y > definition.Height - stamp.Height ||
                    stamp.QuarterTurns < 0 || stamp.QuarterTurns > 3 ||
                    definition.Members.Count + stamp.Width * stamp.Height > MaximumExpandedMembers)
                    throw new InvalidDataException($"固定结构 {definition.Id} 的矩形成员无效或身份重复。");
                ValidateResources(stamp.Resources, definition.Id + "/" + stamp.MemberId);
                for (int y = stamp.Y; y < stamp.Y + stamp.Height; y++)
                for (int x = stamp.X; x < stamp.X + stamp.Width; x++)
                {
                    string id = stamp.Width == 1 && stamp.Height == 1 ? stamp.MemberId : $"{stamp.MemberId}:{x}:{y}";
                    if (!memberIds.Add(id)) throw new InvalidDataException($"固定结构 {definition.Id} 的展开成员身份重复：{id}");
                    definition.Members.Add(new FixedStructureMember
                    {
                        MemberId = id, ItemId = stamp.ItemId, X = x, Y = y,
                        QuarterTurns = stamp.QuarterTurns, Resources = stamp.Resources
                    });
                }
            }
        }

        private static void ValidateResources(FixedStructureResources resources, string context)
        {
            if (resources == null) return;
            if (resources.BatteryChargeRatio.HasValue && (!Finite(resources.BatteryChargeRatio.Value) ||
                resources.BatteryChargeRatio.Value < 0d || resources.BatteryChargeRatio.Value > 1d))
                throw new InvalidDataException($"{context} 的电池预充比例无效。");
            ValidateGas(resources.Gas, context);
            if (resources.Liquids != null)
                foreach (var pair in resources.Liquids)
                    if (!ValidId(pair.Key) || pair.Value <= 0m)
                        throw new InvalidDataException($"{context} 的液体身份或升数无效。");
            if (resources.Inventories == null) return;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (FixedStructureInventory inventory in resources.Inventories)
            {
                if (inventory == null || !ValidName(inventory.Name) || !names.Add(inventory.Name) ||
                    inventory.Items == null || inventory.Items.Count == 0)
                    throw new InvalidDataException($"{context} 的库存名称或物品列表无效。");
                foreach (FixedStructureInventoryItem item in inventory.Items)
                    if (item == null || !ValidId(item.ItemId) || item.Amount <= 0m)
                        throw new InvalidDataException($"{context} 的库存物品身份或数量无效。");
            }
        }

        private static void ValidateGas(FixedStructureGas gas, string context)
        {
            if (gas == null) return;
            if (!Finite(gas.PressureKPa) || gas.PressureKPa <= 0d || !Finite(gas.TemperatureCelsius) ||
                gas.TemperatureCelsius <= -273.15d || gas.Components == null || gas.Components.Count == 0)
                throw new InvalidDataException($"{context} 的气压、温度或气体配比无效。");
            double total = 0d;
            foreach (var pair in gas.Components)
            {
                if (!ValidId(pair.Key) || !Finite(pair.Value) || pair.Value <= 0d || pair.Value > 1d)
                    throw new InvalidDataException($"{context} 的气体身份或比例无效。");
                total += pair.Value;
            }
            if (Math.Abs(total - 1d) > 0.000001d)
                throw new InvalidDataException($"{context} 的气体配比之和必须为 1。");
        }

        private static bool ValidId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value != value.Trim()) return false;
            foreach (char character in value) if (char.IsWhiteSpace(character) || char.IsControl(character)) return false;
            return true;
        }

        private static bool ValidName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value != value.Trim()) return false;
            foreach (char character in value) if (char.IsControl(character)) return false;
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        #endregion
    }
}
