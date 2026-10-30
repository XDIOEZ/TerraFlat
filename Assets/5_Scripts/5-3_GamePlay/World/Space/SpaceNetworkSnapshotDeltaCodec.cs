using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace FlatWorld.Spaceflight
{
    public sealed class SpaceNetworkSnapshotDeltaCursor
    {
        #region 每连接已发送基线
        internal Dictionary<SpaceNetworkSnapshotRecordKey, SpaceNetworkSnapshotRecord> Records = new();
        internal ulong Sequence;
        public void Reset() { Records.Clear(); Sequence = 0; }
        #endregion
    }

    public sealed class SpaceNetworkSnapshotDeltaPacket
    {
        #region 成功发送后提交基线
        public byte[] Payload { get; internal set; }
        internal ulong Sequence;
        internal Dictionary<SpaceNetworkSnapshotRecordKey, SpaceNetworkSnapshotRecord> Records;
        public void ConfirmSent(SpaceNetworkSnapshotDeltaCursor cursor)
        { cursor.Records = Records; cursor.Sequence = Sequence; }
        #endregion
    }

    internal enum SpaceNetworkSnapshotRecordKind : byte
    { Ship, Piece, Flight, Device, Passenger, Navigation, Docking, LooseItem, Asteroid, Sector, Body, GroundAtmosphere, Compartment, LocalWorld }

    internal readonly struct SpaceNetworkSnapshotRecordKey : IEquatable<SpaceNetworkSnapshotRecordKey>
    {
        #region 实体身份
        internal readonly SpaceNetworkSnapshotRecordKind Kind;
        internal readonly string Id;
        internal SpaceNetworkSnapshotRecordKey(SpaceNetworkSnapshotRecordKind kind, string id) { Kind = kind; Id = id; }
        public bool Equals(SpaceNetworkSnapshotRecordKey other) => Kind == other.Kind && Id == other.Id;
        public override bool Equals(object value) => value is SpaceNetworkSnapshotRecordKey other && Equals(other);
        public override int GetHashCode() => unchecked((int)Kind * 397 ^ StringComparer.Ordinal.GetHashCode(Id));
        #endregion
    }

    internal sealed class SpaceNetworkSnapshotField
    {
        #region 冻结字段载荷
        internal JToken Value;
        internal byte Encoding;
        internal byte[] Payload;
        internal int ExpandedBytes;
        #endregion
    }

    internal sealed class SpaceNetworkSnapshotRecord
    {
        #region 冻结实体字段
        internal readonly Dictionary<string, SpaceNetworkSnapshotField> Fields = new(StringComparer.Ordinal);
        #endregion
    }

    public sealed class SpaceNetworkSnapshotDeltaCodec
    {
        #region 每轮共享字段编码
        internal const int MaxPayloadBytes = 8 * 1024 * 1024;
        internal const int MaxExpandedBytes = 64 * 1024 * 1024;
        private const string ParentField = "@parent";
        private sealed class CachedRecord
        {
            internal long Batch;
            internal SpaceNetworkSnapshotRecord Record;
        }
        private sealed class ContainerRecordResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization serialization)
            {
                JsonProperty property = base.CreateProperty(member, serialization);
                // 船件、舱室与星体独立追踪，运动和气体变化不重发结构。
                if (member.DeclaringType == typeof(ShipState) && (member.Name == nameof(ShipState.Pieces) || member.Name == nameof(ShipState.Compartments)) ||
                    member.DeclaringType == typeof(UniverseState) && member.Name == nameof(UniverseState.Bodies)) property.Ignored = true;
                return property;
            }
        }
        private readonly JsonSerializer containerSerializer = JsonSerializer.Create(new JsonSerializerSettings
        { ContractResolver = new ContainerRecordResolver() });
        private readonly JsonSerializer serializer = JsonSerializer.CreateDefault();
        private readonly Dictionary<SpaceNetworkSnapshotRecordKey, CachedRecord> cache = new();
        private readonly List<SpaceNetworkSnapshotRecordKey> expired = new();
        private long batch;
        public int CapturedEntityCount { get; private set; }
        public int EncodedFieldCount { get; private set; }
        public int CompressedFieldCount { get; private set; }

        public void BeginCaptureBatch()
        {
            batch++;
            CapturedEntityCount = EncodedFieldCount = CompressedFieldCount = 0;
            expired.Clear();
            foreach (var pair in cache) if (pair.Value.Batch < batch - 2) expired.Add(pair.Key);
            foreach (var key in expired) cache.Remove(key);
        }

        private SpaceNetworkSnapshotRecord CaptureRecord(SpaceNetworkSnapshotRecordKey key, object source, string parent)
        {
            cache.TryGetValue(key, out CachedRecord previous);
            if (previous?.Batch == batch) return previous.Record;
            CapturedEntityCount++;
            JObject document = JObject.FromObject(source, key.Kind is SpaceNetworkSnapshotRecordKind.Ship or SpaceNetworkSnapshotRecordKind.Sector
                ? containerSerializer : serializer);
            if (parent != null) document[ParentField] = parent;
            SpaceNetworkSnapshotRecord record = previous?.Record;
            foreach (JProperty property in document.Properties())
            {
                if (previous != null && previous.Record.Fields.TryGetValue(property.Name, out var field) && JToken.DeepEquals(field.Value, property.Value))
                    continue;
                if (record == null || ReferenceEquals(record, previous?.Record))
                {
                    record = new SpaceNetworkSnapshotRecord();
                    if (previous != null) foreach (var old in previous.Record.Fields) record.Fields.Add(old.Key, old.Value);
                }
                record.Fields[property.Name] = EncodeField(property.Value);
            }
            if (previous == null) cache.Add(key, previous = new CachedRecord());
            previous.Batch = batch; previous.Record = record;
            return record;
        }

        private SpaceNetworkSnapshotField EncodeField(JToken value)
        {
            EncodedFieldCount++;
            var field = new SpaceNetworkSnapshotField { Value = value.Type == JTokenType.Bytes
                ? new JValue((byte[])value.Value<byte[]>().Clone()) : value.DeepClone(), Payload = Array.Empty<byte>() };
            switch (value.Type)
            {
                case JTokenType.Null: field.Encoding = 0; break;
                case JTokenType.Boolean: field.Encoding = value.Value<bool>() ? (byte)2 : (byte)1; break;
                case JTokenType.Integer:
                    bool unsigned = ((JValue)value).Value is ulong;
                    field.Encoding = unsigned ? (byte)4 : (byte)3;
                    field.Payload = unsigned ? BitConverter.GetBytes(value.Value<ulong>()) : BitConverter.GetBytes(value.Value<long>());
                    break;
                case JTokenType.Float: field.Encoding = 5; field.Payload = BitConverter.GetBytes(value.Value<double>()); break;
                case JTokenType.String: field.Encoding = 6; field.Payload = Encoding.UTF8.GetBytes(value.Value<string>()); break;
                default: field.Encoding = 7; field.Payload = Encoding.UTF8.GetBytes(value.ToString(Formatting.None)); break;
            }
            field.ExpandedBytes = field.Payload.Length;
            if (field.Payload.Length >= 512)
            {
                using var stream = new MemoryStream();
                using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, true)) gzip.Write(field.Payload, 0, field.Payload.Length);
                if (stream.Length + 4 < field.Payload.Length)
                { field.Payload = stream.ToArray(); field.Encoding |= 128; CompressedFieldCount++; }
            }
            return field;
        }
        #endregion

        #region 按观察者发送差量
        public SpaceNetworkSnapshotDeltaPacket Encode(SpaceSessionState state, SpaceNetworkSnapshotDeltaCursor cursor, ulong sequence)
        {
            var visible = new Dictionary<SpaceNetworkSnapshotRecordKey, SpaceNetworkSnapshotRecord>();
            long expandedBytes = 0;
            var updates = new List<(SpaceNetworkSnapshotRecordKey Key, SpaceNetworkSnapshotRecord Record, SpaceNetworkSnapshotRecord Previous)>();
            void Add(SpaceNetworkSnapshotRecordKind kind, string id, object value, string parent = null)
            {
                var key = new SpaceNetworkSnapshotRecordKey(kind, id);
                SpaceNetworkSnapshotRecord record = CaptureRecord(key, value, parent);
                foreach (var field in record.Fields.Values) expandedBytes += field.ExpandedBytes;
                if (expandedBytes > MaxExpandedBytes) throw new InvalidDataException("太空同步状态超过未压缩数据限额。");
                cursor.Records.TryGetValue(key, out var previous);
                if (kind != SpaceNetworkSnapshotRecordKind.LocalWorld) visible.Add(key, record);
                if (!ReferenceEquals(record, previous)) updates.Add((key, record, previous));
            }
            foreach (ShipState ship in state.Ships)
            {
                Add(SpaceNetworkSnapshotRecordKind.Ship, ship.ShipId, ship);
                foreach (ShipPieceState piece in ship.Pieces) Add(SpaceNetworkSnapshotRecordKind.Piece, piece.PieceId, piece, ship.ShipId);
                foreach (ShipCompartmentState room in ship.Compartments) Add(SpaceNetworkSnapshotRecordKind.Compartment, room.CompartmentId, room, ship.ShipId);
            }
            foreach (UniverseState sector in state.Sectors)
            {
                string id = sector.Sector.ToString();
                Add(SpaceNetworkSnapshotRecordKind.Sector, id, sector);
                foreach (BodyState body in sector.Bodies) Add(SpaceNetworkSnapshotRecordKind.Body, body.BodyId, body, id);
            }
            foreach (var value in state.Flights) Add(SpaceNetworkSnapshotRecordKind.Flight, value.ShipId, value);
            foreach (var value in state.Devices) Add(SpaceNetworkSnapshotRecordKind.Device, value.PieceId, value);
            foreach (var value in state.Passengers) Add(SpaceNetworkSnapshotRecordKind.Passenger, value.ProfileId, value);
            foreach (var value in state.Navigation) Add(SpaceNetworkSnapshotRecordKind.Navigation, value.ShipId, value);
            foreach (var value in state.Docking) Add(SpaceNetworkSnapshotRecordKind.Docking, value.PairId, value);
            foreach (var value in state.LooseItems) Add(SpaceNetworkSnapshotRecordKind.LooseItem, value.Id, value);
            foreach (var value in state.OreAsteroids) Add(SpaceNetworkSnapshotRecordKind.Asteroid, value.AsteroidId, value);
            foreach (var value in state.GroundAtmospheres) Add(SpaceNetworkSnapshotRecordKind.GroundAtmosphere, value.WorldKey, value);
            foreach (var value in state.LocalWorldStates) Add(SpaceNetworkSnapshotRecordKind.LocalWorld, value.WorldKey, value);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(cursor.Sequence);
            writer.Write(state.SimulationSeconds);
            writer.Write(state.NavigationSequence);
            int removed = 0;
            foreach (var key in cursor.Records.Keys) if (!visible.ContainsKey(key)) removed++;
            writer.Write(removed);
            foreach (var key in cursor.Records.Keys) if (!visible.ContainsKey(key)) WriteKey(writer, key);
            writer.Write(updates.Count);
            foreach (var update in updates)
            {
                WriteKey(writer, update.Key);
                int count = 0;
                foreach (var field in update.Record.Fields)
                    if (update.Previous == null || !update.Previous.Fields.TryGetValue(field.Key, out var old) || !ReferenceEquals(old, field.Value)) count++;
                writer.Write(count);
                foreach (var field in update.Record.Fields)
                {
                    if (update.Previous != null && update.Previous.Fields.TryGetValue(field.Key, out var old) && ReferenceEquals(old, field.Value)) continue;
                    writer.Write(field.Key); writer.Write(field.Value.Encoding);
                    byte type = (byte)(field.Value.Encoding & 127);
                    if (type < 3) continue;
                    if ((field.Value.Encoding & 128) != 0) writer.Write(field.Value.ExpandedBytes);
                    if (type >= 6) writer.Write(field.Value.Payload.Length);
                    writer.Write(field.Value.Payload);
                }
            }
            if (stream.Length > MaxPayloadBytes) throw new InvalidDataException("太空同步差量超过数据限额。");
            return new SpaceNetworkSnapshotDeltaPacket { Payload = stream.ToArray(), Sequence = sequence, Records = visible };
        }

        private static void WriteKey(BinaryWriter writer, SpaceNetworkSnapshotRecordKey key)
        { writer.Write((byte)key.Kind); writer.Write(key.Id); }
        #endregion

        public sealed class Receiver
        {
            #region 独立于表现状态的接收基线
            private Dictionary<SpaceNetworkSnapshotRecordKey, JObject> records = new();
            private ulong sequence;
            public sealed class PendingSnapshot
            {
                public SpaceSessionState State { get; internal set; }
                public bool IsFullSnapshot { get; internal set; }
                internal ulong Sequence;
                internal Dictionary<SpaceNetworkSnapshotRecordKey, JObject> Records;
            }
            public void Reset() { records.Clear(); sequence = 0; }
            public void ConfirmApplied(PendingSnapshot snapshot) { records = snapshot.Records; sequence = snapshot.Sequence; }

            public PendingSnapshot Decode(byte[] payload, ulong incomingSequence)
            {
                if (payload == null || payload.Length > MaxPayloadBytes) throw new InvalidDataException("太空同步载荷超出限额。");
                using var stream = new MemoryStream(payload, false);
                using var reader = new BinaryReader(stream, Encoding.UTF8);
                ulong baseline = reader.ReadUInt64();
                if (baseline != 0 && baseline != sequence) throw new InvalidDataException("太空差量缺少前序基线。");
                double seconds = reader.ReadDouble();
                long navigationSequence = reader.ReadInt64();
                var next = baseline == 0 ? new Dictionary<SpaceNetworkSnapshotRecordKey, JObject>() : new(records);
                var worlds = new JArray();
                for (int i = ReadCount(reader); i > 0; i--) next.Remove(ReadKey(reader));
                long expandedBytes = 0;
                for (int i = ReadCount(reader); i > 0; i--)
                {
                    var key = ReadKey(reader);
                    JObject value = next.TryGetValue(key, out JObject previous) ? (JObject)previous.DeepClone() : new JObject();
                    for (int fields = ReadCount(reader); fields > 0; fields--)
                    {
                        string name = reader.ReadString();
                        value[name] = ReadField(reader, ref expandedBytes);
                    }
                    if (key.Kind == SpaceNetworkSnapshotRecordKind.LocalWorld) worlds.Add(value);
                    else next[key] = value;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("太空差量存在多余数据。");
                SpaceSessionState state = BuildState(next, worlds, seconds, navigationSequence);
                return new PendingSnapshot { State = state, IsFullSnapshot = baseline == 0, Sequence = incomingSequence, Records = next };
            }

            private static int ReadCount(BinaryReader reader)
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("太空差量数量无效。");
                return count;
            }
            private static SpaceNetworkSnapshotRecordKey ReadKey(BinaryReader reader)
            {
                byte kind = reader.ReadByte();
                if (kind > (byte)SpaceNetworkSnapshotRecordKind.LocalWorld) throw new InvalidDataException("太空差量实体类型无效。");
                return new SpaceNetworkSnapshotRecordKey((SpaceNetworkSnapshotRecordKind)kind, reader.ReadString());
            }
            private static JToken ReadField(BinaryReader reader, ref long expandedBytes)
            {
                byte encoding = reader.ReadByte(), type = (byte)(encoding & 127);
                if (type == 0) return JValue.CreateNull();
                if (type == 1 || type == 2) return new JValue(type == 2);
                bool compressed = (encoding & 128) != 0;
                if (type > 7 || compressed && type < 6) throw new InvalidDataException("太空差量字段类型无效。");
                int expanded = compressed ? reader.ReadInt32() : 0;
                int length = type >= 6 ? reader.ReadInt32() : 8;
                if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                    throw new InvalidDataException("太空差量字段长度无效。");
                expandedBytes += compressed ? expanded : length;
                if (expanded < 0 || expandedBytes > MaxExpandedBytes) throw new InvalidDataException("太空差量解压数据超出限额。");
                byte[] bytes = reader.ReadBytes(length);
                if (compressed)
                {
                    using var source = new MemoryStream(bytes, false);
                    using var gzip = new GZipStream(source, CompressionMode.Decompress);
                    bytes = new byte[expanded];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = gzip.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0) break;
                        offset += read;
                    }
                    if (offset != bytes.Length || gzip.ReadByte() != -1) throw new InvalidDataException("太空差量解压长度不一致。");
                }
                return type switch
                {
                    3 => new JValue(BitConverter.ToInt64(bytes, 0)),
                    4 => new JValue(BitConverter.ToUInt64(bytes, 0)),
                    5 => new JValue(BitConverter.ToDouble(bytes, 0)),
                    6 => new JValue(new UTF8Encoding(false, true).GetString(bytes)),
                    _ => JToken.Parse(new UTF8Encoding(false, true).GetString(bytes))
                };
            }
            #endregion

            #region 重建完整可见集合并隔离可变表现对象
            private static SpaceSessionState BuildState(Dictionary<SpaceNetworkSnapshotRecordKey, JObject> values, JArray worlds,
                double seconds, long navigationSequence)
            {
                var groups = new Dictionary<SpaceNetworkSnapshotRecordKind, JArray>();
                var pieces = new Dictionary<string, JArray>(StringComparer.Ordinal);
                var bodies = new Dictionary<string, JArray>(StringComparer.Ordinal);
                var compartments = new Dictionary<string, JArray>(StringComparer.Ordinal);
                foreach (var pair in values)
                {
                    JObject value = (JObject)pair.Value.DeepClone();
                    if (pair.Key.Kind is SpaceNetworkSnapshotRecordKind.Piece or SpaceNetworkSnapshotRecordKind.Body or SpaceNetworkSnapshotRecordKind.Compartment)
                    {
                        string parent = value.Value<string>(ParentField);
                        value.Remove(ParentField);
                        var children = pair.Key.Kind == SpaceNetworkSnapshotRecordKind.Piece ? pieces :
                            pair.Key.Kind == SpaceNetworkSnapshotRecordKind.Body ? bodies : compartments;
                        if (parent == null) throw new InvalidDataException("太空差量子实体缺少归属。");
                        if (!children.TryGetValue(parent, out var list)) children.Add(parent, list = new JArray());
                        list.Add(value);
                    }
                    else
                    {
                        if (!groups.TryGetValue(pair.Key.Kind, out var list)) groups.Add(pair.Key.Kind, list = new JArray());
                        list.Add(value);
                    }
                }
                JArray Group(SpaceNetworkSnapshotRecordKind kind) => groups.TryGetValue(kind, out var list) ? list : new JArray();
                JArray ships = Group(SpaceNetworkSnapshotRecordKind.Ship), sectors = Group(SpaceNetworkSnapshotRecordKind.Sector);
                foreach (JObject ship in ships)
                {
                    string id = ship.Value<string>(nameof(ShipState.ShipId));
                    ship[nameof(ShipState.Pieces)] = pieces.TryGetValue(id, out var list) ? list : new JArray();
                    ship[nameof(ShipState.Compartments)] = compartments.TryGetValue(id, out var rooms) ? rooms : new JArray();
                }
                foreach (JObject sector in sectors)
                {
                    string id = sector[nameof(UniverseState.Sector)].ToObject<SpaceSectorId>().ToString();
                    sector[nameof(UniverseState.Bodies)] = bodies.TryGetValue(id, out var list) ? list : new JArray();
                }
                var document = new JObject
                {
                    [nameof(SpaceSessionState.Version)] = SpaceSessionState.CurrentVersion,
                    [nameof(SpaceSessionState.SimulationSeconds)] = seconds,
                    [nameof(SpaceSessionState.NavigationSequence)] = navigationSequence,
                    [nameof(SpaceSessionState.Ships)] = ships, [nameof(SpaceSessionState.Sectors)] = sectors,
                    [nameof(SpaceSessionState.Flights)] = Group(SpaceNetworkSnapshotRecordKind.Flight),
                    [nameof(SpaceSessionState.Devices)] = Group(SpaceNetworkSnapshotRecordKind.Device),
                    [nameof(SpaceSessionState.Passengers)] = Group(SpaceNetworkSnapshotRecordKind.Passenger),
                    [nameof(SpaceSessionState.Navigation)] = Group(SpaceNetworkSnapshotRecordKind.Navigation),
                    [nameof(SpaceSessionState.Docking)] = Group(SpaceNetworkSnapshotRecordKind.Docking),
                    [nameof(SpaceSessionState.LooseItems)] = Group(SpaceNetworkSnapshotRecordKind.LooseItem),
                    [nameof(SpaceSessionState.OreAsteroids)] = Group(SpaceNetworkSnapshotRecordKind.Asteroid),
                    [nameof(SpaceSessionState.GroundAtmospheres)] = Group(SpaceNetworkSnapshotRecordKind.GroundAtmosphere),
                    [nameof(SpaceSessionState.LocalWorldStates)] = worlds
                };
                var state = document.ToObject<SpaceSessionState>();
                if (!double.IsFinite(seconds) || seconds < 0d || state.Sectors.Count == 0) throw new InvalidDataException("太空差量的时钟或星区无效。");
                var sectorIds = new HashSet<SpaceSectorId>();
                foreach (UniverseState sector in state.Sectors)
                    if (!sectorIds.Add(sector.Sector)) throw new InvalidDataException("太空差量星区身份重复。");
                return state;
            }
            #endregion
        }
    }
}
