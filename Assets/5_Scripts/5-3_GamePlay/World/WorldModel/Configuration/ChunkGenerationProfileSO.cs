using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "ChunkGenerationProfile", menuName = "FlatWorld/World/Chunk Generation Profile")]
public sealed class ChunkGenerationProfileSO : ScriptableObject
{
    [Serializable]
    private struct NumericParameter
    {
        [LabelText("参数标识")]
        public string Id;

        [LabelText("参数数值")]
        public double Value;
    }

    [Serializable]
    private struct TextParameter
    {
        [LabelText("参数标识")]
        public string Id;

        [LabelText("文本内容")]
        public string Value;
    }

    [SerializeField, LabelText("配置标识")] private string profileId = "surface.default";
    [SerializeField, LabelText("生成签名")] private int generationSignature =
        DeterministicChunkGenerator.CurrentGenerationSignature;
    [SerializeField, LabelText("区块宽度"), Min(1)] private int chunkWidth = 100;
    [SerializeField, LabelText("区块高度"), Min(1)] private int chunkHeight = 100;
    [SerializeField, LabelText("数值参数列表")] private List<NumericParameter> numericParameters = new();
    [SerializeField, LabelText("文本参数列表")] private List<TextParameter> textParameters = new();

    public string ProfileId => profileId;
    public int GenerationSignature => generationSignature;
    public int ChunkWidth => chunkWidth;
    public int ChunkHeight => chunkHeight;

    /// <summary>把 Unity 资源中的参数复制成后台线程可以安全读取的配置快照。</summary>
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
