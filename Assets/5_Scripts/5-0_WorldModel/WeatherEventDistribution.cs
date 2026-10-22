using System;

namespace FlatWorld.WorldModel
{
    /// <summary>从配置表读取离散取值和概率，运行时只负责按表抽签。</summary>
    [Serializable]
    public sealed class WeatherEventDistribution
    {
        #region 离散概率表
        public float[] Values = Array.Empty<float>();
        public float[] Probabilities = Array.Empty<float>();

        public WeatherEventDistribution Clone() => new WeatherEventDistribution
        {
            Values = (float[])Values.Clone(), Probabilities = (float[])Probabilities.Clone()
        };

        public void Validate(float minimum, float maximum)
        {
            if (!Finite(minimum) || !Finite(maximum) || maximum < minimum || Values == null ||
                Probabilities == null || Values.Length == 0 || Values.Length != Probabilities.Length)
                throw new ArgumentOutOfRangeException(nameof(WeatherEventDistribution), "天气档位和概率表不能为空且长度须一致。");
            double total = 0d;
            for (int i = 0; i < Values.Length; i++)
            {
                if (!Finite(Values[i]) || Values[i] < minimum || Values[i] > maximum ||
                    i > 0 && Values[i] <= Values[i - 1] || !Finite(Probabilities[i]) ||
                    Probabilities[i] < 0f || Probabilities[i] > 1f)
                    throw new ArgumentOutOfRangeException(nameof(WeatherEventDistribution), "天气档位须递增且概率在零到一之间。");
                total += Probabilities[i];
            }
            if (Math.Abs(total - 1d) > .000001d)
                throw new ArgumentOutOfRangeException(nameof(WeatherEventDistribution), "天气档位概率合计须为100%。");
        }

        public float Sample(float minimum, float maximum, double sample)
        {
            Validate(minimum, maximum);
            if (!Finite(sample) || sample < 0d || sample > 1d)
                throw new ArgumentOutOfRangeException(nameof(sample), "天气抽签随机数须在零到一之间。");
            double total = 0d;
            foreach (float probability in Probabilities) total += probability;
            double target = sample * total, cumulative = 0d;
            int lastAvailable = 0;
            // 只累计配置中的概率，不用函数生成概率，也不在事件中途重新抽签。
            for (int i = 0; i < Values.Length; i++)
            {
                if (Probabilities[i] <= 0f) continue;
                lastAvailable = i;
                cumulative += Probabilities[i];
                if (target < cumulative) return Values[i];
            }
            return Values[lastAvailable];
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        #endregion
    }
}
