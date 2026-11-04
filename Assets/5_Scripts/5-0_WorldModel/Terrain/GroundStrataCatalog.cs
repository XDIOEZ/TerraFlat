using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlatWorld.WorldModel
{
    /// <summary>主线程冻结的纯数值地层目录，后台只读取已展开的天然地层。</summary>
    public sealed class GroundStrataCatalog
    {
        #region 冻结规则展开
        public const string ParameterId = "terrain.ground.strata.v1";
        private readonly Dictionary<int, int[]> columns = new();
        private readonly HashSet<int> harvestable = new();
        public GroundStrataCatalog(string encoded)
        {
            var rules = new Dictionary<int, int[]>();
            if (!string.IsNullOrEmpty(encoded))
                foreach (string row in encoded.Split(';'))
                {
                    string[] fields = row.Split(',');
                    if (fields.Length != 4) throw new FormatException("天然地层目录字段数无效。");
                    var rule = new int[4];
                    for (int i = 0; i < 4; i++) rule[i] = int.Parse(fields[i], CultureInfo.InvariantCulture);
                    if (rule[1] < 0 || rule[1] > 100 || rule[1] > 1 && rule[2] != rule[0])
                        throw new FormatException("天然地层层数或重复地块规则无效。");
                    rules.Add(rule[0], rule);
                    if (rule[1] > 0) harvestable.Add(rule[0]);
                }
            foreach (int id in rules.Keys)
            {
                var layers = new List<int>();
                var visited = new HashSet<int>();
                int current = id;
                while (current != 0)
                {
                    if (!visited.Add(current)) throw new InvalidOperationException("天然地层规则存在循环。");
                    if (!rules.TryGetValue(current, out int[] rule))
                        throw new InvalidOperationException($"天然地层引用未登记地块：{current}");
                    int count = Math.Max(1, rule[1]);
                    for (int n = 0; n < count; n++) layers.Add(current);
                    current = rule[1] == 0 ? 0 : rule[1] > 1 ? rule[3] : rule[2];
                }
                columns.Add(id, layers.ToArray());
            }
        }
        public bool IsHarvestable(int tileId) => harvestable.Contains(tileId);
        public bool TryGetNaturalColumn(int tileId, out int[] layers) => columns.TryGetValue(tileId, out layers);
        public IReadOnlyList<int> GetNaturalColumn(int tileId) =>
            columns.TryGetValue(tileId, out int[] result) ? result : new[] { tileId };
        #endregion
    }

    public struct GroundStratum
    {
        #region 单层状态
        public int TileId;
        public float HarvestProgress;
        public byte Grass;
        public bool PlayerPlaced;
        public short NavigationCost;
        public bool Walkable;
        #endregion
    }

    /// <summary>独立列快照按从底到顶保存，长度只包含有效层，不暴露池容量。</summary>
    public sealed class GroundColumnSnapshot
    {
        #region 列快照
        public int BottomZ;
        public GroundStratum[] Layers;
        public int TopZ => BottomZ + (Layers?.Length ?? 0) - 1;
        public bool IsValid => Layers != null && (long)BottomZ + Layers.Length <= int.MaxValue &&
            Array.TrueForAll(Layers, layer => layer.TileId > 0 && float.IsFinite(layer.HarvestProgress) &&
                layer.HarvestProgress >= 0f && layer.HarvestProgress <= 1f && layer.Grass <= ChunkTerrainData.GrassPresent);
        #endregion
    }
}
