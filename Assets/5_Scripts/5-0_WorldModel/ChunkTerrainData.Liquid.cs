using System;
using System.Buffers;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>一格液体的纯运行时值；0～10 整数等级，一份液体就是一级。</summary>
    public readonly struct LiquidCellValue : IEquatable<LiquidCellValue>
    {
        #region 格子值
        public const int MaximumLevel = 10;
        public readonly int LiquidTypeIndex;
        public readonly int LiquidLevel;
        public LiquidCellValue(int typeIndex, int level) { LiquidTypeIndex = typeIndex; LiquidLevel = level; }
        public bool Equals(LiquidCellValue other) => LiquidTypeIndex == other.LiquidTypeIndex && LiquidLevel == other.LiquidLevel;

        public static int NormalizeLevel(int level) => Math.Clamp(level, 0, MaximumLevel);
        // 浮点深度只在表现和比例采样时派生，不作为第二份权威水量。
        public static float ToNormalizedDepth(int level) => NormalizeLevel(level) / (float)MaximumLevel;
        public static int FromGeneratedDepth(double depth)
        {
            if (!double.IsFinite(depth)) throw new ArgumentOutOfRangeException(nameof(depth));
            // 生成器也会传入 float，十分位的二进制误差不能凭空多升一级。
            return depth <= 0d ? 0 : Math.Clamp((int)Math.Ceiling(Math.Clamp(depth, 0d, 1d) *
                MaximumLevel - 0.000001d), 1, MaximumLevel);
        }
        #endregion
    }

    /// <summary>液体数组的唯一所有者；生成草稿 Seal 时移交，取消生成与区块逐出时归还数组池。</summary>
    internal sealed class LiquidCellStorage : IDisposable
    {
        #region 数组所有权
        internal readonly LiquidTypeCatalog Types;
        internal int[] Level;
        internal int[] TypeIndex;
        internal readonly int Count;
        internal LiquidCellStorage(int count, LiquidTypeCatalog types)
        {
            Count = count;
            Types = types ?? LiquidTypeCatalog.BuiltIn;
            Level = ArrayPool<int>.Shared.Rent(count);
            TypeIndex = ArrayPool<int>.Shared.Rent(count);
            Array.Clear(Level, 0, count);
            Array.Clear(TypeIndex, 0, count);
        }
        internal LiquidCellValue Read(int index) => new(TypeIndex[index], Level[index]);
        internal LiquidCellValue Normalize(int typeIndex, int level)
        {
            level = LiquidCellValue.NormalizeLevel(level);
            Types.GetId(typeIndex);
            if (level > 0 && typeIndex == 0) throw new ArgumentException("非零液体等级必须具有液体身份。");
            return new LiquidCellValue(level > 0 ? typeIndex : 0, level);
        }
        internal void Write(int index, LiquidCellValue value) { Level[index] = value.LiquidLevel; TypeIndex[index] = value.LiquidTypeIndex; }
        public void Dispose()
        {
            if (Level == null) return;
            ArrayPool<int>.Shared.Return(Level, true);
            ArrayPool<int>.Shared.Return(TypeIndex, true);
            Level = null;
            TypeIndex = null;
        }
        #endregion
    }

    public sealed partial class ChunkTerrainBuffer
    {
        #region 生成液体层
        private LiquidCellStorage liquid;
        public LiquidTypeCatalog LiquidTypes => liquid.Types;
        public int GetLiquidLevel(int x, int y) { ThrowIfUnavailable(); return liquid.Level[GetIndex(x, y)]; }
        public float GetLiquidDepth(int x, int y) => LiquidCellValue.ToNormalizedDepth(GetLiquidLevel(x, y));
        /// <summary>生成阶段同时确定底部地块和液体；此接口只写液体数组。</summary>
        public void SetLiquid(int x, int y, int liquidTypeIndex, int liquidLevel)
        {
            ThrowIfUnavailable();
            int index = GetIndex(x, y);
            LiquidCellValue value = liquid.Normalize(liquidTypeIndex, liquidLevel);
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
        private long liquidRevision;
        /// <summary>只随真实液体身份或深度变化递增，环境层修改不让水体派生缓存失效。</summary>
        public long LiquidRevision => liquidRevision;
        public LiquidTypeCatalog LiquidTypes => liquid.Types;
        public ReadOnlySpan<int> LiquidLevel { get { ThrowIfDisposed(); return liquid.Level.AsSpan(0, CellCount); } }
        public ReadOnlySpan<int> LiquidTypeIndex { get { ThrowIfDisposed(); return liquid.TypeIndex.AsSpan(0, CellCount); } }
        public int GetLiquidLevel(int x, int y) { ThrowIfDisposed(); return liquid.Level[GetIndex(x, y)]; }
        public float GetLiquidDepth(int x, int y) => LiquidCellValue.ToNormalizedDepth(GetLiquidLevel(x, y));
        public int GetLiquidTypeIndex(int x, int y) { ThrowIfDisposed(); return liquid.TypeIndex[GetIndex(x, y)]; }
        public string GetLiquidId(int x, int y) => LiquidTypes.GetId(GetLiquidTypeIndex(x, y));

        /// <summary>统一纯数据写入口；只改变液体，原 GroundTileId 和地块通行成本始终不变。</summary>
        public bool SetLiquid(int x, int y, int liquidTypeIndex, int liquidLevel)
        {
            ThrowIfDisposed();
            int index = GetIndex(x, y);
            LiquidCellValue next = liquid.Normalize(liquidTypeIndex, liquidLevel);
            LiquidCellValue previous = liquid.Read(index);
            if (previous.Equals(next)) return false;
            if (!liquidBaselines.ContainsKey(index)) liquidBaselines.Add(index, previous);
            liquid.Write(index, next);
            liquidRevision++;
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
