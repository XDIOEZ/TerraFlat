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
    /// 一次区块生成要用的全部设置副本。
    /// 创建后就不再改变，所以 Unity 面板或 MOD 后来修改设置，也不会干扰正在后台生成的区块。
    /// </summary>
    public sealed class ChunkGenerationProfileSnapshot
    {
        private readonly IReadOnlyDictionary<string, double> numericParameters;
        private readonly IReadOnlyDictionary<string, string> textParameters;

        public ChunkGenerationProfileSnapshot(string profileId, int signature, int width, int height,
            IDictionary<string, double> numericParameters = null,
            IDictionary<string, string> textParameters = null)
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
        /// <summary>给 MOD 使用的数字设置副本。</summary>
        public IReadOnlyDictionary<string, double> NumericParameters => numericParameters;
        /// <summary>给 MOD 使用的文字设置副本。</summary>
        public IReadOnlyDictionary<string, string> TextParameters => textParameters;
        /// <summary>游戏自带生成器使用的、已经整理和检查过的设置。</summary>
        public ChunkGenerationSettingsSnapshot Settings { get; }
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
    /// 里面只有地形，不包括玩家、怪物或物品。地形只能从包裹里取走一次；没人取就要释放内存。
    /// </summary>
    public sealed class ChunkGenerationResult : IDisposable
    {
        private ChunkTerrainBuffer terrain;
        private bool disposed;

        public ChunkGenerationResult(ChunkGenerationRequest request, ChunkTerrainBuffer terrain)
        {
            Request = request;
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        }

        /// <summary>这份结果对应哪张任务单。</summary>
        public ChunkGenerationRequest Request { get; }
        /// <summary>包裹里的地形是否已经被区块取走。</summary>
        public bool WasConsumed { get; private set; }
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

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            terrain?.Dispose();
            terrain = null;
        }
    }

    /// <summary>所有后台区块生成器都必须提供的 Generate 方法。</summary>
    public interface IChunkPureGenerator
    {
        ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken cancellationToken);
    }
}
