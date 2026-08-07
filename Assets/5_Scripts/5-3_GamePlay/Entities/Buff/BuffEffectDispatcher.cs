using System;
using System.Collections.Generic;
using UnityEngine;

public static class BuffEffectTypeIds
{
    public const string MoveSpeedMultiplier = "core:move_speed_multiplier";
    public const string FoodConsumeSpeedMultiplier = "core:food_consume_speed_multiplier";
    public const string TemperatureCoolingMultiplier = "core:temperature_cooling_multiplier";
    public const string Heal = "core:heal";
    public const string StaminaChange = "core:stamina_change";
    public const string NutritionChange = "core:nutrition_change";
    public const string TrueDamage = "core:true_damage";
    public const string MaxHealthPercentTrueDamage = "core:max_health_percent_true_damage";
}

/// <summary>
/// 将效果 typeId 映射到 C# 方法。JSON 构建阶段完成字典查询，Tick 阶段只调用缓存委托。
/// </summary>
public static class BuffEffectDispatcher
{
    private static readonly Dictionary<string, BuffEffectHandler> Handlers =
        new(StringComparer.OrdinalIgnoreCase);

    static BuffEffectDispatcher()
    {
        Register(BuffEffectTypeIds.MoveSpeedMultiplier, ApplyMoveSpeedMultiplier);
        Register(BuffEffectTypeIds.FoodConsumeSpeedMultiplier, ApplyFoodConsumeSpeedMultiplier);
        Register(BuffEffectTypeIds.TemperatureCoolingMultiplier, ApplyTemperatureCoolingMultiplier);
        Register(BuffEffectTypeIds.Heal, ApplyHeal);
        Register(BuffEffectTypeIds.StaminaChange, ApplyStaminaChange);
        Register(BuffEffectTypeIds.NutritionChange, ApplyNutritionChange);
        Register(BuffEffectTypeIds.TrueDamage, ApplyTrueDamage);
        Register(BuffEffectTypeIds.MaxHealthPercentTrueDamage, ApplyMaxHealthPercentTrueDamage);
    }

    public static void Register(string typeId, BuffEffectHandler handler)
    {
        if (string.IsNullOrWhiteSpace(typeId))
            throw new ArgumentException("Buff 效果 typeId 不能为空", nameof(typeId));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        string normalized = typeId.Trim().ToLowerInvariant();
        if (Handlers.ContainsKey(normalized))
            throw new InvalidOperationException($"Buff 效果处理器已注册：{normalized}");
        Handlers[normalized] = handler;
    }

    internal static bool TryCacheHandler(BuffEffectDefinition effect)
    {
        if (effect == null || string.IsNullOrWhiteSpace(effect.TypeId))
            return false;

        bool found = Handlers.TryGetValue(effect.TypeId, out BuffEffectHandler handler);
        effect.TryCacheHandler(found ? handler : null);
        return found;
    }

    public static void Execute(IReadOnlyList<BuffEffectDefinition> effects, BuffInstance runtime)
    {
        if (effects == null || runtime == null)
            return;

        for (int i = 0; i < effects.Count; i++)
        {
            BuffEffectDefinition effect = effects[i];
            if (effect == null)
                continue;

            if (!effect.IsHandlerCached)
            {
                Debug.LogError($"[BuffEffectDispatcher] Buff {runtime.DefinitionId} 未缓存效果处理器：{effect.TypeId}");
                continue;
            }

            try
            {
                effect.Execute(runtime);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BuffEffectDispatcher] Buff {runtime.DefinitionId} 执行 {effect.TypeId} 失败：{exception.Message}");
                Debug.LogException(exception);
            }
        }
    }

    public static bool IsSupportedNutritionTarget(string targetId)
    {
        return NormalizeTarget(targetId) is
            "carbohydrates" or "fat" or "protein" or "water" or "vitamins";
    }

    private static Item GetReceiver(BuffInstance runtime)
    {
        Item receiver = runtime?.Receiver;
        return receiver?.itemMods == null ? null : receiver;
    }

    private static void ApplyMoveSpeedMultiplier(BuffEffectDefinition effect, BuffInstance runtime)
    {
        Item receiver = GetReceiver(runtime);
        Mover mover = receiver?.itemMods.GetMod_ByID(ModText.Mover) as Mover;
        if (mover?.Speed != null)
            mover.Speed.MultiplicativeModifier *= effect.Value;
    }

    private static void ApplyFoodConsumeSpeedMultiplier(BuffEffectDefinition effect, BuffInstance runtime)
    {
        Item receiver = GetReceiver(runtime);
        Mod_Food food = receiver?.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
        food?.MultiplyRuntimeNutritionConsumeSpeed(effect.Value);
    }

    private static void ApplyTemperatureCoolingMultiplier(BuffEffectDefinition effect, BuffInstance runtime)
    {
        Item receiver = GetReceiver(runtime);
        Mod_Temperature temperature = receiver?.itemMods.GetMod_ByID(ModText.Temperature) as Mod_Temperature;
        temperature?.MultiplyRuntimeCoolingSpeed(effect.Value);
    }

    private static void ApplyHeal(BuffEffectDefinition effect, BuffInstance runtime)
    {
        Item receiver = GetReceiver(runtime);
        DamageReceiver damageReceiver = receiver?.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
        if (effect.Value > 0f)
            damageReceiver?.Heal(effect.Value);
    }

    private static void ApplyStaminaChange(BuffEffectDefinition effect, BuffInstance runtime)
    {
        Item receiver = GetReceiver(runtime);
        Mod_Stamina stamina = receiver?.itemMods.GetMod_ByID(ModText.Stamina) as Mod_Stamina;
        stamina?.AddStamina(effect.Value);
    }

    private static void ApplyNutritionChange(BuffEffectDefinition effect, BuffInstance runtime)
    {
        Item receiver = GetReceiver(runtime);
        Mod_Food food = receiver?.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
        Nutrition nutrition = food?.Data?.nutrition;
        if (nutrition == null)
            return;

        float before;
        float after;
        switch (NormalizeTarget(effect.TargetId))
        {
            case "carbohydrates":
                before = nutrition.Carbohydrates;
                after = Mathf.Clamp(before + effect.Value, 0f, nutrition.Max_Carbohydrates);
                nutrition.Carbohydrates = after;
                break;
            case "fat":
                before = nutrition.Fat;
                after = Mathf.Clamp(before + effect.Value, 0f, nutrition.Max_Fat);
                nutrition.Fat = after;
                break;
            case "protein":
                before = nutrition.Protein;
                after = Mathf.Clamp(before + effect.Value, 0f, nutrition.Max_Protein);
                nutrition.Protein = after;
                break;
            case "water":
                before = nutrition.Water;
                after = Mathf.Clamp(before + effect.Value, 0f, nutrition.Max_Water);
                nutrition.Water = after;
                break;
            case "vitamins":
                before = nutrition.Vitamins;
                after = Mathf.Clamp(before + effect.Value, 0f, nutrition.Max_Vitamins);
                nutrition.Vitamins = after;
                break;
            default:
                return;
        }

        if (!Mathf.Approximately(before, after))
            food.DataUpdate?.Invoke();
    }

    private static void ApplyTrueDamage(BuffEffectDefinition effect, BuffInstance runtime)
    {
        Item receiver = GetReceiver(runtime);
        DamageReceiver damageReceiver = receiver?.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
        if (effect.Value > 0f)
            damageReceiver?.ForceHurt(effect.Value);
    }

    private static void ApplyMaxHealthPercentTrueDamage(BuffEffectDefinition effect, BuffInstance runtime)
    {
        Item receiver = GetReceiver(runtime);
        if (receiver?.itemData == null)
            return;

        if (!string.IsNullOrWhiteSpace(effect.RequiredTag))
        {
            bool hasItemTag = receiver.itemData.Tags != null &&
                              receiver.itemData.Tags.Contains(effect.RequiredTag);
            bool hasUnityTag = string.Equals(
                receiver.gameObject.tag,
                effect.RequiredTag,
                StringComparison.Ordinal);
            if (!hasItemTag && !hasUnityTag)
                return;
        }

        DamageReceiver damageReceiver = receiver.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
        if (damageReceiver == null || damageReceiver.MaxHp <= 0f || effect.Value <= 0f)
            return;

        damageReceiver.ForceHurt(damageReceiver.MaxHp * Mathf.Clamp01(effect.Value));
    }

    private static string NormalizeTarget(string targetId)
    {
        return (targetId ?? string.Empty).Trim().ToLowerInvariant();
    }
}
