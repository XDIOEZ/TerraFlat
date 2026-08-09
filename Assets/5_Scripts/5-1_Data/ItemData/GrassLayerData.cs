using MemoryPack;
using System;

public enum GrassCellState : byte
{
    Uninitialized = 0,
    Empty = 1,
    Present = 2,
    Removed = 3
}

[Serializable]
[MemoryPackable]
public partial class GrassLayerData
{
    public int Width;
    public int Height;
    public byte[] Cells = Array.Empty<byte>();

    public void EnsureSize(int width, int height)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        int requiredLength = width * height;

        if (Width == width && Height == height && Cells != null && Cells.Length == requiredLength)
            return;

        byte[] previous = Cells;
        int previousWidth = Width;
        int previousHeight = Height;

        Width = width;
        Height = height;
        Cells = requiredLength > 0 ? new byte[requiredLength] : Array.Empty<byte>();

        if (previous == null || previous.Length == 0 || requiredLength == 0)
            return;

        int copyWidth = Math.Min(previousWidth, width);
        int copyHeight = Math.Min(previousHeight, height);
        for (int y = 0; y < copyHeight; y++)
        {
            Array.Copy(previous, y * previousWidth, Cells, y * width, copyWidth);
        }
    }

    public bool Contains(int x, int y)
        => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public GrassCellState Get(int x, int y)
    {
        if (!Contains(x, y) || Cells == null)
            return GrassCellState.Uninitialized;

        return (GrassCellState)Cells[y * Width + x];
    }

    public bool Set(int x, int y, GrassCellState state)
    {
        if (!Contains(x, y) || Cells == null)
            return false;

        Cells[y * Width + x] = (byte)state;
        return true;
    }

    /// <summary>消费一格现有的草，并把它标记为已移除。</summary>
    public bool TryConsume(int x, int y)
    {
        if (!Contains(x, y) || Cells == null)
            return false;

        int index = y * Width + x;
        if ((GrassCellState)Cells[index] != GrassCellState.Present)
            return false;

        Cells[index] = (byte)GrassCellState.Removed;
        return true;
    }

    public void Clear()
    {
        if (Cells != null && Cells.Length > 0)
            Array.Clear(Cells, 0, Cells.Length);
    }

    public byte[] CopyCells()
    {
        if (Cells == null || Cells.Length == 0)
            return Array.Empty<byte>();

        byte[] copy = new byte[Cells.Length];
        Array.Copy(Cells, copy, Cells.Length);
        return copy;
    }
}
