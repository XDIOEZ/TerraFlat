using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Gameplay.Events
{
    /// <summary>
    /// Loads and validates modular JSON and Lua event catalogs. A broken file or entry is isolated
    /// so the remaining valid event definitions can still run.
    /// </summary>
    public static class GameEventConfigLoader
    {
        public const int SupportedSchemaVersion = 1;
        public const string ResourcePath = "Config/GameEvents/Definitions";

        internal static GameEventConfigLoadResult LoadFromResources(GameEventLuaRuntime luaRuntime, bool logIssues = true)
        {
            List<GameEventConfigResourceDocument> documents = ReadResourceDocuments();
            List<GameEventConfigSource> luaSources = new();

            List<GameEventConfigIssue> luaIssues = new();
            luaRuntime?.LoadCatalogs(luaSources, luaIssues);
            GameEventConfigLoadResult result = LoadResourceDocuments(documents, luaSources, logIssues);
            result.Issues.AddRange(luaIssues);
            if (logIssues)
                foreach (GameEventConfigIssue issue in luaIssues) Debug.LogError(issue.ToString());
            return result;
        }

        [BuiltInContentBake]
        public static void BakeBuiltInConfiguration()
        {
            _ = LoadResourceDocuments(ReadResourceDocuments(), false);
            _ = GameEventLuaRuntime.ReadResourceScripts();
        }

        public static GameEventConfigLoadResult LoadSources(
            IEnumerable<GameEventConfigSource> sources,
            bool logIssues = true)
        {
            GameEventConfigLoadResult result = new();
            LoadSourcesInto(sources, new Dictionary<string, GameEventDefinition>(StringComparer.Ordinal), result, logIssues);
            result.Definitions.Sort(CompareDefinitions);
            return result;
        }

        private static void LoadSourcesInto(
            IEnumerable<GameEventConfigSource> sources,
            Dictionary<string, GameEventDefinition> definitionsById,
            GameEventConfigLoadResult result,
            bool logIssues)
        {
            if (sources == null)
            {
                AddIssue(result, "<sources>", "<file>", "Configuration source collection is null.", logIssues);
                return;
            }

            List<GameEventConfigSource> orderedSources = new(sources);
            orderedSources.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

            for (int sourceIndex = 0; sourceIndex < orderedSources.Count; sourceIndex++)
                LoadSource(orderedSources[sourceIndex], definitionsById, result, logIssues);
        }

        private static List<GameEventConfigResourceDocument> ReadResourceDocuments()
        {
            List<GameEventConfigResourceDocument> documents = new();
            string[] paths = BuiltInContentBinaryCatalog.ReadResourceDirectoryPaths(ResourcePath);
            Array.Sort(paths, CompareResourcePaths);

            for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
            {
                string path = paths[pathIndex];
                GameEventConfigResourceDocument document = BuiltInContentBinaryCatalog.ReadResource(
                    path,
                    text => ParseResourceDocument(path, text));
                if (document != null)
                    documents.Add(document);
            }

            return documents;
        }

        private static GameEventConfigLoadResult LoadResourceDocuments(
            IReadOnlyList<GameEventConfigResourceDocument> documents,
            bool logIssues)
        {
            GameEventConfigLoadResult result = new();
            Dictionary<string, GameEventDefinition> definitionsById = new(StringComparer.Ordinal);
            for (int i = 0; i < documents.Count; i++)
                MergeResourceDocument(documents[i], definitionsById, result, logIssues);
            result.Definitions.Sort(CompareDefinitions);
            return result;
        }

        private static GameEventConfigLoadResult LoadResourceDocuments(
            IReadOnlyList<GameEventConfigResourceDocument> documents,
            List<GameEventConfigSource> sources,
            bool logIssues)
        {
            GameEventConfigLoadResult result = new();
            Dictionary<string, GameEventDefinition> definitionsById = new(StringComparer.Ordinal);
            List<GameEventConfigSource> orderedSources = new(sources);
            orderedSources.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            int documentIndex = 0;
            int sourceIndex = 0;

            while (documentIndex < documents.Count || sourceIndex < orderedSources.Count)
            {
                bool readDocument = sourceIndex >= orderedSources.Count ||
                    (documentIndex < documents.Count &&
                     string.CompareOrdinal(documents[documentIndex].SourceName, orderedSources[sourceIndex].Name) <= 0);
                if (readDocument)
                    MergeResourceDocument(documents[documentIndex++], definitionsById, result, logIssues);
                else
                    LoadSource(orderedSources[sourceIndex++], definitionsById, result, logIssues);
            }

            result.Definitions.Sort(CompareDefinitions);
            return result;
        }

        private static void MergeResourceDocument(
            GameEventConfigResourceDocument document,
            Dictionary<string, GameEventDefinition> definitionsById,
            GameEventConfigLoadResult result,
            bool logIssues)
        {
            GameEventConfigIssueData[] issues = document.Issues ?? Array.Empty<GameEventConfigIssueData>();
            for (int issueIndex = 0; issueIndex < issues.Length; issueIndex++)
            {
                GameEventConfigIssueData issueData = issues[issueIndex];
                AddIssue(result, issueData.SourceName, issueData.EventId, issueData.Message, logIssues);
            }

            GameEventDefinition[] definitions = document.Definitions ?? Array.Empty<GameEventDefinition>();
            for (int definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
            {
                GameEventDefinition definition = definitions[definitionIndex];
                if (definition == null)
                    continue;
                definition.SourceName = document.SourceName;
                if (definitionsById.TryGetValue(definition.Id, out GameEventDefinition duplicate))
                {
                    AddIssue(
                        result,
                        document.SourceName,
                        definition.Id,
                        $"Duplicate id; already defined in '{duplicate.SourceName}'.",
                        logIssues);
                    continue;
                }

                definitionsById.Add(definition.Id, definition);
                if (definition.Enabled)
                    result.Definitions.Add(definition);
            }
        }

        private static GameEventConfigResourceDocument ParseResourceDocument(string path, string text)
        {
            string sourceName = $"{GetResourceAssetName(path)}.json";
            GameEventConfigLoadResult parsed = new();
            Dictionary<string, GameEventDefinition> definitionsById = new(StringComparer.Ordinal);
            List<GameEventDefinition> definitions = new();
            LoadSource(new GameEventConfigSource(sourceName, text), definitionsById, parsed, false, definitions);

            GameEventConfigIssueData[] issues = new GameEventConfigIssueData[parsed.Issues.Count];
            for (int i = 0; i < issues.Length; i++)
            {
                GameEventConfigIssue issue = parsed.Issues[i];
                issues[i] = new GameEventConfigIssueData
                {
                    SourceName = issue.SourceName,
                    EventId = issue.EventId,
                    Message = issue.Message
                };
            }

            return new GameEventConfigResourceDocument
            {
                SourceName = sourceName,
                Definitions = definitions.ToArray(),
                Issues = issues
            };
        }

        private static void LoadSource(
            GameEventConfigSource source,
            Dictionary<string, GameEventDefinition> definitionsById,
            GameEventConfigLoadResult result,
            bool logIssues,
            List<GameEventDefinition> acceptedDefinitions = null)
        {
            JObject root;
            try
            {
                root = JsonConfigurationCache.ParseObject(source.Text);
            }
            catch (Exception exception)
            {
                AddIssue(result, source.Name, "<file>", $"Invalid JSON: {exception.Message}", logIssues);
                return;
            }

            if (!int.TryParse(
                    root["schemaVersion"]?.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int schemaVersion) ||
                schemaVersion != SupportedSchemaVersion)
            {
                AddIssue(
                    result,
                    source.Name,
                    "<file>",
                    $"Unsupported schemaVersion={schemaVersion}; expected {SupportedSchemaVersion}.",
                    logIssues);
                return;
            }

            if (root["events"] is not JArray eventArray)
            {
                AddIssue(result, source.Name, "<file>", "'events' must be an array.", logIssues);
                return;
            }

            JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                MissingMemberHandling = MissingMemberHandling.Ignore
            });

            for (int eventIndex = 0; eventIndex < eventArray.Count; eventIndex++)
            {
                if (eventArray[eventIndex] is not JObject eventObject)
                {
                    AddIssue(result, source.Name, $"<index {eventIndex}>", "Event entry must be an object.", logIssues);
                    continue;
                }

                GameEventDefinition definition;
                try
                {
                    definition = JsonConfigurationCache.FromToken<GameEventDefinition>(eventObject, serializer);
                }
                catch (Exception exception)
                {
                    string rawId = eventObject.Value<string>("id") ?? $"<index {eventIndex}>";
                    AddIssue(result, source.Name, rawId, $"Cannot deserialize entry: {exception.Message}", logIssues);
                    continue;
                }

                if (definition == null)
                    continue;

                definition.SourceName = source.Name;
                Normalize(definition);
                if (!Validate(definition, result, logIssues))
                    continue;

                if (definitionsById.TryGetValue(definition.Id, out GameEventDefinition duplicate))
                {
                    AddIssue(
                        result,
                        source.Name,
                        definition.Id,
                        $"Duplicate id; already defined in '{duplicate.SourceName}'.",
                        logIssues);
                    continue;
                }

                definitionsById.Add(definition.Id, definition);
                acceptedDefinitions?.Add(definition);
                if (definition.Enabled)
                    result.Definitions.Add(definition);
            }
        }

        private static void Normalize(GameEventDefinition definition)
        {
            definition.Id = definition.Id?.Trim();
            definition.DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.Id
                : definition.DisplayName.Trim();
            definition.Description = definition.Description?.Trim() ?? string.Empty;
            definition.ConflictGroup = definition.ConflictGroup?.Trim() ?? string.Empty;
            definition.Trigger ??= new GameEventExtensionDefinition();
            definition.Trigger.Type = NormalizeType(definition.Trigger.Type);
            definition.Trigger.Parameters ??= new JObject();
            definition.Conditions ??= new List<GameEventExtensionDefinition>();
            definition.Actions ??= new List<GameEventActionDefinition>();

            for (int i = 0; i < definition.Conditions.Count; i++)
            {
                GameEventExtensionDefinition condition = definition.Conditions[i];
                if (condition == null)
                    continue;
                condition.Type = NormalizeType(condition.Type);
                condition.Parameters ??= new JObject();
            }

            for (int i = 0; i < definition.Actions.Count; i++)
            {
                GameEventActionDefinition action = definition.Actions[i];
                if (action == null)
                    continue;
                action.Id = action.Id?.Trim();
                action.Type = NormalizeType(action.Type);
                action.Parameters ??= new JObject();
            }
        }

        private static bool Validate(
            GameEventDefinition definition,
            GameEventConfigLoadResult result,
            bool logIssues)
        {
            bool valid = true;
            if (string.IsNullOrWhiteSpace(definition.Id))
                valid &= Report(definition, "id cannot be empty.", result, logIssues);
            if (string.IsNullOrWhiteSpace(definition.Trigger?.Type))
                valid &= Report(definition, "trigger.type cannot be empty.", result, logIssues);
            if (definition.DurationDays < 0f)
                valid &= Report(definition, "durationDays cannot be negative.", result, logIssues);
            if (definition.CooldownDays < 0f)
                valid &= Report(definition, "cooldownDays cannot be negative.", result, logIssues);

            for (int i = 0; i < definition.Conditions.Count; i++)
            {
                GameEventExtensionDefinition condition = definition.Conditions[i];
                if (condition == null || string.IsNullOrWhiteSpace(condition.Type))
                    valid &= Report(definition, $"conditions[{i}].type cannot be empty.", result, logIssues);
            }

            if (definition.Actions.Count == 0)
            {
                valid &= Report(definition, "actions cannot be empty.", result, logIssues);
            }
            else
            {
                HashSet<string> actionIds = new(StringComparer.Ordinal);
                for (int i = 0; i < definition.Actions.Count; i++)
                {
                    GameEventActionDefinition action = definition.Actions[i];
                    if (action == null)
                    {
                        valid &= Report(definition, $"actions[{i}] cannot be null.", result, logIssues);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(action.Id))
                        valid &= Report(definition, $"actions[{i}].id cannot be empty.", result, logIssues);
                    else if (!actionIds.Add(action.Id))
                        valid &= Report(definition, $"Duplicate action id '{action.Id}'.", result, logIssues);
                    if (string.IsNullOrWhiteSpace(action.Type))
                        valid &= Report(definition, $"actions[{i}].type cannot be empty.", result, logIssues);
                }
            }

            return valid;
        }

        private static bool Report(
            GameEventDefinition definition,
            string message,
            GameEventConfigLoadResult result,
            bool logIssues)
        {
            AddIssue(result, definition.SourceName, definition.Id, message, logIssues);
            return false;
        }

        private static void AddIssue(
            GameEventConfigLoadResult result,
            string sourceName,
            string eventId,
            string message,
            bool logIssues)
        {
            GameEventConfigIssue issue = new(sourceName, eventId, message);
            result.Issues.Add(issue);
            if (logIssues)
                Debug.LogError(issue.ToString());
        }

        private static int CompareDefinitions(GameEventDefinition left, GameEventDefinition right)
        {
            int priorityComparison = right.Priority.CompareTo(left.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : string.CompareOrdinal(left.Id, right.Id);
        }

        private static int CompareResourcePaths(string left, string right)
        {
            int nameComparison = string.CompareOrdinal(GetResourceAssetName(left), GetResourceAssetName(right));
            return nameComparison != 0 ? nameComparison : string.CompareOrdinal(left, right);
        }

        private static string GetResourceAssetName(string path)
        {
            int start = path.LastIndexOf('/') + 1;
            int end = path.LastIndexOf('.');
            if (end < start)
                end = path.Length;
            return path.Substring(start, end - start);
        }

        internal static string NormalizeType(string value)
        {
            return value?.Trim().ToLowerInvariant() ?? string.Empty;
        }
    }

    [MemoryPack.MemoryPackable]
    internal sealed partial class GameEventConfigResourceDocument
    {
        public string SourceName;
        public GameEventDefinition[] Definitions = Array.Empty<GameEventDefinition>();
        public GameEventConfigIssueData[] Issues = Array.Empty<GameEventConfigIssueData>();
    }

    [MemoryPack.MemoryPackable]
    internal sealed partial class GameEventConfigIssueData
    {
        public string SourceName;
        public string EventId;
        public string Message;
    }
}
