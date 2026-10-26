using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed class SpaceOreAsteroidCatalog
    {
        #region 小行星矿带配置与确定性生成
        private static SpaceOreAsteroidCatalog current;
        private readonly Dictionary<string, SpaceOreAsteroidComposition> compositions = new(StringComparer.Ordinal);
        private readonly double totalCompositionWeight;
        public SpaceOreAsteroidConfiguration Configuration { get; }

        private SpaceOreAsteroidCatalog(SpaceOreAsteroidConfiguration configuration)
        {
            Configuration = configuration ?? throw new InvalidDataException("矿石小行星配置为空。");
            if (configuration.SchemaVersion != 1 || configuration.Fields == null || configuration.Compositions == null ||
                !Positive(configuration.PhysicsActivationDistanceMeters) || configuration.PhysicsActivationDistanceMeters > 4096d)
                throw new InvalidDataException("矿石小行星版本或物理范围无效。");
            foreach (SpaceOreAsteroidComposition composition in configuration.Compositions)
            {
                if (composition == null || string.IsNullOrWhiteSpace(composition.Id) || string.IsNullOrWhiteSpace(composition.ItemId) ||
                    !Positive(composition.Weight) || composition.AmountMin <= 0 || composition.AmountMax < composition.AmountMin ||
                    composition.AmountMax > 1000 || !compositions.TryAdd(composition.Id, composition))
                    throw new InvalidDataException("矿石小行星矿物定义无效或重复。");
                totalCompositionWeight += composition.Weight;
            }
            if (!Positive(totalCompositionWeight)) throw new InvalidDataException("矿石小行星缺少矿物。");
            var fieldIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SpaceOreAsteroidField field in configuration.Fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Id) || !fieldIds.Add(field.Id) || field.CenterBodyIds == null ||
                    field.CenterBodyIds.Length == 0 || field.CountPerBody <= 0 || field.CountPerBody > 256 ||
                    !Positive(field.MinimumRadiusMeters) || field.MaximumRadiusMeters < field.MinimumRadiusMeters ||
                    !Positive(field.MaximumRadiusMeters) || field.MaximumRadiusMeters > 20d ||
                    !Positive(field.MinimumClearanceMeters) || field.MaximumClearanceMeters < field.MinimumClearanceMeters ||
                    !Positive(field.MaximumClearanceMeters) || field.MaximumClearanceMeters > SpaceSectorCoordinates.HalfSizeMeters ||
                    !Positive(field.HitPointsPerRadius) || !Positive(field.MaximumRadiusMeters * field.HitPointsPerRadius) ||
                    field.StoneAmountMin < 0 || field.StoneAmountMax < field.StoneAmountMin || field.StoneAmountMax > 1000)
                    throw new InvalidDataException("矿石小行星矿带范围无效。");
                var centerIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (string center in field.CenterBodyIds)
                    if (string.IsNullOrWhiteSpace(center) || !centerIds.Add(center))
                        throw new InvalidDataException("矿石小行星矿带存在空或重复中心。");
            }
        }

        public static SpaceOreAsteroidCatalog Parse(string json)
            => new(JsonConvert.DeserializeObject<SpaceOreAsteroidConfiguration>(json,
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }));

        public static SpaceOreAsteroidCatalog LoadDefault()
        {
            if (current != null) return current;
            TextAsset asset = Resources.Load<TextAsset>("Config/Space/ore-asteroids");
            if (asset == null) throw new InvalidDataException("矿石小行星配置不存在。");
            return current = Parse(asset.text);
        }

        public static void Reset() => current = null;

        public SpaceOreAsteroidComposition GetComposition(string id)
            => compositions.TryGetValue(id ?? string.Empty, out SpaceOreAsteroidComposition composition)
                ? composition : throw new InvalidDataException("矿石小行星矿物不存在：" + id);

        public List<SpaceOreAsteroidState> GenerateSectorOreAsteroids(int seed, UniverseState sector)
        {
            if (sector?.Bodies == null) throw new ArgumentNullException(nameof(sector));
            var result = new List<SpaceOreAsteroidState>();
            foreach (SpaceOreAsteroidField field in Configuration.Fields)
            foreach (string centerId in field.CenterBodyIds)
            foreach (BodyState body in sector.Bodies)
            {
                if (!string.Equals(string.IsNullOrWhiteSpace(body.TemplateBodyId) ? body.BodyId : body.TemplateBodyId,
                    centerId, StringComparison.Ordinal)) continue;
                for (int index = 0; index < field.CountPerBody; index++)
                {
                    string id = body.BodyId + "/ore:" + field.Id + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    uint random = unchecked((uint)UniverseSectorFactory.DeriveSeed(seed, id));
                    if (random == 0) random = 0x9e3779b9u;
                    double radius = Range(ref random, field.MinimumRadiusMeters, field.MaximumRadiusMeters);
                    double distance = body.RadiusMeters + radius + Range(ref random, field.MinimumClearanceMeters, field.MaximumClearanceMeters);
                    // 每颗使用独立身份种子与独立角度格，加载顺序和别的星区不改变矿种或数量。
                    double angle = (index + Unit(ref random)) / field.CountPerBody * Math.PI * 2d;
                    SpaceOreAsteroidComposition composition = SelectComposition(Unit(ref random) * totalCompositionWeight);
                    result.Add(new SpaceOreAsteroidState
                    {
                        AsteroidId = id, CenterBodyId = body.BodyId, Sector = body.Sector,
                        CompositionId = composition.Id, OffsetMeters = new SpaceVector2(Math.Cos(angle) * distance, Math.Sin(angle) * distance),
                        RadiusMeters = radius, MaximumHitPoints = field.HitPointsPerRadius * radius, HitPoints = field.HitPointsPerRadius * radius,
                        OreAmount = Amount(ref random, composition.AmountMin, composition.AmountMax),
                        StoneAmount = Amount(ref random, field.StoneAmountMin, field.StoneAmountMax)
                    });
                }
            }
            return result;
        }

        private SpaceOreAsteroidComposition SelectComposition(double sample)
        {
            foreach (SpaceOreAsteroidComposition composition in Configuration.Compositions)
            { sample -= composition.Weight; if (sample < 0d) return composition; }
            return Configuration.Compositions[^1];
        }
        private static bool Positive(double value) => double.IsFinite(value) && value > 0d;
        private static double Unit(ref uint state)
        { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state / 4294967296d; }
        private static double Range(ref uint state, double min, double max) => min + (max - min) * Unit(ref state);
        private static int Amount(ref uint state, int min, int max) => min + (int)(Unit(ref state) * (max - min + 1));
        #endregion
    }

    [Serializable]
    public sealed class SpaceOreAsteroidConfiguration
    {
        #region 矿带参数
        public int SchemaVersion = 1;
        public double PhysicsActivationDistanceMeters = 512d;
        public SpaceOreAsteroidField[] Fields;
        public SpaceOreAsteroidComposition[] Compositions;
        #endregion
    }
    [Serializable]
    public sealed class SpaceOreAsteroidField
    {
        #region 矿带分布
        public string Id;
        public string[] CenterBodyIds;
        public int CountPerBody;
        public double MinimumClearanceMeters, MaximumClearanceMeters, MinimumRadiusMeters, MaximumRadiusMeters, HitPointsPerRadius;
        public int StoneAmountMin, StoneAmountMax;
        #endregion
    }
    [Serializable]
    public sealed class SpaceOreAsteroidComposition
    {
        #region 可击碎的矿物组成
        public string Id, ItemId;
        public double Weight;
        public int AmountMin, AmountMax;
        #endregion
    }
}
