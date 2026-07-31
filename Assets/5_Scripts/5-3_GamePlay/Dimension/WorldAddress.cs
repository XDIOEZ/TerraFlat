using System;

[Serializable]
public struct WorldAddress : IEquatable<WorldAddress>
{
    public const string SurfaceDimensionId = "surface";
    public const string CaveDimensionId = "cave";
    public const string Separator = "__dimension__";

    public string PlanetId;
    public string DimensionId;

    public WorldAddress(string planetId, string dimensionId)
    {
        PlanetId = NormalizePlanetId(planetId);
        DimensionId = NormalizeDimensionId(dimensionId);
    }

    public bool IsSurface => DimensionId == SurfaceDimensionId;
    public bool IsValid => !string.IsNullOrWhiteSpace(PlanetId) && !string.IsNullOrWhiteSpace(DimensionId);
    public string WorldKey => IsSurface ? PlanetId : $"{PlanetId}{Separator}{DimensionId}";

    public static WorldAddress FromWorldKey(string worldKey)
    {
        string normalized = string.IsNullOrWhiteSpace(worldKey) ? "地球" : worldKey.Trim();
        int separatorIndex = normalized.IndexOf(Separator, StringComparison.Ordinal);
        if (separatorIndex < 0)
            return new WorldAddress(normalized, SurfaceDimensionId);

        string planetId = normalized.Substring(0, separatorIndex);
        string dimensionId = normalized.Substring(separatorIndex + Separator.Length);
        return new WorldAddress(planetId, dimensionId);
    }

    public WorldAddress WithDimension(string dimensionId)
    {
        return new WorldAddress(PlanetId, dimensionId);
    }

    public bool Equals(WorldAddress other)
    {
        return string.Equals(PlanetId, other.PlanetId, StringComparison.Ordinal) &&
               string.Equals(DimensionId, other.DimensionId, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is WorldAddress other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((PlanetId != null ? PlanetId.GetHashCode() : 0) * 397) ^
                   (DimensionId != null ? DimensionId.GetHashCode() : 0);
        }
    }

    public override string ToString()
    {
        return WorldKey;
    }

    public static bool operator ==(WorldAddress left, WorldAddress right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(WorldAddress left, WorldAddress right)
    {
        return !left.Equals(right);
    }

    private static string NormalizePlanetId(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "地球" : value.Trim();
        return normalized.Replace(Separator, "_");
    }

    private static string NormalizeDimensionId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? SurfaceDimensionId : value.Trim().ToLowerInvariant();
    }
}
