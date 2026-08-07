using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

[CreateAssetMenu(fileName = "ChunkGenerationProfile", menuName = "FlatWorld/World/Chunk Generation Profile")]
public sealed class ChunkGenerationProfileSO : ScriptableObject
{
    [Serializable]
    private struct NumericParameter
    {
        public string Id;
        public double Value;
    }

    [Serializable]
    private struct TextParameter
    {
        public string Id;
        public string Value;
    }

    [SerializeField] private string profileId = "surface.default";
    [SerializeField] private int generationSignature = 6;
    [SerializeField, Min(1)] private int chunkWidth = 100;
    [SerializeField, Min(1)] private int chunkHeight = 100;
    [SerializeField] private List<NumericParameter> numericParameters = new();
    [SerializeField] private List<TextParameter> textParameters = new();

    public string ProfileId => profileId;
    public int GenerationSignature => generationSignature;
    public int ChunkWidth => chunkWidth;
    public int ChunkHeight => chunkHeight;

    public ChunkGenerationProfileSnapshot CreateSnapshot()
    {
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < numericParameters.Count; i++)
        {
            NumericParameter parameter = numericParameters[i];
            if (string.IsNullOrWhiteSpace(parameter.Id))
                continue;
            if (!numbers.TryAdd(parameter.Id, parameter.Value))
                throw new InvalidOperationException($"Duplicate numeric generation parameter: {parameter.Id}");
        }

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < textParameters.Count; i++)
        {
            TextParameter parameter = textParameters[i];
            if (string.IsNullOrWhiteSpace(parameter.Id))
                continue;
            if (!texts.TryAdd(parameter.Id, parameter.Value ?? string.Empty))
                throw new InvalidOperationException($"Duplicate text generation parameter: {parameter.Id}");
        }

        return new ChunkGenerationProfileSnapshot(
            profileId, generationSignature, chunkWidth, chunkHeight, numbers, texts);
    }
}
