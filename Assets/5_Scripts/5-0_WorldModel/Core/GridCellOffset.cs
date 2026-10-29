using System;

namespace FlatWorld.WorldModel
{
    /// <summary>相对某个世界格锚点的纯整数偏移，可在不创建 Unity 对象的情况下用于地图计算。</summary>
    public readonly struct GridCellOffset : IEquatable<GridCellOffset>
    {
        /// <summary>水平格偏移。</summary>
        public readonly int X;

        /// <summary>垂直格偏移。</summary>
        public readonly int Y;

        /// <summary>创建纯整数格偏移。</summary>
        public GridCellOffset(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(GridCellOffset other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is GridCellOffset other && Equals(other);

        public override int GetHashCode() => unchecked((X * 397) ^ Y);
    }
}
