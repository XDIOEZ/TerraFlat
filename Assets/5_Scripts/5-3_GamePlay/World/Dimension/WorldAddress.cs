using System;

[Serializable]
public readonly struct WorldAddress : IEquatable<WorldAddress>
{
    #region 逻辑世界身份
    public const string SurfaceDimensionId = "surface";
    public const string CaveDimensionId = "cave";
    public const string Separator = "__dimension__";

    // 根键用于关联一组世界，星球归属由世界数据的 BodyId 表达。
    public readonly string RootWorldKey;
    public readonly string DimensionId;

    public WorldAddress(string rootWorldKey, string dimensionId)
    {
        RootWorldKey = NormalizeRootWorldKey(rootWorldKey);
        DimensionId = NormalizeDimensionId(dimensionId);
    }

    public bool IsSurface => DimensionId == SurfaceDimensionId;
    public bool IsValid => !string.IsNullOrWhiteSpace(RootWorldKey) && !string.IsNullOrWhiteSpace(DimensionId);
    public string WorldKey => IsSurface ? RootWorldKey : $"{RootWorldKey}{Separator}{DimensionId}";

    public static WorldAddress FromWorldKey(string worldKey)
    {
        string normalized = string.IsNullOrWhiteSpace(worldKey) ? "地球" : worldKey.Trim();
        int separatorIndex = normalized.IndexOf(Separator, StringComparison.Ordinal);
        if (separatorIndex < 0)
            return new WorldAddress(normalized, SurfaceDimensionId);

        return new WorldAddress(normalized.Substring(0, separatorIndex),
            normalized.Substring(separatorIndex + Separator.Length));
    }

    public WorldAddress WithDimension(string dimensionId) => new(RootWorldKey, dimensionId);

    public bool Equals(WorldAddress other)
    {
        return string.Equals(RootWorldKey, other.RootWorldKey, StringComparison.Ordinal) &&
               string.Equals(DimensionId, other.DimensionId, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) => obj is WorldAddress other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((RootWorldKey != null ? RootWorldKey.GetHashCode() : 0) * 397) ^
                   (DimensionId != null ? DimensionId.GetHashCode() : 0);
        }
    }

    public override string ToString() => WorldKey;

    public static bool operator ==(WorldAddress left, WorldAddress right) => left.Equals(right);

    public static bool operator !=(WorldAddress left, WorldAddress right) => !left.Equals(right);

    private static string NormalizeRootWorldKey(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "地球" : value.Trim();
        return normalized.Replace(Separator, "_");
    }

    private static string NormalizeDimensionId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? SurfaceDimensionId : value.Trim().ToLowerInvariant();
    }
    #endregion
}
