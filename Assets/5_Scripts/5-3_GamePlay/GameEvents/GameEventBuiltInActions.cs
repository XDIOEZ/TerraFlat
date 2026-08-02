using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Gameplay.Events
{
    [Serializable]
    public sealed class CreatureWavesGameEventActionParameters
    {
        [JsonProperty("prefabId")]
        public string PrefabId;

        [JsonProperty("waves")]
        public int Waves = 1;

        [JsonProperty("countPerWave")]
        public int CountPerWave = 1;

        [JsonProperty("countPerPlayer")]
        public bool CountPerPlayer;

        [JsonProperty("waveIntervalGameSeconds")]
        public float WaveIntervalGameSeconds;

        [JsonProperty("maxSpawnAttemptsPerTick")]
        public int MaxSpawnAttemptsPerTick = 4;

        [JsonProperty("minDistance")]
        public float MinDistance = 10f;

        [JsonProperty("maxDistance")]
        public float MaxDistance = 30f;

        [JsonProperty("playerVisibilityExclusionDistance")]
        public float PlayerVisibilityExclusionDistance = 8f;

        [JsonProperty("searchAttemptsPerCreature")]
        public int SearchAttemptsPerCreature = 16;

        [JsonProperty("requireGlobalDarkness")]
        public bool RequireGlobalDarkness;

        [JsonProperty("requireCompletelyDarkTile")]
        public bool RequireCompletelyDarkTile;

        [JsonProperty("maxAllowedTileLight")]
        public float MaxAllowedTileLight = 1f;

        [JsonProperty("allowedBiomes")]
        public List<string> AllowedBiomes = new();
    }

    [Serializable]
    internal sealed class CreatureWavesRuntimeState
    {
        public int NextWaveIndex;
        public int PendingInCurrentWave;
        public float NextWaveTotalTime;
    }

    public sealed class CreatureWavesGameEventAction : IGameEventActionHandler
    {
        public string Type => "creature.waves";

        public bool Validate(JObject parameters, out string error)
        {
            CreatureWavesGameEventActionParameters value = Read(parameters);
            if (string.IsNullOrWhiteSpace(value.PrefabId))
            {
                error = "prefabId cannot be empty.";
                return false;
            }
            if (value.Waves < 1 || value.CountPerWave < 1)
            {
                error = "waves and countPerWave must be at least 1.";
                return false;
            }
            if (value.WaveIntervalGameSeconds < 0f)
            {
                error = "waveIntervalGameSeconds cannot be negative.";
                return false;
            }
            if (value.MinDistance < 0f || value.MaxDistance < value.MinDistance)
            {
                error = "Spawn distances are invalid; maxDistance must be >= minDistance >= 0.";
                return false;
            }
            if (value.MaxAllowedTileLight < 0f || value.MaxAllowedTileLight > 1f)
            {
                error = "maxAllowedTileLight must be between 0 and 1.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public GameEventActionStatus Begin(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            CreatureWavesRuntimeState runtime = ReadRuntime(state);
            if (runtime.NextWaveTotalTime <= 0f)
                runtime.NextWaveTotalTime = context.ActiveEvent.StartedTotalTime;
            WriteRuntime(state, runtime);
            return Tick(context, parameters, state);
        }

        public void Resume(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            // All required wave progress is already persisted in RuntimeDataJson.
        }

        public GameEventActionStatus Tick(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            CreatureWavesGameEventActionParameters value = Read(parameters);
            CreatureWavesRuntimeState runtime = ReadRuntime(state);
            if (runtime.NextWaveIndex >= value.Waves && runtime.PendingInCurrentWave <= 0)
                return GameEventActionStatus.Completed;

            MonsterSpawnerManager spawner = MonsterSpawnerManager.Instance;
            if (spawner == null)
                return GameEventActionStatus.Running;

            if (runtime.PendingInCurrentWave <= 0)
            {
                if (context.CurrentTotalTime + 0.0001f < runtime.NextWaveTotalTime)
                    return GameEventActionStatus.Running;

                int playerMultiplier = value.CountPerPlayer
                    ? Mathf.Max(1, spawner.GetEventSpawnPlayerCount(context.ActiveWorldKey))
                    : 1;
                runtime.PendingInCurrentWave = Mathf.Max(1, value.CountPerWave) * playerMultiplier;
            }

            int attemptCount = Mathf.Min(
                runtime.PendingInCurrentWave,
                Mathf.Max(1, value.MaxSpawnAttemptsPerTick));
            int spawned = spawner.SpawnEventCreatures(new GameEventCreatureSpawnRequest
            {
                WorldKey = context.ActiveWorldKey,
                PrefabId = value.PrefabId,
                Count = attemptCount,
                MinDistance = value.MinDistance,
                MaxDistance = value.MaxDistance,
                PlayerVisibilityExclusionDistance = value.PlayerVisibilityExclusionDistance,
                SearchAttemptsPerCreature = Mathf.Max(1, value.SearchAttemptsPerCreature),
                RequireGlobalDarkness = value.RequireGlobalDarkness,
                RequireCompletelyDarkTile = value.RequireCompletelyDarkTile,
                MaxAllowedTileLight = Mathf.Clamp01(value.MaxAllowedTileLight),
                AllowedBiomes = value.AllowedBiomes ?? new List<string>()
            });

            runtime.PendingInCurrentWave = Mathf.Max(0, runtime.PendingInCurrentWave - spawned);
            if (runtime.PendingInCurrentWave <= 0)
            {
                runtime.NextWaveIndex++;
                runtime.NextWaveTotalTime = context.ActiveEvent.StartedTotalTime +
                                            runtime.NextWaveIndex *
                                            Mathf.Max(0f, value.WaveIntervalGameSeconds);
            }

            WriteRuntime(state, runtime);
            return runtime.NextWaveIndex >= value.Waves && runtime.PendingInCurrentWave <= 0
                ? GameEventActionStatus.Completed
                : GameEventActionStatus.Running;
        }

        public void End(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state,
            bool cancelled)
        {
            // Spawned actors use the normal creature lifecycle and are not deleted at event end.
        }

        private static CreatureWavesGameEventActionParameters Read(JObject parameters)
        {
            return parameters?.ToObject<CreatureWavesGameEventActionParameters>()
                   ?? new CreatureWavesGameEventActionParameters();
        }

        private static CreatureWavesRuntimeState ReadRuntime(GameEventActionRuntimeSaveData state)
        {
            if (string.IsNullOrWhiteSpace(state?.RuntimeDataJson))
                return new CreatureWavesRuntimeState();

            try
            {
                return JsonConvert.DeserializeObject<CreatureWavesRuntimeState>(state.RuntimeDataJson)
                       ?? new CreatureWavesRuntimeState();
            }
            catch
            {
                return new CreatureWavesRuntimeState();
            }
        }

        private static void WriteRuntime(
            GameEventActionRuntimeSaveData state,
            CreatureWavesRuntimeState runtime)
        {
            state.RuntimeDataJson = JsonConvert.SerializeObject(runtime, Formatting.None);
        }
    }

    [Serializable]
    public sealed class CreatureAdvanceGameEventActionParameters
    {
        [JsonProperty("prefabId")]
        public string PrefabId;

        [JsonProperty("count")]
        public int Count = 1;

        [JsonProperty("maxSpawnAttemptsPerTick")]
        public int MaxSpawnAttemptsPerTick = 4;

        [JsonProperty("minDistance")]
        public float MinDistance = 12f;

        [JsonProperty("maxDistance")]
        public float MaxDistance = 24f;

        [JsonProperty("playerVisibilityExclusionDistance")]
        public float PlayerVisibilityExclusionDistance = 12f;

        [JsonProperty("requireOutsidePlayerView")]
        public bool RequireOutsidePlayerView = true;

        [JsonProperty("searchAttemptsPerCreature")]
        public int SearchAttemptsPerCreature = 24;

        [JsonProperty("arrivalDistance")]
        public float ArrivalDistance = 1.25f;

        [JsonProperty("attackActorsOnRoute")]
        public bool AttackActorsOnRoute = true;

        [JsonProperty("allowedBiomes")]
        public List<string> AllowedBiomes = new();
    }

    [Serializable]
    internal sealed class CreatureAdvanceRuntimeState
    {
        public int SpawnedCount;
    }

    /// <summary>
    /// 在触发器提供的目标点周围生成生物，并向支持推进命令的 AI 下发同一目标。
    /// 生成位置同时受所有玩家距离和活动游戏相机视口约束。
    /// </summary>
    public sealed class CreatureAdvanceGameEventAction : IGameEventActionHandler
    {
        public string Type => "creature.advance";

        public bool Validate(JObject parameters, out string error)
        {
            CreatureAdvanceGameEventActionParameters value = Read(parameters);
            if (string.IsNullOrWhiteSpace(value.PrefabId))
            {
                error = "prefabId cannot be empty.";
                return false;
            }

            if (value.Count < 1 || value.MaxSpawnAttemptsPerTick < 1)
            {
                error = "count and maxSpawnAttemptsPerTick must be at least 1.";
                return false;
            }

            if (value.MinDistance < 0f || value.MaxDistance < value.MinDistance)
            {
                error = "Spawn distances are invalid; maxDistance must be >= minDistance >= 0.";
                return false;
            }

            if (value.PlayerVisibilityExclusionDistance < 0f ||
                value.SearchAttemptsPerCreature < 1 ||
                value.ArrivalDistance <= 0f)
            {
                error = "Visibility distance must be non-negative; search attempts and arrival distance must be positive.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public GameEventActionStatus Begin(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            return Tick(context, parameters, state);
        }

        public void Resume(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            // Spawn count and every spawned AI's advance command are persisted independently.
        }

        public GameEventActionStatus Tick(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            CreatureAdvanceGameEventActionParameters value = Read(parameters);
            CreatureAdvanceRuntimeState runtime = ReadRuntime(state);
            if (runtime.SpawnedCount >= value.Count)
                return GameEventActionStatus.Completed;

            if (!TryReadTarget(context.TriggerPayload, out int targetItemGuid, out Vector3 targetPosition))
                throw new InvalidOperationException("Trigger payload does not contain a valid target item position.");

            MonsterSpawnerManager spawner = MonsterSpawnerManager.Instance;
            if (spawner == null)
                return GameEventActionStatus.Running;

            int attemptCount = Mathf.Min(
                value.Count - runtime.SpawnedCount,
                Mathf.Max(1, value.MaxSpawnAttemptsPerTick));
            List<Item> spawnedItems = new(attemptCount);
            int spawned = spawner.SpawnEventCreatures(new GameEventCreatureSpawnRequest
            {
                WorldKey = context.ActiveWorldKey,
                PrefabId = value.PrefabId,
                Count = attemptCount,
                MinDistance = value.MinDistance,
                MaxDistance = value.MaxDistance,
                PlayerVisibilityExclusionDistance = value.PlayerVisibilityExclusionDistance,
                RequireOutsidePlayerView = value.RequireOutsidePlayerView,
                UseSpawnAnchor = true,
                SpawnAnchor = targetPosition,
                SearchAttemptsPerCreature = Mathf.Max(1, value.SearchAttemptsPerCreature),
                AllowedBiomes = value.AllowedBiomes ?? new List<string>()
            }, spawnedItems);

            AIAdvanceCommand command = new(
                targetItemGuid,
                targetPosition,
                value.ArrivalDistance,
                value.AttackActorsOnRoute);
            for (int i = 0; i < spawnedItems.Count; i++)
            {
                if (!TryIssueAdvanceCommand(spawnedItems[i], command))
                {
                    Debug.LogWarning(
                        $"[GameEvent] Spawned '{value.PrefabId}' does not implement " +
                        $"{nameof(IAIAdvanceCommandReceiver)}; it will keep its default AI.",
                        spawnedItems[i]);
                }
            }

            runtime.SpawnedCount += spawned;
            WriteRuntime(state, runtime);
            return runtime.SpawnedCount >= value.Count
                ? GameEventActionStatus.Completed
                : GameEventActionStatus.Running;
        }

        public void End(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state,
            bool cancelled)
        {
            // Advance commands belong to the spawned actors and survive the short-lived event action.
        }

        private static bool TryIssueAdvanceCommand(Item spawnedItem, AIAdvanceCommand command)
        {
            if (spawnedItem == null)
                return false;

            MonoBehaviour[] behaviours = spawnedItem.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is not IAIAdvanceCommandReceiver receiver)
                    continue;

                receiver.BeginAdvance(command);
                return true;
            }

            return false;
        }

        private static bool TryReadTarget(
            JObject payload,
            out int targetItemGuid,
            out Vector3 targetPosition)
        {
            targetItemGuid = payload?.Value<int?>("targetItemGuid") ?? 0;
            targetPosition = default;
            if (payload?["targetPosition"] is not JObject position)
                return false;

            float? x = position.Value<float?>("x");
            float? y = position.Value<float?>("y");
            float? z = position.Value<float?>("z");
            if (!x.HasValue || !y.HasValue || !z.HasValue)
                return false;

            targetPosition = new Vector3(x.Value, y.Value, z.Value);
            return IsFinite(targetPosition);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static CreatureAdvanceGameEventActionParameters Read(JObject parameters)
        {
            return parameters?.ToObject<CreatureAdvanceGameEventActionParameters>()
                   ?? new CreatureAdvanceGameEventActionParameters();
        }

        private static CreatureAdvanceRuntimeState ReadRuntime(GameEventActionRuntimeSaveData state)
        {
            if (string.IsNullOrWhiteSpace(state?.RuntimeDataJson))
                return new CreatureAdvanceRuntimeState();

            try
            {
                return JsonConvert.DeserializeObject<CreatureAdvanceRuntimeState>(state.RuntimeDataJson)
                       ?? new CreatureAdvanceRuntimeState();
            }
            catch
            {
                return new CreatureAdvanceRuntimeState();
            }
        }

        private static void WriteRuntime(
            GameEventActionRuntimeSaveData state,
            CreatureAdvanceRuntimeState runtime)
        {
            state.RuntimeDataJson = JsonConvert.SerializeObject(runtime, Formatting.None);
        }
    }

    [Serializable]
    public sealed class WeatherOverrideGameEventActionParameters
    {
        [JsonProperty("weather")]
        public string Weather = "Rain";

        [JsonProperty("intensity")]
        public float Intensity = 1f;
    }

    [Serializable]
    internal sealed class WeatherOverrideRuntimeState
    {
        public bool Applied;
    }

    public sealed class WeatherOverrideGameEventAction : IGameEventActionHandler
    {
        public string Type => "weather.override";

        public bool Validate(JObject parameters, out string error)
        {
            WeatherOverrideGameEventActionParameters value = Read(parameters);
            if (!Enum.TryParse(value.Weather, true, out WeatherType weather) ||
                !Enum.IsDefined(typeof(WeatherType), weather) ||
                weather == WeatherType.Clear)
            {
                error = $"weather '{value.Weather}' is invalid or Clear.";
                return false;
            }
            if (value.Intensity <= 0f || value.Intensity > 1f)
            {
                error = "intensity must be greater than 0 and at most 1.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public GameEventActionStatus Begin(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            return Apply(context, parameters, state);
        }

        public void Resume(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            Apply(context, parameters, state);
        }

        public GameEventActionStatus Tick(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            return Apply(context, parameters, state);
        }

        public void End(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state,
            bool cancelled)
        {
            WeatherMgr.Instance?.ClearGameEventWeather(context.Definition.Id);
        }

        private static GameEventActionStatus Apply(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            WeatherOverrideGameEventActionParameters value = Read(parameters);
            if (!Enum.TryParse(value.Weather, true, out WeatherType weather))
                return GameEventActionStatus.Running;

            bool applied = WeatherMgr.Instance != null &&
                           WeatherMgr.Instance.ApplyGameEventWeather(
                               context.Definition.Id,
                               weather,
                               Mathf.Clamp01(value.Intensity),
                               context.ActiveEvent.EndTotalTime);
            state.RuntimeDataJson = JsonConvert.SerializeObject(
                new WeatherOverrideRuntimeState { Applied = applied },
                Formatting.None);
            return applied ? GameEventActionStatus.Completed : GameEventActionStatus.Running;
        }

        private static WeatherOverrideGameEventActionParameters Read(JObject parameters)
        {
            return parameters?.ToObject<WeatherOverrideGameEventActionParameters>()
                   ?? new WeatherOverrideGameEventActionParameters();
        }
    }

    [Serializable]
    public sealed class EmitSignalGameEventActionParameters
    {
        [JsonProperty("signal")]
        public string Signal;

        [JsonProperty("payload")]
        public JObject Payload = new();
    }

    [Serializable]
    internal sealed class EmitSignalRuntimeState
    {
        public bool Emitted;
    }

    public sealed class EmitSignalGameEventAction : IGameEventActionHandler
    {
        public string Type => "signal.emit";

        public bool Validate(JObject parameters, out string error)
        {
            EmitSignalGameEventActionParameters value = Read(parameters);
            if (string.IsNullOrWhiteSpace(value.Signal))
            {
                error = "signal cannot be empty.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public GameEventActionStatus Begin(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            EmitSignalRuntimeState runtime = ReadRuntime(state);
            if (!runtime.Emitted)
            {
                EmitSignalGameEventActionParameters value = Read(parameters);
                context.Manager.RaiseConfiguredSignal(value.Signal, value.Payload ?? new JObject());
                runtime.Emitted = true;
                state.RuntimeDataJson = JsonConvert.SerializeObject(runtime, Formatting.None);
            }

            return GameEventActionStatus.Completed;
        }

        public void Resume(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            // Completed signals are deliberately not emitted again after loading a save.
        }

        public GameEventActionStatus Tick(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state)
        {
            return Begin(context, parameters, state);
        }

        public void End(
            GameEventActionContext context,
            JObject parameters,
            GameEventActionRuntimeSaveData state,
            bool cancelled)
        {
        }

        private static EmitSignalGameEventActionParameters Read(JObject parameters)
        {
            return parameters?.ToObject<EmitSignalGameEventActionParameters>()
                   ?? new EmitSignalGameEventActionParameters();
        }

        private static EmitSignalRuntimeState ReadRuntime(GameEventActionRuntimeSaveData state)
        {
            if (string.IsNullOrWhiteSpace(state?.RuntimeDataJson))
                return new EmitSignalRuntimeState();

            try
            {
                return JsonConvert.DeserializeObject<EmitSignalRuntimeState>(state.RuntimeDataJson)
                       ?? new EmitSignalRuntimeState();
            }
            catch
            {
                return new EmitSignalRuntimeState();
            }
        }
    }
}
