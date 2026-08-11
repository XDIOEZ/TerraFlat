using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 校验 JSON DTO、规范化 ID，并构建已经缓存效果处理器的运行时 Buff 定义。
/// </summary>
public static class BuffDefinitionFactory
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerSettings StrictJsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error
    };

    public static BuffCatalogDto Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Buff JSON 为空");

        return JsonConvert.DeserializeObject<BuffCatalogDto>(json, StrictJsonSettings)
            ?? throw new InvalidDataException("Buff JSON 无法反序列化");
    }

    public static BuffDefinitionDto DeserializeDefinition(JToken token)
    {
        if (token == null)
            throw new InvalidDataException("Buff JSON 定义为空");

        return token.ToObject<BuffDefinitionDto>(JsonSerializer.Create(StrictJsonSettings))
            ?? throw new InvalidDataException("Buff JSON 定义无法反序列化");
    }

    public static List<BuffDefinition> BuildCatalog(BuffCatalogDto catalog)
    {
        if (catalog == null)
            throw new InvalidDataException("Buff JSON 根对象为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的 Buff schemaVersion：{catalog.SchemaVersion}");

        var definitions = new List<BuffDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuffDefinitionDto dto in catalog.Buffs ?? new List<BuffDefinitionDto>())
        {
            BuffDefinition definition = Build(dto);
            if (!ids.Add(definition.Id))
                throw new InvalidDataException($"存在重复 Buff ID：{definition.Id}");
            definitions.Add(definition);
        }

        return definitions;
    }

    public static BuffDefinition Build(BuffDefinitionDto dto)
    {
        if (dto == null)
            throw new InvalidDataException("Buff 定义为空");

        string id = NormalizeRequired(dto.Id, "Buff id");
        if (dto.DurationSeconds.HasValue)
        {
            ValidateFinite(dto.DurationSeconds.Value, $"Buff {id} durationSeconds");
            if (dto.DurationSeconds.Value < 0f)
                throw new InvalidDataException($"Buff {id} durationSeconds 不能小于 0");
        }

        ValidateFinite(dto.TickIntervalSeconds, $"Buff {id} tickIntervalSeconds");
        ValidateFinite(dto.DrinkDurationExtensionSeconds, $"Buff {id} drinkDurationExtensionSeconds");
        if (dto.TickIntervalSeconds < 0f)
            throw new InvalidDataException($"Buff {id} tickIntervalSeconds 不能小于 0");
        if (dto.DrinkDurationExtensionSeconds < 0f)
            throw new InvalidDataException($"Buff {id} drinkDurationExtensionSeconds 不能小于 0");

        var definition = new BuffDefinition
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? id : dto.DisplayName.Trim(),
            Category = ParseCategory(dto.Category, id),
            Description = dto.Description?.Trim() ?? string.Empty,
            DurationSeconds = dto.DurationSeconds,
            TickIntervalSeconds = dto.TickIntervalSeconds,
            StackMode = ParseStackMode(dto.StackMode, id),
            DrinkDurationExtensionSeconds = dto.DrinkDurationExtensionSeconds
        };

        if ((definition.StackMode is BuffStackMode.ExtendDuration or BuffStackMode.RefreshDuration) &&
            (!definition.DurationSeconds.HasValue || definition.DurationSeconds.Value <= 0f))
        {
            throw new InvalidDataException(
                $"Buff {id} 使用 {definition.StackMode} 时 durationSeconds 必须大于 0");
        }

        if (definition.IsPermanent && definition.DrinkDurationExtensionSeconds > 0f)
            throw new InvalidDataException($"Buff {id} 是永久 Buff，不能配置饮水延时");

        var all = new List<BuffEffectDefinition>();
        var start = new List<BuffEffectDefinition>();
        var tick = new List<BuffEffectDefinition>();
        var stop = new List<BuffEffectDefinition>();

        int effectIndex = 0;
        foreach (BuffEffectDto effectDto in dto.Effects ?? new List<BuffEffectDto>())
        {
            BuffEffectDefinition effect = BuildEffect(effectDto, id, effectIndex++);
            all.Add(effect);
            switch (effect.Phase)
            {
                case BuffEffectPhase.Start:
                    start.Add(effect);
                    break;
                case BuffEffectPhase.Tick:
                    tick.Add(effect);
                    break;
                case BuffEffectPhase.Stop:
                    stop.Add(effect);
                    break;
            }
        }

        if (tick.Count > 0 && definition.TickIntervalSeconds <= 0f)
            throw new InvalidDataException($"Buff {id} 包含 tick 效果，但 tickIntervalSeconds 未大于 0");

        definition.SetEffects(all, start, tick, stop);
        return definition;
    }

    private static BuffEffectDefinition BuildEffect(BuffEffectDto dto, string buffId, int index)
    {
        if (dto == null)
            throw new InvalidDataException($"Buff {buffId} 的 effects[{index}] 为空");

        string typeId = NormalizeRequired(dto.TypeId, $"Buff {buffId} effects[{index}].typeId")
            .ToLowerInvariant();
        ValidateFinite(dto.Value, $"Buff {buffId} effects[{index}].value");

        var effect = new BuffEffectDefinition
        {
            TypeId = typeId,
            Phase = ParsePhase(dto.Phase, buffId, index),
            TargetId = dto.TargetId?.Trim(),
            RequiredTag = dto.RequiredTag?.Trim(),
            Value = dto.Value
        };

        ValidateEffectParameters(effect, buffId, index);
        if (!BuffEffectDispatcher.TryCacheHandler(effect))
            throw new InvalidDataException($"Buff {buffId} 使用了未知效果处理器：{typeId}");
        return effect;
    }

    private static void ValidateEffectParameters(BuffEffectDefinition effect, string buffId, int index)
    {
        string context = $"Buff {buffId} effects[{index}]";
        switch (effect.TypeId)
        {
            case BuffEffectTypeIds.MoveSpeedMultiplier:
            case BuffEffectTypeIds.FoodConsumeSpeedMultiplier:
            case BuffEffectTypeIds.WaterConsumeSpeedMultiplier:
            case BuffEffectTypeIds.TemperatureCoolingMultiplier:
                if (effect.Value <= 0f)
                    throw new InvalidDataException($"{context}.value 必须大于 0");
                break;

            case BuffEffectTypeIds.Heal:
            case BuffEffectTypeIds.TrueDamage:
                if (effect.Value < 0f)
                    throw new InvalidDataException($"{context}.value 不能小于 0");
                break;

            case BuffEffectTypeIds.MaxHealthPercentTrueDamage:
                if (effect.Value < 0f || effect.Value > 1f)
                    throw new InvalidDataException($"{context}.value 必须位于 0 到 1 之间");
                break;

            case BuffEffectTypeIds.NutritionChange:
                if (!BuffEffectDispatcher.IsSupportedNutritionTarget(effect.TargetId))
                    throw new InvalidDataException($"{context}.targetId 无效：{effect.TargetId}");
                break;
        }
    }

    private static BuffEffectPhase ParsePhase(string value, string id, int index)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "start" => BuffEffectPhase.Start,
            "tick" => BuffEffectPhase.Tick,
            "stop" => BuffEffectPhase.Stop,
            _ => throw new InvalidDataException($"Buff {id} effects[{index}].phase 无效：{value}")
        };
    }

    private static BuffStackMode ParseStackMode(string value, string id)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ignore" => BuffStackMode.Ignore,
            "extend_duration" => BuffStackMode.ExtendDuration,
            "refresh_duration" => BuffStackMode.RefreshDuration,
            _ => throw new InvalidDataException($"Buff {id} stackMode 无效：{value}")
        };
    }

    private static BuffCategory ParseCategory(string value, string id)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => BuffCategory.General,
            "general" => BuffCategory.General,
            "blood_loss" => BuffCategory.BloodLoss,
            _ => throw new InvalidDataException($"Buff {id} category 无效：{value}")
        };
    }

    private static string NormalizeRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{field} 不能为空");
        return value.Trim();
    }

    private static void ValidateFinite(float value, string field)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidDataException($"{field} 必须是有限数字");
    }
}
