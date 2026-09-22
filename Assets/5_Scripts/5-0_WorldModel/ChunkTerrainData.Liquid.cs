using System;
using System.Buffers;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>一格液体的纯运行时值；深度范围 0～1，编号不参与持久化。</summary>
    public readonly struct LiquidCellValue : IEquatable<LiquidCellValue>
    {
        #region 格子值
        public readonly int LiquidTypeIndex;
        public readonly float LiquidDepth;
        public LiquidCellValue(int typeIndex, float depth) { LiquidTypeIndex = typeIndex; LiquidDepth = depth; }
        public bool Equals(LiquidCellValue other) => LiquidTypeIndex == other.LiquidTypeIndex && LiquidDepth.Equals(other.LiquidDepth);
        #endregion
    }

    /// <summary>液体数组的唯一所有者；生成草稿 Seal 时移交，取消生成与区块逐出时归还数组池。</summary>
    internal sealed class LiquidCellStorage : IDisposable
    {
        #region 数组所有权
        internal readonly LiquidTypeCatalog Types;
        internal float[] Depth;
        internal int[] TypeIndex;
        internal readonly int Count;
        internal LiquidCellStorage(int count, LiquidTypeCatalog types)
        {
            Count = count;
            Types = types ?? LiquidTypeCatalog.BuiltIn;
            Depth = ArrayPool<float>.Shared.Rent(count);
            TypeIndex = ArrayPool<int>.Shared.Rent(count);
            Array.Clear(Depth, 0, count);
            Array.Clear(TypeIndex, 0, count);
        }
        internal LiquidCellValue Read(int index) => new(TypeIndex[index], Depth[index]);
        internal LiquidCellValue Normalize(int typeIndex, float depth)
        {
            if (float.IsNaN(depth) || float.IsInfinity(depth)) throw new ArgumentOutOfRangeException(nameof(depth));
            Types.GetId(typeIndex);
            depth = Math.Clamp(depth, 0f, 1f);
            if (depth > 0f && typeIndex == 0) throw new ArgumentException("非零液深必须具有液体身份。");
            return new LiquidCellValue(depth > 0f ? typeIndex : 0, depth);
        }
        internal void Write(int index, LiquidCellValue value) { Depth[index] = value.LiquidDepth; TypeIndex[index] = value.LiquidTypeIndex; }
        public void Dispose()
        {
            if (Depth == null) return;
            ArrayPool<float>.Shared.Return(Depth, true);
            ArrayPool<int>.Shared.Return(TypeIndex, true);
            Depth = null;
            TypeIndex = null;
        }
        #endregion
    }

    public sealed partial class ChunkTerrainBuffer
    {
        #region 生成液体层
        private LiquidCellStorage liquid;
        public LiquidTypeCatalog LiquidTypes => liquid.Types;
        public float GetLiquidDepth(int x, int y) { ThrowIfUnavailable(); return liquid.Depth[GetIndex(x, y)]; }
        /// <summary>生成阶段同时确定底部地块和液体；此接口只写液体数组。</summary>
        public void SetLiquid(int x, int y, int liquidTypeIndex, float liquidDepth)
        {
            ThrowIfUnavailable();
            int index = GetIndex(x, y);
            LiquidCellValue value = liquid.Normalize(liquidTypeIndex, liquidDepth);
            liquid.Write(index, value);
        }
        #endregion
    }

    public sealed partial class ChunkTerrainData
    {
        #region 权威液体层
        private readonly LiquidCellStorage liquid;
        // 只为发生过变化的格子保留生成基线；整块原始液体永远不写入存档。
        private readonly Dictionary<int, LiquidCellValue> liquidBaselines = new();
        public LiquidTypeCatalog LiquidTypes => liquid.Types;
        public ReadOnlySpan<float> LiquidDepth { get { ThrowIfDisposed(); return liquid.Depth.AsSpan(0, CellCount); } }
        public ReadOnlySpan<int> LiquidTypeIndex { get { ThrowIfDisposed(); return liquid.TypeIndex.AsSpan(0, CellCount); } }
        public float GetLiquidDepth(int x, int y) { ThrowIfDisposed(); return liquid.Depth[GetIndex(x, y)]; }
        public int GetLiquidTypeIndex(int x, int y) { ThrowIfDisposed(); return liquid.TypeIndex[GetIndex(x, y)]; }
        public string GetLiquidId(int x, int y) => LiquidTypes.GetId(GetLiquidTypeIndex(x, y));

        /// <summary>统一纯数据写入口；只改变液体，原 GroundTileId 和地块通行成本始终不变。</summary>
        public bool SetLiquid(int x, int y, int liquidTypeIndex, float liquidDepth)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            LiquidCellValue next = liquid.Normalize(liquidTypeIndex, liquidDepth);
            LiquidCellValue previous = liquid.Read(index);
            if (previous.Equals(next)) return false;
            if (!liquidBaselines.ContainsKey(index)) liquidBaselines.Add(index, previous);
            liquid.Write(index, next);
            MarkChanged(x, y, TerrainChangeKind.Liquid);
            return true;
        }

        /// <summary>用于稀疏存档去重：恢复生成值后可以删除该格差量。</summary>
        public bool IsLiquidChanged(int x, int y)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            return liquidBaselines.TryGetValue(index, out LiquidCellValue baseline) && !baseline.Equals(liquid.Read(index));
        }
        #endregion
    }
}
