using System;

const int width = 32;
const int height = 32;
const int baselineReads = width * height * 9;
var cache = new TerrainShadowSurfaceSampleCache<int>();
cache.ConfigureForChunkWithOneCellHalo(width, height);
cache.BeginFlushPass();

int surfaceReads = 0;
for (int y = 0; y < height; y++)
for (int x = 0; x < width; x++)
for (int dy = -1; dy <= 1; dy++)
for (int dx = -1; dx <= 1; dx++)
    ReadCachedSurface(x + dx, y + dy);

int expectedHaloSamples = (width + 2) * (height + 2);
Require(surfaceReads == expectedHaloSamples,
    $"full chunk sample count expected {expectedHaloSamples}, got {surfaceReads}");

cache.BeginFlushPass();
int missingReads = 0;
for (int i = 0; i < 2; i++)
{
    if (!cache.TryGetCachedSurfaceSample(-1, -1, out bool available, out _))
    {
        missingReads++;
        cache.StoreSurfaceSample(-1, -1, false, 0);
    }
    else
        Require(!available, "missing neighbour must remain unavailable inside one pass");
}
Require(missingReads == 1, $"missing neighbour sampled {missingReads} times");

cache.BeginFlushPass();
int resourceRevision = 4;
int firstRevision = ReadConfiguredRevision();
resourceRevision = 9;
cache.BeginFlushPass();
int refreshedRevision = ReadConfiguredRevision();
Require(firstRevision == 4 && refreshedRevision == 9,
    "a new flush must observe changed tile-definition configuration");

cache.BeginFlushPass();
int terrainHeight = 2;
int beforeChange = ReadTerrainHeight();
terrainHeight = 7;
cache.InvalidateCurrentFlushPass();
int afterChange = ReadTerrainHeight();
Require(beforeChange == 2 && afterChange == 7,
    "terrain change during a flush must invalidate samples already read");

cache.BeginFlushPass();
int leftSeam = ReadWrappedCell(-1, 0);
int rightSeam = ReadWrappedCell(width, 0);
Require(leftSeam == width - 1 && rightSeam == 0 && leftSeam != rightSeam,
    "left and right wrapped halo samples must retain their own coordinates");
Require(!cache.TryGetCachedSurfaceSample(-2, 0, out _, out _),
    "coordinates beyond the one-cell halo must not enter the cache");

cache.ConfigureForChunkWithOneCellHalo(16, 16);
cache.BeginFlushPass();
cache.StoreSurfaceSample(16, 0, true, 16);
Require(cache.TryGetCachedSurfaceSample(16, 0, out bool edgeAvailable, out int edgeValue) &&
        edgeAvailable && edgeValue == 16,
    "the right border halo must be included after resizing");
Require(!cache.TryGetCachedSurfaceSample(17, 0, out _, out _),
    "the resized cache must reject coordinates beyond its halo");

Console.WriteLine($"PASS full scan reads: {surfaceReads} vs {baselineReads} without reuse");
Console.WriteLine("PASS unavailable neighbours are cached for one flush");
Console.WriteLine("PASS next flush observes changed configuration");
Console.WriteLine("PASS in-flush invalidation rereads changed terrain");
Console.WriteLine("PASS wrapped halo coordinates and resized bounds");

int ReadCachedSurface(int x, int y)
{
    if (cache.TryGetCachedSurfaceSample(x, y, out bool available, out int surface))
        return available ? surface : -1;

    surfaceReads++;
    int wrappedX = PositiveModulo(x, width);
    int wrappedY = PositiveModulo(y, height);
    surface = wrappedY * width + wrappedX;
    cache.StoreSurfaceSample(x, y, true, surface);
    return surface;
}

int ReadConfiguredRevision()
{
    if (cache.TryGetCachedSurfaceSample(0, 0, out bool available, out int surface))
        return available ? surface : -1;
    cache.StoreSurfaceSample(0, 0, true, resourceRevision);
    return resourceRevision;
}

int ReadTerrainHeight()
{
    if (cache.TryGetCachedSurfaceSample(1, 1, out bool available, out int surface))
        return available ? surface : -1;
    cache.StoreSurfaceSample(1, 1, true, terrainHeight);
    return terrainHeight;
}

int ReadWrappedCell(int x, int y)
{
    if (cache.TryGetCachedSurfaceSample(x, y, out bool available, out int surface))
        return available ? surface : -1;
    int wrappedX = PositiveModulo(x, width);
    cache.StoreSurfaceSample(x, y, true, wrappedX);
    return wrappedX;
}

static int PositiveModulo(int value, int modulus) => (value % modulus + modulus) % modulus;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
