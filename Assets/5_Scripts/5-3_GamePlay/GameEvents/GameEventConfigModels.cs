using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Gameplay.Events
{
    [Serializable]
    public sealed class GameEventCatalogDefinition
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion;

        [JsonProperty("events")]
        public List<GameEventDefinition> Events = new();
    }

    [Serializable]
    public sealed class GameEventDefinition
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("displayName")]
        public string DisplayName;

        [JsonProperty("description")]
        public string Description;

        [JsonProperty("enabled")]
        public bool Enabled = true;

        [JsonProperty("priority")]
        public int Priority;

        [JsonProperty("conflictGroup")]
        public string ConflictGroup;

        [JsonProperty("oncePerWorld")]
        public bool OncePerWorld;

        [JsonProperty("cooldownDays")]
        public float CooldownDays;

        [JsonProperty("durationDays")]
        public float DurationDays;

        [JsonProperty("trigger")]
        public GameEventExtensionDefinition Trigger = new();

        [JsonProperty("conditions")]
        public List<GameEventExtensionDefinition> Conditions = new();

        [JsonProperty("actions")]
        public List<GameEventActionDefinition> Actions = new();

        [JsonIgnore]
        public string SourceName;
    }

    [Serializable]
    public class GameEventExtensionDefinition
    {
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("parameters")]
        public JObject Parameters = new();
    }

    [Serializable]
    public sealed class GameEventActionDefinition : GameEventExtensionDefinition
    {
        [JsonProperty("id")]
        public string Id;
    }

    public sealed class GameEventConfigSource
    {
        public string Name { get; }
        public string Text { get; }

        public GameEventConfigSource(string name, string text)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "<unnamed>" : name;
            Text = text ?? string.Empty;
        }
    }

    public sealed class GameEventConfigIssue
    {
        public string SourceName { get; }
        public string EventId { get; }
        public string Message { get; }

        public GameEventConfigIssue(string sourceName, string eventId, string message)
        {
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "<unknown>" : sourceName;
            EventId = string.IsNullOrWhiteSpace(eventId) ? "<file>" : eventId;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            return $"[GameEventConfig] file='{SourceName}', event='{EventId}': {Message}";
        }
    }

    public sealed class GameEventConfigLoadResult
    {
        public List<GameEventDefinition> Definitions { get; } = new();
        public List<GameEventConfigIssue> Issues { get; } = new();
        public bool HasErrors => Issues.Count > 0;
    }
}
