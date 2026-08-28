using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 告诉后台生成器世界边界是什么样的。
    /// 默认表示无限世界；如果提供了范围，就表示走出左边会从右边回来、走出上边会从下边回来。
    /// </summary>
    public readonly struct ChunkGenerationTopologySnapshot
    {
        public ChunkGenerationTopologySnapshot(Int2 min, Int2 span)
        {
            if (span.X <= 0)
                throw new ArgumentOutOfRangeException(nameof(span));
            if (span.Y <= 0)
                throw new ArgumentOutOfRangeException(nameof(span));
            Min = min;
            Span = span;
            IsWrapped = true;
        }

        /// <summary>世界是否会在边界绕回另一边；false 表示世界无限延伸。</summary>
        public bool IsWrapped { get; }
        /// <summary>有限世界左下方的起始坐标。</summary>
        public Int2 Min { get; }
        /// <summary>有限世界横向和纵向各有多长。</summary>
        public Int2 Span { get; }

        /// <summary>把越过左右边界的 X 坐标绕回世界内；无限世界不做修改。</summary>
        public int NormalizeX(int value) => IsWrapped ? Wrap(value, Min.X, Span.X) : value;
        /// <summary>把越过上下边界的 Y 坐标绕回世界内；无限世界不做修改。</summary>
        public int NormalizeY(int value) => IsWrapped ? Wrap(value, Min.Y, Span.Y) : value;

        private static int Wrap(int value, int min, int span)
        {
            // 先用更大的整数类型计算，避免极端坐标溢出；负数余数再补回正确范围。
            long offset = (long)value - min;
            long wrapped = offset % span;
            if (wrapped < 0L)
                wrapped += span;
            return (int)(min + wrapped);
        }
    }

    /// <summary>
    /// 一条洞穴矿脉规则的纯数据副本。
    /// 列表顺序保持旧版“稀有矿优先、石矿兜底”的判定顺序，字段可直接序列化为 JSON。
    /// </summary>
    public sealed class CaveResourceRuleSnapshot
    {
        public CaveResourceRuleSnapshot(string ruleId, string itemId, double veinThreshold,
            double veinScale, int noiseOffset)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
                throw new ArgumentException("Cave resource rule id is required.", nameof(ruleId));
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Cave resource item id is required.", nameof(itemId));

            RuleId = ruleId.Trim();
            ItemId = itemId.Trim();
            VeinThreshold = Clamp01(veinThreshold);
            VeinScale = PositiveFinite(veinScale, 0.04d);
            NoiseOffset = noiseOffset;
        }

        public string RuleId { get; }
        public string ItemId { get; }
        public double VeinThreshold { get; }
        public double VeinScale { get; }
        public int NoiseOffset { get; }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0d;
            return value < 0d ? 0d : value > 1d ? 1d : value;
        }

        private static double PositiveFinite(double value, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0d
                ? fallback
                : value;
        }
    }

    /// <summary>
    /// 矿洞生成引用地表参数的纯数据上下文。
    /// 矿洞与地表使用不同的维度种子，不能只根据矿洞自身地形猜测入口或高度；
    /// 这里保存冻结后的地表 Profile、地表种子和拓扑，使后台生成可以复算入口与高度而不依赖已加载区块。
    /// </summary>
    public sealed class CavePortalPairingSnapshot
    {
        public CavePortalPairingSnapshot(string surfaceDimensionId, int surfaceWorldSeed,
            ChunkGenerationProfileSnapshot surfaceProfile,
            ChunkGenerationTopologySnapshot surfaceTopology = default)
        {
            if (string.IsNullOrWhiteSpace(surfaceDimensionId))
            {
                throw new ArgumentException("Surface dimension id is required.",
                    nameof(surfaceDimensionId));
            }

            SurfaceDimensionId = surfaceDimensionId.Trim();
            SurfaceWorldSeed = surfaceWorldSeed == 0 ? 1 : surfaceWorldSeed;
            SurfaceProfile = surfaceProfile ?? throw new ArgumentNullException(nameof(surfaceProfile));
            SurfaceTopology = surfaceTopology;
            Fingerprint = CalculateFingerprint();
        }

        /// <summary>与当前矿洞成对的地表维度标识。</summary>
        public string SurfaceDimensionId { get; }
        /// <summary>地表实际使用的维度派生种子。</summary>
        public int SurfaceWorldSeed { get; }
        /// <summary>地表已冻结的完整纯生成配置。</summary>
        public ChunkGenerationProfileSnapshot SurfaceProfile { get; }
        /// <summary>地表的有限世界拓扑。</summary>
        public ChunkGenerationTopologySnapshot SurfaceTopology { get; }
        /// <summary>纳入矿洞生成设置哈希的稳定配对指纹。</summary>
        public ulong Fingerprint { get; }

        /// <summary>从矿洞任务派生地表纯生成请求，并始终保留地表自身的种子与拓扑。</summary>
        public ChunkGenerationRequest CreateSurfaceRequest(
            ChunkGenerationRequest referenceRequest, Int2 origin)
        {
            return new ChunkGenerationRequest(referenceRequest.WorldEpoch,
                new WorldAddress(SurfaceDimensionId, origin), SurfaceWorldSeed,
                referenceRequest.RequestVersion, SurfaceProfile, SurfaceTopology);
        }

        private ulong CalculateFingerprint()
        {
            ulong hash = 14695981039346656037UL;
            AddString(ref hash, SurfaceDimensionId);
            AddLong(ref hash, SurfaceWorldSeed);
            AddLong(ref hash, unchecked((long)SurfaceProfile.GenerationFingerprint));
            AddLong(ref hash, SurfaceTopology.IsWrapped ? 1 : 0);
            AddLong(ref hash, SurfaceTopology.Min.X);
            AddLong(ref hash, SurfaceTopology.Min.Y);
            AddLong(ref hash, SurfaceTopology.Span.X);
            AddLong(ref hash, SurfaceTopology.Span.Y);
            return hash;
        }

        private static void AddString(ref ulong hash, string value)
        {
            string normalized = value ?? string.Empty;
            for (int i = 0; i < normalized.Length; i++)
                AddLong(ref hash, normalized[i]);
            AddLong(ref hash, 0xff);
        }

        private static void AddLong(ref ulong hash, long value)
        {
            unchecked
            {
                ulong bits = (ulong)value;
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(bits >> (i * 8));
                    hash *= 1099511628211UL;
                }
            }
        }
    }

    /// <summary>
    /// 一次区块生成要用的全部设置副本。
    /// 创建后就不再改变，所以 Unity 面板或 MOD 后来修改设置，也不会干扰正在后台生成的区块。
    /// </summary>
    public sealed class ChunkGenerationProfileSnapshot
    {
        private readonly IReadOnlyDictionary<string, double> numericParameters;
        private readonly IReadOnlyDictionary<string, string> textParameters;

        public ChunkGenerationProfileSnapshot(string profileId, int signature, int width, int height,
            IDictionary<string, double> numericParameters = null,
            IDictionary<string, string> textParameters = null,
            double ecologyGlobalMultiplier = 1d,
            IEnumerable<EcologySpawnRuleSnapshot> ecologyRules = null,
            IEnumerable<CaveResourceRuleSnapshot> caveResourceRules = null,
            CavePortalPairingSnapshot portalPairing = null)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile id is required.", nameof(profileId));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            ProfileId = profileId;
            Signature = signature;
            Width = width;
            Height = height;
            this.numericParameters = new ReadOnlyDictionary<string, double>(
                numericParameters == null
                    ? new Dictionary<string, double>(StringComparer.Ordinal)
                    : new Dictionary<string, double>(numericParameters, StringComparer.Ordinal));
            this.textParameters = new ReadOnlyDictionary<string, string>(
                textParameters == null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(textParameters, StringComparer.Ordinal));
            EcologyGlobalMultiplier = double.IsNaN(ecologyGlobalMultiplier) ||
                double.IsInfinity(ecologyGlobalMultiplier)
                ? 1d
                : Math.Max(0d, ecologyGlobalMultiplier);
            var ecologyList = ecologyRules == null
                ? new List<EcologySpawnRuleSnapshot>()
                : new List<EcologySpawnRuleSnapshot>(ecologyRules);
            var ecologyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = ecologyList.Count - 1; i >= 0; i--)
            {
                EcologySpawnRuleSnapshot rule = ecologyList[i];
                if (rule == null)
                {
                    ecologyList.RemoveAt(i);
                    continue;
                }
                if (!ecologyIds.Add(rule.RuleId))
                    throw new InvalidOperationException($"Duplicate ecology rule id: {rule.RuleId}");
            }
            ecologyList.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.RuleId, right.RuleId));
            EcologyRules = new ReadOnlyCollection<EcologySpawnRuleSnapshot>(ecologyList);
            var caveResourceList = caveResourceRules == null
                ? new List<CaveResourceRuleSnapshot>()
                : new List<CaveResourceRuleSnapshot>(caveResourceRules);
            var caveResourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = caveResourceList.Count - 1; i >= 0; i--)
            {
                CaveResourceRuleSnapshot rule = caveResourceList[i];
                if (rule == null)
                {
                    caveResourceList.RemoveAt(i);
                    continue;
                }
                if (!caveResourceIds.Add(rule.RuleId))
                    throw new InvalidOperationException($"Duplicate cave resource rule id: {rule.RuleId}");
            }
            // 矿物规则的先后顺序就是稀有度优先级，不能按名称排序。
            CaveResourceRules = new ReadOnlyCollection<CaveResourceRuleSnapshot>(caveResourceList);
            PortalPairing = portalPairing;
            EcologyFingerprint = CalculateEcologyFingerprint(EcologyGlobalMultiplier,
                EcologyRules);
            GenerationFingerprint = CalculateGenerationFingerprint(
                ProfileId, Signature, Width, Height,
                this.numericParameters, this.textParameters,
                EcologyGlobalMultiplier, EcologyRules, CaveResourceRules, PortalPairing);
            Settings = new ChunkGenerationSettingsSnapshot(this.numericParameters, this.textParameters);
        }

        /// <summary>这套生成设置的名字。</summary>
        public string ProfileId { get; }
        /// <summary>这套设置的版本标记，用来判断生成规则是否变过。</summary>
        public int Signature { get; }
        /// <summary>一个区块有多少列格子。</summary>
        public int Width { get; }
        /// <summary>一个区块有多少行格子。</summary>
        public int Height { get; }
        /// <summary>配置内容的稳定指纹，用于跨窗口复用纯生成缓存。</summary>
        public ulong GenerationFingerprint { get; }
        /// <summary>只包含生态配置的稳定指纹，用于存档冻结和联机校验。</summary>
        public ulong EcologyFingerprint { get; }
        /// <summary>给 MOD 使用的数字设置副本。</summary>
        public IReadOnlyDictionary<string, double> NumericParameters => numericParameters;
        /// <summary>给 MOD 使用的文字设置副本。</summary>
        public IReadOnlyDictionary<string, string> TextParameters => textParameters;
        /// <summary>自然生态物品的全局生成倍率。</summary>
        public double EcologyGlobalMultiplier { get; }
        /// <summary>自然生态物品规则；只含字符串、数值和稳定标签。</summary>
        public IReadOnlyList<EcologySpawnRuleSnapshot> EcologyRules { get; }
        /// <summary>洞穴矿脉规则；列表顺序代表从稀有到常见的回退优先级。</summary>
        public IReadOnlyList<CaveResourceRuleSnapshot> CaveResourceRules { get; }
        /// <summary>矿洞复算地表入口和高度时使用的冻结配对上下文；地表 Profile 为空。</summary>
        public CavePortalPairingSnapshot PortalPairing { get; }
        /// <summary>游戏自带生成器使用的、已经整理和检查过的设置。</summary>
        public ChunkGenerationSettingsSnapshot Settings { get; }

        /// <summary>复制 Profile 并覆盖一个数字参数；用于把当前世界级设置安全传给后台任务。</summary>
        public ChunkGenerationProfileSnapshot WithNumericParameter(string id, double value)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Parameter id is required.", nameof(id));
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            var numbers = new Dictionary<string, double>(numericParameters, StringComparer.Ordinal)
            {
                [id] = value
            };
            return new ChunkGenerationProfileSnapshot(
                ProfileId,
                Signature,
                Width,
                Height,
                numbers,
                new Dictionary<string, string>(textParameters, StringComparer.Ordinal),
                EcologyGlobalMultiplier,
                EcologyRules,
                CaveResourceRules,
                PortalPairing);
        }

        /// <summary>复制 Profile 并替换已经冻结的生态规则，用于存档恢复。</summary>
        public ChunkGenerationProfileSnapshot WithEcology(
            double globalMultiplier, IEnumerable<EcologySpawnRuleSnapshot> rules)
        {
            return new ChunkGenerationProfileSnapshot(
                ProfileId,
                Signature,
                Width,
                Height,
                new Dictionary<string, double>(numericParameters, StringComparer.Ordinal),
                new Dictionary<string, string>(textParameters, StringComparer.Ordinal),
                globalMultiplier,
                rules,
                CaveResourceRules,
                PortalPairing);
        }

        /// <summary>
        /// 使用世界首次进入时冻结的原始参数恢复 Profile。
        /// 保留当前资源的 Profile 标识、签名和区块尺寸，只替换会影响确定性地形/物品结果的配置内容。
        /// </summary>
        public ChunkGenerationProfileSnapshot WithGenerationConfiguration(
            IDictionary<string, double> numbers,
            IDictionary<string, string> texts,
            IEnumerable<CaveResourceRuleSnapshot> caveResourceRules)
        {
            return new ChunkGenerationProfileSnapshot(
                ProfileId,
                Signature,
                Width,
                Height,
                numbers ?? new Dictionary<string, double>(numericParameters,
                    StringComparer.Ordinal),
                texts ?? new Dictionary<string, string>(textParameters, StringComparer.Ordinal),
                EcologyGlobalMultiplier,
                EcologyRules,
                caveResourceRules ?? CaveResourceRules,
                PortalPairing);
        }

        /// <summary>附加跨维度地表参考上下文，并让它参与完整生成指纹。</summary>
        public ChunkGenerationProfileSnapshot WithCavePortalPairing(
            CavePortalPairingSnapshot portalPairing)
        {
            if (ReferenceEquals(PortalPairing, portalPairing))
                return this;

            return new ChunkGenerationProfileSnapshot(
                ProfileId,
                Signature,
                Width,
                Height,
                new Dictionary<string, double>(numericParameters, StringComparer.Ordinal),
                new Dictionary<string, string>(textParameters, StringComparer.Ordinal),
                EcologyGlobalMultiplier,
                EcologyRules,
                CaveResourceRules,
                portalPairing);
        }

        /// <summary>按键排序计算稳定 FNV-1a，避免依赖字典枚举顺序和进程随机哈希。</summary>
        private static ulong CalculateGenerationFingerprint(string profileId, int signature,
            int width, int height, IReadOnlyDictionary<string, double> numbers,
            IReadOnlyDictionary<string, string> texts, double ecologyGlobalMultiplier,
            IReadOnlyList<EcologySpawnRuleSnapshot> ecologyRules,
            IReadOnlyList<CaveResourceRuleSnapshot> caveResourceRules,
            CavePortalPairingSnapshot portalPairing)
        {
            ulong hash = 14695981039346656037UL;
            AddString(ref hash, profileId);
            AddLong(ref hash, signature);
            AddLong(ref hash, width);
            AddLong(ref hash, height);

            var numberKeys = new List<string>(numbers.Keys);
            numberKeys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < numberKeys.Count; i++)
            {
                string key = numberKeys[i];
                AddString(ref hash, key);
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(numbers[key]));
            }

            var textKeys = new List<string>(texts.Keys);
            textKeys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < textKeys.Count; i++)
            {
                string key = textKeys[i];
                AddString(ref hash, key);
                AddString(ref hash, texts[key] ?? string.Empty);
            }
            AddEcologyFingerprintFields(ref hash, ecologyGlobalMultiplier, ecologyRules);
            AddCaveResourceFingerprintFields(ref hash, caveResourceRules);
            AddLong(ref hash, unchecked((long)(portalPairing?.Fingerprint ?? 0UL)));
            return hash;
        }

        private static ulong CalculateEcologyFingerprint(double ecologyGlobalMultiplier,
            IReadOnlyList<EcologySpawnRuleSnapshot> ecologyRules)
        {
            ulong hash = 14695981039346656037UL;
            AddEcologyFingerprintFields(ref hash, ecologyGlobalMultiplier, ecologyRules);
            return hash;
        }

        private static void AddEcologyFingerprintFields(ref ulong hash,
            double ecologyGlobalMultiplier, IReadOnlyList<EcologySpawnRuleSnapshot> ecologyRules)
        {
            AddLong(ref hash, BitConverter.DoubleToInt64Bits(ecologyGlobalMultiplier));
            var rules = new List<EcologySpawnRuleSnapshot>(ecologyRules ??
                Array.Empty<EcologySpawnRuleSnapshot>());
            rules.Sort((left, right) => StringComparer.Ordinal.Compare(left.RuleId, right.RuleId));
            for (int i = 0; i < rules.Count; i++)
            {
                EcologySpawnRuleSnapshot rule = rules[i];
                AddString(ref hash, rule.RuleId);
                AddString(ref hash, rule.ItemId);
                AddLong(ref hash, rule.ItemCount);
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.SpawnChance));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.SpawnChanceMultiplier));
                AddLong(ref hash, (int)rule.DistributionMode);
                AddLong(ref hash, rule.PatchSpacing);
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.PatchRadius));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.PatchChance));
                AddLong(ref hash, rule.BiomeMask);
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.MinTemperature));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.MaxTemperature));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.MinPrecipitation));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.MaxPrecipitation));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.MinHeight));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.MaxHeight));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(
                    rule.MinRiverFloodplainStrength));
                AddLong(ref hash, rule.CompanionOnly ? 1 : 0);
                AddString(ref hash, rule.CompanionHostTag);
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.CompanionSpawnChance));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.CompanionOffsetX));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.CompanionOffsetY));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.CompanionMinRadius));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.CompanionMaxRadius));
                var tags = new List<string>(rule.ProvidedTags);
                tags.Sort(StringComparer.OrdinalIgnoreCase);
                for (int tagIndex = 0; tagIndex < tags.Count; tagIndex++)
                    AddString(ref hash, tags[tagIndex]);
            }
        }

        /// <summary>矿物列表有明确优先级，因此按原始配置顺序参与指纹。</summary>
        private static void AddCaveResourceFingerprintFields(ref ulong hash,
            IReadOnlyList<CaveResourceRuleSnapshot> caveResourceRules)
        {
            IReadOnlyList<CaveResourceRuleSnapshot> rules = caveResourceRules ??
                Array.Empty<CaveResourceRuleSnapshot>();
            AddLong(ref hash, rules.Count);
            for (int i = 0; i < rules.Count; i++)
            {
                CaveResourceRuleSnapshot rule = rules[i];
                AddString(ref hash, rule.RuleId);
                AddString(ref hash, rule.ItemId);
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.VeinThreshold));
                AddLong(ref hash, BitConverter.DoubleToInt64Bits(rule.VeinScale));
                AddLong(ref hash, rule.NoiseOffset);
            }
        }

        private static void AddString(ref ulong hash, string value)
        {
            for (int i = 0; i < value.Length; i++)
                AddLong(ref hash, value[i]);
            AddLong(ref hash, 0xff);
        }

        private static void AddLong(ref ulong hash, long value)
        {
            unchecked
            {
                ulong bits = (ulong)value;
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(bits >> (i * 8));
                    hash *= 1099511628211UL;
                }
            }
        }
    }

    /// <summary>
    /// 一张完整的“生成这个区块”任务单。
    /// 它带着世界版本和任务编号，因此旧世界、已取消或被新任务替代的结果不会误用。
    /// </summary>
    public readonly struct ChunkGenerationRequest
    {
        public ChunkGenerationRequest(long worldEpoch, WorldAddress address, int worldSeed,
            long requestVersion, ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology = default)
        {
            if (worldEpoch <= 0)
                throw new ArgumentOutOfRangeException(nameof(worldEpoch));
            if (requestVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestVersion));
            WorldEpoch = worldEpoch;
            Address = address;
            WorldSeed = worldSeed;
            RequestVersion = requestVersion;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Topology = topology;
        }

        /// <summary>创建任务时的世界版本号。</summary>
        public long WorldEpoch { get; }
        /// <summary>要生成哪个区块。</summary>
        public WorldAddress Address { get; }
        /// <summary>世界随机种子；种子相同，生成结果也应该相同。</summary>
        public int WorldSeed { get; }
        /// <summary>这个区块的第几个生成任务；数字越大越新。</summary>
        public long RequestVersion { get; }
        /// <summary>这个任务要使用的生成设置。</summary>
        public ChunkGenerationProfileSnapshot Profile { get; }
        /// <summary>这个世界是否有限并会在边界绕回。</summary>
        public ChunkGenerationTopologySnapshot Topology { get; }
    }

    /// <summary>
    /// 后台生成器交回来的“结果包裹”。
    /// 里面包括纯地形和纯生态放置记录，不包括玩家、怪物或 Unity Item。数据只能各取走一次；没人取就要释放内存。
    /// </summary>
    public sealed class ChunkGenerationResult : IDisposable
    {
        private ChunkTerrainBuffer terrain;
        private ChunkEcologyData ecology;
        private bool disposed;

        public ChunkGenerationResult(ChunkGenerationRequest request, ChunkTerrainBuffer terrain,
            ChunkEcologyData ecology = null)
        {
            Request = request;
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            this.ecology = ecology ?? ChunkEcologyData.Empty;
        }

        /// <summary>这份结果对应哪张任务单。</summary>
        public ChunkGenerationRequest Request { get; }
        /// <summary>包裹里的地形是否已经被区块取走。</summary>
        public bool WasConsumed { get; private set; }
        /// <summary>区块是否已经取走生态放置记录。</summary>
        public bool EcologyWasConsumed { get; private set; }
        /// <summary>这个结果包裹是否已经清理掉。</summary>
        public bool IsDisposed => disposed;

        internal ChunkTerrainData ConsumeTerrain()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ChunkGenerationResult));
            if (WasConsumed)
                throw new InvalidOperationException("Generation result was already consumed.");
            // 把临时草稿变成正式地形并交出去，然后清掉包裹里的引用，防止被取第二次。
            WasConsumed = true;
            ChunkTerrainData result = terrain.Seal();
            terrain = null;
            return result;
        }

        internal ChunkEcologyData ConsumeEcology()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ChunkGenerationResult));
            if (EcologyWasConsumed)
                throw new InvalidOperationException("Ecology data was already consumed.");
            EcologyWasConsumed = true;
            ChunkEcologyData result = ecology;
            ecology = null;
            return result ?? ChunkEcologyData.Empty;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            terrain?.Dispose();
            terrain = null;
            ecology = null;
        }
    }

    /// <summary>所有后台区块生成器都必须提供的 Generate 方法。</summary>
    public interface IChunkPureGenerator
    {
        ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken cancellationToken);
    }
}
