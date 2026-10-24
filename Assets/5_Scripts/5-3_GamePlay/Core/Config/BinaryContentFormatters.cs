using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine.Scripting;

#region 纯世界占地定义适配

[Preserve]
public sealed class BinaryGridOccupancyAttribute : MemoryPackCustomFormatterAttribute<BinaryGridOccupancyFormatter, WorldGridOccupancyData>
{
    public override BinaryGridOccupancyFormatter GetFormatter() => new();
}

/// <summary>在配置边界适配二进制格式，纯世界模型不依赖具体序列化库。</summary>
[Preserve]
public sealed class BinaryGridOccupancyFormatter : MemoryPackFormatter<WorldGridOccupancyData>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref WorldGridOccupancyData value)
    {
        writer.WriteUnmanaged(value != null);
        if (value == null) return;
        if (value.Cells == null) { writer.WriteNullCollectionHeader(); return; }
        writer.WriteCollectionHeader(value.Cells.Count);
        foreach (WorldGridOccupancyCellData cell in value.Cells)
        {
            writer.WriteUnmanaged(cell != null);
            if (cell != null) writer.WriteUnmanaged(cell.X, cell.Y);
        }
    }

    public override void Deserialize(ref MemoryPackReader reader, ref WorldGridOccupancyData value)
    {
        if (!reader.ReadUnmanaged<bool>()) { value = null; return; }
        value = new WorldGridOccupancyData();
        if (!reader.TryReadCollectionHeader(out int count)) { value.Cells = null; return; }
        if (count < 0 || count > 1000000) throw new InvalidDataException("缓存占地数量无效");
        value.Cells = new List<WorldGridOccupancyCellData>(count);
        for (int i = 0; i < count; i++)
            value.Cells.Add(reader.ReadUnmanaged<bool>() ? new WorldGridOccupancyCellData
            { X = reader.ReadUnmanaged<int>(), Y = reader.ReadUnmanaged<int>() } : null);
    }
}

#endregion
