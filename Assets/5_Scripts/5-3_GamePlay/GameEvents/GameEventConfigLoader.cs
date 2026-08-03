using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Gameplay.Events
{
    /// <summary>
    /// Loads and validates modular JSON event catalogs. A broken file or entry is isolated
    /// so the remaining valid event definitions can still run.
    /// </summary>
    public static class GameEventConfigLoader
    {
        public const int SupportedSchemaVersion = 1;
        public const string ResourcePath = "Config/GameEvents/Definitions";

        public static GameEventConfigLoadResult LoadFromResources(bool logIssues = true)
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourcePath);
            Array.Sort(assets, (left, right) => string.CompareOrdinal(left.name, right.name));

            List<GameEventConfigSource> sources = new(assets.Length);
            for (int i = 0; i < assets.Length; i++)
                sources.Add(new GameEventConfigSource($"{assets[i].name}.json", assets[i].text));

            return LoadSources(sources, logIssues);
        }

        public static GameEventConfigLoadResult LoadSources(
            IEnumerable<GameEventConfigSource> sources,
            bool logIssues = true)
        {
            GameEventConfigLoadResult result = new();
            if (sources == null)
            {
                AddIssue(result, "<sources>", "<file>", "Configuration source collection is null.", logIssues);
                return result;
            }

            List<GameEventConfigSource> orderedSources = new(sources);
            orderedSources.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            Dictionary<string, GameEventDefinition> definitionsById = new(StringComparer.Ordinal);

            for (int sourceIndex = 0; sourceIndex < orderedSources.Count; sourceIndex++)
                LoadSource(orderedSources[sourceIndex], definitionsById, result, logIssues);

            result.Definitions.Sort(CompareDefinitions);
            return result;
        }

        private static void LoadSource(
            GameEventConfigSource source,
            Dictionary<string, GameEventDefinition> definitionsById,
            GameEventConfigLoadResult result,
            bool logIssues)
        {
            JObject root;
            try
            {
                root = JObject.Parse(source.Text);
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
                    definition = eventObject.ToObject<GameEventDefinition>(serializer);
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

        internal static string NormalizeType(string value)
        {
            return value?.Trim().ToLowerInvariant() ?? string.Empty;
        }
    }
}
