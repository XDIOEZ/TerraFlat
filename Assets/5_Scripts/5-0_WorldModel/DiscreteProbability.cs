using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlatWorld.WorldModel
{
    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class DiscreteProbabilityEntryDefinition
    {
        #region JSON 档位
        public double Value;
        public double Percent;
        #endregion
    }

    [MemoryPack.MemoryPackable]
    [Serializable]
    public sealed partial class DiscreteProbabilityTableDefinition
    {
        #region JSON 原始表
        public string Id;
        public DiscreteProbabilityEntryDefinition[] Entries;
        #endregion
    }

    /// <summary>统一修正入口只处理本次独立状态，不接触 JSON 原表。</summary>
    public delegate void DiscreteProbabilityModifier(string tableId, ref DiscreteProbabilityState state);

    public struct DiscreteProbabilityState
    {
        #region 可独立修改的运行态
        private readonly double[] values;
        private double[] weights;
        public int Count => values?.Length ?? 0;

        internal DiscreteProbabilityState(double[] values, double[] weights)
        {
            this.values = values;
            this.weights = weights;
        }

        public double GetValue(int index) => values[index];
        public double GetWeight(int index) => weights[index];

        public void SetWeight(int index, double weight)
        {
            DiscreteProbability.RequireNonNegative(weight, nameof(weight));
            // 结构体复制后也不共享可变权重，难度修正不会串到另一份状态。
            double[] copy = (double[])weights.Clone();
            copy[index] = weight;
            weights = copy;
        }

        public void MultiplyWeight(int index, double multiplier)
        {
            DiscreteProbability.RequireNonNegative(multiplier, nameof(multiplier));
            SetWeight(index, weights[index] * multiplier);
        }

        public double GetFinalPercent(int index)
        {
            double total = 0d;
            foreach (double weight in weights) total += weight;
            if (total <= 0d || double.IsInfinity(total))
                throw new InvalidOperationException("离散概率修正后的总权重必须为有限正数。");
            return weights[index] / total * 100d;
        }

        internal DiscreteProbabilitySnapshot Freeze(string id)
        {
            if (Count == 0 || weights == null || weights.Length != Count)
                throw new ArgumentException("修正规则返回了无效的离散概率状态。");
            return new DiscreteProbabilitySnapshot(id, values, weights);
        }
        #endregion
    }

    public sealed class DiscreteProbabilitySnapshot
    {
        #region 不可变最终概率
        private readonly double[] values;
        private readonly double[] probabilities;
        private readonly double[] cumulative;
        public string Id { get; }
        public int Count => values.Length;

        internal DiscreteProbabilitySnapshot(string id, double[] sourceValues, double[] weights)
        {
            Id = id;
            values = (double[])sourceValues.Clone();
            probabilities = new double[weights.Length];
            cumulative = new double[weights.Length];
            double total = 0d;
            foreach (double weight in weights)
            {
                DiscreteProbability.RequireNonNegative(weight, nameof(weights));
                total += weight;
            }
            if (total <= 0d || double.IsInfinity(total))
                throw new ArgumentException($"离散概率表 {id} 的总权重必须为有限正数。");
            double sum = 0d;
            for (int i = 0; i < weights.Length; i++)
            {
                probabilities[i] = weights[i] / total;
                cumulative[i] = sum += probabilities[i];
            }
            int lastPositive = weights.Length - 1;
            while (weights[lastPositive] <= 0d) lastPositive--;
            for (int i = lastPositive; i < weights.Length; i++) cumulative[i] = 1d;
        }

        public double GetValue(int index) => values[index];
        public double GetProbability(int index) => probabilities[index];
        public double GetPercent(int index) => probabilities[index] * 100d;

        public double ProbabilityOf(double value)
        {
            for (int i = 0; i < Count; i++) if (values[i] == value) return probabilities[i];
            throw new ArgumentException($"离散概率表 {Id} 没有结果值 {value}。");
        }

        internal int Find(double sample)
        {
            if (sample >= 1d)
            {
                for (int i = Count - 1; i >= 0; i--) if (probabilities[i] > 0d) return i;
            }
            int low = 0, high = Count - 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (sample < cumulative[middle]) high = middle;
                else low = middle + 1;
            }
            return low;
        }
        #endregion
    }

    public static class DiscreteProbability
    {
        #region 统一构建与抽签
        public static DiscreteProbabilitySnapshot Build(DiscreteProbabilityTableDefinition definition,
            DiscreteProbabilityModifier modifier = null)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id) ||
                definition.Entries == null || definition.Entries.Length == 0)
                throw new ArgumentException("离散概率表必须包含稳定 ID 和非空档位。");
            var values = new double[definition.Entries.Length];
            var weights = new double[values.Length];
            double total = 0d;
            var unique = new HashSet<double>();
            for (int i = 0; i < values.Length; i++)
            {
                DiscreteProbabilityEntryDefinition entry = definition.Entries[i];
                if (entry == null || double.IsNaN(entry.Value) || double.IsInfinity(entry.Value) || !unique.Add(entry.Value))
                    throw new ArgumentException($"离散概率表 {definition.Id} 含无效或重复结果值。");
                RequireNonNegative(entry.Percent, nameof(entry.Percent));
                values[i] = entry.Value;
                weights[i] = entry.Percent;
                total += entry.Percent;
            }
            if (Math.Abs(total - 100d) > .001d)
                throw new ArgumentException($"离散概率表 {definition.Id} 的原始百分比须合计100%，当前为 {total}%。");
            var state = new DiscreteProbabilityState(values, weights);
            modifier?.Invoke(definition.Id, ref state);
            return state.Freeze(definition.Id);
        }

        public static double Sample(DiscreteProbabilitySnapshot table, double sample)
        {
            RequireSample(table, sample);
            return table.GetValue(table.Find(sample));
        }

        public static double SampleWithin(DiscreteProbabilitySnapshot table, double sample, double minimum, double maximum)
        {
            RequireSample(table, sample);
            if (double.IsNaN(minimum) || double.IsInfinity(minimum) || double.IsNaN(maximum) ||
                double.IsInfinity(maximum) || maximum < minimum) throw new ArgumentOutOfRangeException(nameof(minimum));
            double total = 0d;
            int last = -1;
            for (int i = 0; i < table.Count; i++)
                if (table.GetValue(i) >= minimum && table.GetValue(i) <= maximum && table.GetProbability(i) > 0d)
                { total += table.GetProbability(i); last = i; }
            if (last < 0) throw new InvalidOperationException($"概率表 {table.Id} 在 {minimum}～{maximum} 内没有有效档位。");
            double target = sample * total;
            for (int i = 0; i < table.Count; i++)
            {
                if (table.GetValue(i) < minimum || table.GetValue(i) > maximum || table.GetProbability(i) <= 0d) continue;
                target -= table.GetProbability(i);
                if (target < 0d) return table.GetValue(i);
            }
            return table.GetValue(last);
        }

        private static void RequireSample(DiscreteProbabilitySnapshot table, double sample)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (double.IsNaN(sample) || sample < 0d || sample > 1d) throw new ArgumentOutOfRangeException(nameof(sample));
        }

        internal static void RequireNonNegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new ArgumentOutOfRangeException(name, "概率权重必须为非负有限值。");
        }
        #endregion
    }

    public sealed class DiscreteProbabilityCatalog
    {
        #region 不可变目录与生成快照
        private const string Prefix = "probability.";
        private readonly Dictionary<string, DiscreteProbabilitySnapshot> tables = new(StringComparer.Ordinal);
        public DiscreteProbabilityCatalog(IEnumerable<DiscreteProbabilityTableDefinition> definitions,
            DiscreteProbabilityModifier modifier = null)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (var definition in definitions)
            {
                var table = DiscreteProbability.Build(definition, modifier);
                if (!tables.TryAdd(table.Id, table)) throw new ArgumentException($"重复的离散概率表 ID：{table.Id}");
            }
        }

        public DiscreteProbabilitySnapshot Require(string id) => tables.TryGetValue(id, out var table)
            ? table : throw new KeyNotFoundException($"缺少离散概率表：{id}");

        public void WriteGenerationParameters(IDictionary<string, double> parameters)
        {
            foreach (var pair in tables)
            {
                if (!pair.Key.StartsWith("generation.", StringComparison.Ordinal)) continue;
                string key = Prefix + pair.Key + ".";
                parameters[key + "count"] = pair.Value.Count;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    string entry = key + i.ToString(CultureInfo.InvariantCulture) + ".";
                    parameters[entry + "value"] = pair.Value.GetValue(i);
                    parameters[entry + "percent"] = pair.Value.GetPercent(i);
                }
            }
        }

        public static DiscreteProbabilityCatalog FromGenerationParameters(IReadOnlyDictionary<string, double> parameters)
        {
            var definitions = new List<DiscreteProbabilityTableDefinition>();
            foreach (var pair in parameters)
            {
                if (!pair.Key.StartsWith(Prefix, StringComparison.Ordinal) || !pair.Key.EndsWith(".count", StringComparison.Ordinal)) continue;
                if (pair.Value < 1d || pair.Value > 100000d || pair.Value != Math.Floor(pair.Value))
                    throw new ArgumentException("冻结概率表的档位数量无效。");
                string id = pair.Key.Substring(Prefix.Length, pair.Key.Length - Prefix.Length - 6);
                var entries = new DiscreteProbabilityEntryDefinition[(int)pair.Value];
                for (int i = 0; i < entries.Length; i++)
                {
                    string key = Prefix + id + "." + i.ToString(CultureInfo.InvariantCulture) + ".";
                    entries[i] = new DiscreteProbabilityEntryDefinition { Value = parameters[key + "value"], Percent = parameters[key + "percent"] };
                }
                definitions.Add(new DiscreteProbabilityTableDefinition { Id = id, Entries = entries });
            }
            return new DiscreteProbabilityCatalog(definitions);
        }
        #endregion
    }
}
