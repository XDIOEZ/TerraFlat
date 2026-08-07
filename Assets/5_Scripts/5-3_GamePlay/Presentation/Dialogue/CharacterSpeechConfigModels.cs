using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FlatWorld.Dialogue
{
    #region JSON 配置模型

    [Serializable]
    public sealed class CharacterSpeechConfigRoot
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion;

        [JsonProperty("entries")]
        public List<CharacterSpeechConfigEntry> Entries = new();
    }

    [Serializable]
    public sealed class CharacterSpeechConfigEntry
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("topic")]
        public string Topic;

        [JsonProperty("triggers", ItemConverterType = typeof(StringEnumConverter))]
        public List<CharacterSpeechTrigger> Triggers = new();

        [JsonProperty("priority")]
        [JsonConverter(typeof(StringEnumConverter))]
        public CharacterSpeechPriority Priority;

        [JsonProperty("conditions")]
        public List<CharacterSpeechCondition> Conditions = new();

        [JsonProperty("lines")]
        public List<string> Lines = new();

        [JsonProperty("duration")]
        public float Duration;

        [JsonProperty("cooldown")]
        public float Cooldown;

        [JsonProperty("once")]
        public bool Once;

        [JsonProperty("completionFlag")]
        public string CompletionFlag;

        [JsonProperty("enabled")]
        public bool Enabled = true;

        [JsonIgnore]
        public string SourceName;
    }

    [Serializable]
    public sealed class CharacterSpeechCondition
    {
        [JsonProperty("fact")]
        public string Fact;

        [JsonProperty("operator")]
        [JsonConverter(typeof(StringEnumConverter))]
        public CharacterSpeechConditionOperator Operator;

        [JsonProperty("value")]
        public string Value;
    }

    public enum CharacterSpeechConditionOperator
    {
        Equal,
        NotEqual,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual,
        Exists,
        NotExists
    }

    #endregion

    #region 加载结果

    public sealed class CharacterSpeechConfigSource
    {
        public string Name { get; }
        public string Text { get; }

        public CharacterSpeechConfigSource(string name, string text)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "<未命名配置>" : name;
            Text = text ?? string.Empty;
        }
    }

    public sealed class CharacterSpeechConfigIssue
    {
        public string SourceName { get; }
        public string EntryId { get; }
        public string FactName { get; }
        public string Message { get; }

        public CharacterSpeechConfigIssue(
            string sourceName,
            string entryId,
            string factName,
            string message)
        {
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "<未知配置>" : sourceName;
            EntryId = string.IsNullOrWhiteSpace(entryId) ? "<未知条目>" : entryId;
            FactName = factName ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            string fact = string.IsNullOrWhiteSpace(FactName)
                ? string.Empty
                : $"，Fact '{FactName}'";
            return $"[自言自语配置] 文件 '{SourceName}'，条目 '{EntryId}'{fact}：{Message}";
        }
    }

    public sealed class CharacterSpeechConfigLoadResult
    {
        public List<CharacterSpeechConfigEntry> Entries { get; } = new();
        public List<CharacterSpeechConfigIssue> Issues { get; } = new();
        public bool HasErrors => Issues.Count > 0;
    }

    #endregion
}
