using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine.Scripting;

#region JSON 扩展参数的二进制树

/// <summary>直接保存参数树的值和类型，恢复时不再生成或解析 JSON 字符串。</summary>
[Preserve]
public sealed class BinaryJsonObjectAttribute : MemoryPackCustomFormatterAttribute<BinaryJsonObjectFormatter, JObject>
{
    public override BinaryJsonObjectFormatter GetFormatter() => new();
}

[Preserve]
public sealed class BinaryJsonObjectFormatter : MemoryPackFormatter<JObject>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref JObject value)
        => WriteToken(ref writer, value, 0);

    public override void Deserialize(ref MemoryPackReader reader, ref JObject value)
    {
        JToken token = ReadToken(ref reader, 0);
        if (token != null && token is not JObject) throw new InvalidDataException("缓存参数根节点不是对象");
        value = (JObject)token;
    }

    internal static void WriteToken<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, JToken token, int depth)
        where TBufferWriter : class, IBufferWriter<byte>
    {
        if (depth > 64) throw new InvalidDataException("缓存参数嵌套过深");
        writer.WriteUnmanaged(token == null ? byte.MaxValue : (byte)token.Type);
        if (token == null) return;
        switch (token.Type)
        {
            case JTokenType.Object:
                var obj = (JObject)token;
                writer.WriteCollectionHeader(obj.Count);
                foreach (JProperty property in obj.Properties())
                { writer.WriteString(property.Name); WriteToken(ref writer, property.Value, depth + 1); }
                return;
            case JTokenType.Array:
                var array = (JArray)token;
                writer.WriteCollectionHeader(array.Count);
                foreach (JToken child in array) WriteToken(ref writer, child, depth + 1);
                return;
            case JTokenType.Integer:
                object integer = ((JValue)token).Value;
                byte kind = integer is BigInteger ? (byte)2 : integer is ulong ? (byte)1 : (byte)0;
                writer.WriteUnmanaged(kind);
                if (kind == 2) writer.WriteString(((BigInteger)integer).ToString(CultureInfo.InvariantCulture));
                else if (kind == 1) writer.WriteUnmanaged((ulong)integer);
                else writer.WriteUnmanaged(Convert.ToInt64(integer, CultureInfo.InvariantCulture));
                return;
            case JTokenType.Float:
                object number = ((JValue)token).Value;
                writer.WriteUnmanaged(number is decimal);
                if (number is decimal exact) writer.WriteUnmanaged(exact);
                else writer.WriteUnmanaged(Convert.ToDouble(number, CultureInfo.InvariantCulture));
                return;
            case JTokenType.String: writer.WriteString(token.Value<string>()); return;
            case JTokenType.Boolean: writer.WriteUnmanaged(token.Value<bool>()); return;
            case JTokenType.Null:
            case JTokenType.Undefined: return;
            case JTokenType.Date:
                object date = ((JValue)token).Value;
                writer.WriteUnmanaged(date is DateTimeOffset);
                if (date is DateTimeOffset offset) writer.WriteValue(offset);
                else writer.WriteValue((DateTime)date);
                return;
            case JTokenType.Bytes: writer.WriteUnmanagedArray(token.Value<byte[]>()); return;
            case JTokenType.Guid: writer.WriteValue(token.Value<Guid>()); return;
            case JTokenType.TimeSpan: writer.WriteValue(token.Value<TimeSpan>()); return;
            case JTokenType.Uri: writer.WriteString(token.Value<Uri>().OriginalString); return;
            default: throw new InvalidDataException("缓存不支持此参数类型：" + token.Type);
        }
    }

    internal static JToken ReadToken(ref MemoryPackReader reader, int depth)
    {
        if (depth > 64) throw new InvalidDataException("缓存参数嵌套过深");
        byte tag = reader.ReadUnmanaged<byte>();
        if (tag == byte.MaxValue) return null;
        switch ((JTokenType)tag)
        {
            case JTokenType.Object:
                int properties = Count(ref reader);
                var obj = new JObject();
                for (int i = 0; i < properties; i++) obj.Add(reader.ReadString(), ReadToken(ref reader, depth + 1));
                return obj;
            case JTokenType.Array:
                int elements = Count(ref reader);
                var array = new JArray();
                for (int i = 0; i < elements; i++) array.Add(ReadToken(ref reader, depth + 1));
                return array;
            case JTokenType.Integer:
                byte kind = reader.ReadUnmanaged<byte>();
                if (kind == 0) return new JValue(reader.ReadUnmanaged<long>());
                if (kind == 1) return new JValue(reader.ReadUnmanaged<ulong>());
                if (kind == 2) return new JValue(BigInteger.Parse(reader.ReadString(), CultureInfo.InvariantCulture));
                throw new InvalidDataException("缓存整数类型无效");
            case JTokenType.Float:
                return reader.ReadUnmanaged<bool>() ? new JValue(reader.ReadUnmanaged<decimal>())
                    : new JValue(reader.ReadUnmanaged<double>());
            case JTokenType.String: return new JValue(reader.ReadString());
            case JTokenType.Boolean: return new JValue(reader.ReadUnmanaged<bool>());
            case JTokenType.Null: return JValue.CreateNull();
            case JTokenType.Undefined: return JValue.CreateUndefined();
            case JTokenType.Date:
                return reader.ReadUnmanaged<bool>() ? new JValue(reader.ReadValue<DateTimeOffset>())
                    : new JValue(reader.ReadValue<DateTime>());
            case JTokenType.Bytes: return new JValue(reader.ReadUnmanagedArray<byte>());
            case JTokenType.Guid: return new JValue(reader.ReadValue<Guid>());
            case JTokenType.TimeSpan: return new JValue(reader.ReadValue<TimeSpan>());
            case JTokenType.Uri: return new JValue(new Uri(reader.ReadString(), UriKind.RelativeOrAbsolute));
            default: throw new InvalidDataException("缓存参数类型无效：" + tag);
        }
    }

    internal static int Count(ref MemoryPackReader reader)
    {
        if (!reader.TryReadCollectionHeader(out int count) || count < 0 || count > 1000000)
            throw new InvalidDataException("缓存集合长度无效");
        return count;
    }
}

/// <summary>MOD 和通用扩展配置允许任意 JSON 值，保持空值、数组和数值精度。</summary>
[Preserve]
public sealed class BinaryJsonTokenFormatter : MemoryPackFormatter<JToken>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref JToken value)
        => BinaryJsonObjectFormatter.WriteToken(ref writer, value, 0);

    public override void Deserialize(ref MemoryPackReader reader, ref JToken value)
        => value = BinaryJsonObjectFormatter.ReadToken(ref reader, 0);
}

[Preserve]
public sealed class BinaryJsonObjectListAttribute : MemoryPackCustomFormatterAttribute<BinaryJsonObjectListFormatter, List<JObject>>
{
    public override BinaryJsonObjectListFormatter GetFormatter() => new();
}

[Preserve]
public sealed class BinaryJsonObjectListFormatter : MemoryPackFormatter<List<JObject>>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref List<JObject> value)
    {
        if (value == null) { writer.WriteNullCollectionHeader(); return; }
        writer.WriteCollectionHeader(value.Count);
        foreach (JObject item in value) BinaryJsonObjectFormatter.WriteToken(ref writer, item, 0);
    }

    public override void Deserialize(ref MemoryPackReader reader, ref List<JObject> value)
    {
        if (!reader.TryReadCollectionHeader(out int count)) { value = null; return; }
        if (count < 0 || count > 1000000) throw new InvalidDataException("缓存来源数量无效");
        value = new List<JObject>(count);
        for (int i = 0; i < count; i++)
            value.Add(BinaryJsonObjectFormatter.ReadToken(ref reader, 0) as JObject ??
                      throw new InvalidDataException("缓存来源不是对象"));
    }
}

#endregion
