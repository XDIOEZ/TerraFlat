using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>伤害触发 Buff 与夜间前保持清醒，可独立组合到任意动物。</summary>
public sealed class AnimalDamageReactionCapability : AnimalBehaviorTreeCapability
{
    #region 受击反应
    private readonly AnimalBehaviorTreeContext context;
    private readonly string buffId;
    private readonly bool suppressSleepUntilNight;
    private readonly float dayEndRatio;
    private bool sleepSuppressed;
    private double sleepAllowedFromGameDay;

    public AnimalDamageReactionCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        buffId = AnimalLandTargetRules.Text(config, "buffId");
        suppressSleepUntilNight = AnimalLandTargetRules.Flag(config, "suppressSleepUntilNight", false);
        dayEndRatio = AnimalLandTargetRules.Number(config, "dayEndRatio", 0.75f);
        sleepSuppressed = saved?["sleepSuppressed"]?.Value<bool>() ?? false;
        sleepAllowedFromGameDay = saved?["sleepAllowedFromGameDay"]?.Value<double>() ?? -1d;
        if (context.Health != null) context.Health.OnDamageReceived += HandleDamage;
    }

    public bool IsSleepSuppressed
    {
        get
        {
            if (!sleepSuppressed) return false;
            if (!TryGetCurrentGameDay(out double day)) return true;
            if (sleepAllowedFromGameDay < 0d) sleepAllowedFromGameDay = NextNight(day);
            if (day < sleepAllowedFromGameDay) return true;
            sleepSuppressed = false;
            sleepAllowedFromGameDay = -1d;
            return false;
        }
    }

    private void HandleDamage(DamageReceiverDamageInfo info)
    {
        if (info == null || info.DamageValue <= 0f || info.Attacker == context.Actor) return;
        if (!string.IsNullOrWhiteSpace(buffId))
            context.Actor.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager)?.AddBuff(buffId);
        if (!suppressSleepUntilNight) return;
        sleepSuppressed = true;
        sleepAllowedFromGameDay = TryGetCurrentGameDay(out double day) ? NextNight(day) : -1d;
    }

    private double NextNight(double day)
    {
        double next = Math.Floor(day) + Mathf.Clamp01(dayEndRatio);
        return next > day ? next : next + 1d;
    }

    private bool TryGetCurrentGameDay(out double day)
    {
        day = 0d;
        DayTimeSystem time = DayTimeSystem.Instance;
        if (time == null || !time.WorldTimeDict.TryGetValue(context.Actor.gameObject.scene.name, out TimeData data) || data == null) return false;
        float dayLength = Mathf.Max(1f, data.DayLength);
        day = Math.Max(0, data.TotalDays) + (double)Mathf.Repeat(data.CurrentTime, dayLength) / dayLength;
        return true;
    }

    public override JObject CaptureSaveData() => new()
    {
        ["sleepSuppressed"] = sleepSuppressed, ["sleepAllowedFromGameDay"] = sleepAllowedFromGameDay
    };
    public override void Dispose()
    {
        if (context.Health != null) context.Health.OnDamageReceived -= HandleDamage;
    }
    #endregion
}

/// <summary>按配置维持草食营养，并复用地面觅食器。</summary>
public sealed class AnimalForagingCapability : AnimalBehaviorTreeCapability
{
    #region 草食维持与目标
    private readonly AnimalBehaviorTreeContext context;
    private readonly AIAnimalForaging foraging;
    private readonly System.Collections.Generic.List<string> edibleTags;
    private readonly bool grassEnabled;
    private readonly bool pauseNutrition;
    private readonly bool grassIndependentOfHunger;
    private readonly float enterHunger;
    private readonly float exitHunger;
    private readonly float eatDistance;
    private readonly float grassRadius;
    private readonly float grassDuration;
    private readonly float sustenanceDays;
    private readonly float searchDelay;
    private readonly float unavailableCooldown;
    private readonly float itemEatInterval;
    private readonly bool sustenanceUsesWorldTime;
    private DayTimeSystem worldTime;
    private float unavailableRemaining;
    private float sustenanceRemaining;
    private float itemEatElapsed;
    private Item previousFoodTarget;

    public bool GrassMealDue => grassEnabled && sustenanceRemaining <= 0f;

    public AnimalForagingCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        edibleTags = new System.Collections.Generic.List<string>(AnimalLandTargetRules.Strings(config, "foodTags"));
        grassEnabled = AnimalLandTargetRules.Flag(config, "grassEnabled", true);
        pauseNutrition = AnimalLandTargetRules.Flag(config, "pauseNutrition", true);
        grassIndependentOfHunger = AnimalLandTargetRules.Flag(config, "grassIndependentOfHunger", false);
        enterHunger = AnimalLandTargetRules.Number(config, "enterHunger", 0.35f);
        exitHunger = AnimalLandTargetRules.Number(config, "exitHunger", 0.85f);
        eatDistance = AnimalLandTargetRules.Number(config, "eatDistance", 1.2f);
        grassRadius = AnimalLandTargetRules.Number(config, "grassRadius", 8f);
        grassDuration = AnimalLandTargetRules.Number(config, "grassDuration", 1f);
        sustenanceDays = AnimalLandTargetRules.Number(config, "sustenanceDays", 2f);
        searchDelay = AnimalLandTargetRules.Number(config, "searchDelay", 0.8f);
        unavailableCooldown = AnimalLandTargetRules.Number(config, "unavailableCooldown", 0f);
        itemEatInterval = AnimalLandTargetRules.Number(config, "itemEatInterval", 0f);
        sustenanceUsesWorldTime = AnimalLandTargetRules.Flag(config, "sustenanceUsesWorldTime", false);
        bool startsFed = AnimalLandTargetRules.Flag(config, "startsWithSustenance", true);
        sustenanceRemaining = saved?["sustenanceRemaining"]?.Value<float>() ?? (startsFed ? GetDayLength() * sustenanceDays : 0f);
        unavailableRemaining = saved?["unavailableRemaining"]?.Value<float>() ?? 0f;
        foraging = new AIAnimalForaging(context.Actor.transform, context.Detector, context.Food,
            () => context.Detector?.RequestDetectorUpdate(),
            (position, distance) => context.Mod_Mover.SetInteractionDestination(position, distance),
            target => WorldTopologyRuntime.Distance(context.Position, target.position),
            () => context.Mod_Mover.StopMovement(), ConsumeGrass);
        ApplyNutrition();
    }

    public override void Tick(float deltaTime)
    {
        foraging.TickSearchCooldown(deltaTime);
        unavailableRemaining = Mathf.Max(0f, unavailableRemaining - Mathf.Max(0f, deltaTime));
        if (sustenanceUsesWorldTime) EnsureWorldTimeBinding();
        else if (grassEnabled) sustenanceRemaining = Mathf.Max(0f, sustenanceRemaining - Mathf.Max(0f, deltaTime));
        ApplyNutrition();
    }

    public bool TryChooseMeal(bool alreadyEating, out bool withinEatDistance)
    {
        withinEatDistance = false;
        if (unavailableRemaining > 0f) return false;
        float nutrition = context.GetNutritionRate();
        bool hungryForSearch = nutrition <= enterHunger;
        bool hungryForEating = alreadyEating ? nutrition < exitHunger : hungryForSearch;
        bool grassDue = GrassMealDue && (hungryForEating || grassIndependentOfHunger);
        if (grassDue && foraging.TryAcquireGrassTarget(true, eatDistance, grassRadius, searchDelay))
        {
            withinEatDistance = foraging.GrassTargetDistance <= eatDistance;
            return grassIndependentOfHunger || (withinEatDistance ? hungryForEating : hungryForSearch);
        }
        if (!hungryForEating || !foraging.TryFindFoodTarget(edibleTags, true))
        {
            if (hungryForEating || grassDue) unavailableRemaining = unavailableCooldown;
            return false;
        }
        withinEatDistance = foraging.FoodTargetDistance <= eatDistance;
        return withinEatDistance ? hungryForEating : hungryForSearch;
    }

    public void TickMeal(bool eating, float elapsed, float deltaTime)
    {
        Item foodTarget = foraging.FoodTarget;
        if (foodTarget != previousFoodTarget || !eating) { itemEatElapsed = 0f; previousFoodTarget = foodTarget; }
        if (!eating) { foraging.TickForage(GrassMealDue, edibleTags, eatDistance, grassRadius, searchDelay); return; }
        if (foodTarget != null && itemEatInterval > 0f)
        {
            itemEatElapsed += Mathf.Max(0f, deltaTime);
            context.Mod_Mover.SetInteractionDestination(foodTarget.transform.position, eatDistance);
            if (itemEatElapsed < itemEatInterval) return;
            itemEatElapsed = 0f;
        }
        foraging.TickEat(GrassMealDue, edibleTags, eatDistance, grassRadius, searchDelay, grassDuration, elapsed);
    }

    public void ClearTargets() { foraging.ClearTargets(); previousFoodTarget = null; itemEatElapsed = 0f; }
    private void ConsumeGrass(Vector2Int _)
    {
        context.Food.RestoreNutritionToMaximum();
        sustenanceRemaining = GetDayLength() * Mathf.Max(0.1f, sustenanceDays);
        ApplyNutrition();
    }
    private void ApplyNutrition()
    {
        if (context.Food != null && pauseNutrition)
            context.Food.RuntimeNutritionConsumeMultiplier = grassEnabled && sustenanceRemaining > 0f ? 0f : 1f;
    }
    private float GetDayLength()
    {
        DayTimeSystem time = DayTimeSystem.Instance;
        return time != null && time.WorldTimeDict.TryGetValue(context.Actor.gameObject.scene.name, out TimeData data) && data != null
            ? Mathf.Max(1f, data.DayLength) : 1440f;
    }
    private void EnsureWorldTimeBinding()
    {
        DayTimeSystem current = DayTimeSystem.Instance;
        if (current == worldTime) return;
        if (worldTime != null) worldTime.TimeAdvanced -= HandleWorldTimeAdvanced;
        worldTime = current;
        if (worldTime != null) worldTime.TimeAdvanced += HandleWorldTimeAdvanced;
    }
    private void HandleWorldTimeAdvanced(string scene, float previous, float next)
    {
        if (grassEnabled && scene == context.Actor.gameObject.scene.name)
            sustenanceRemaining = Mathf.Max(0f, sustenanceRemaining - Mathf.Max(0f, next - previous));
        ApplyNutrition();
    }
    public override JObject CaptureSaveData() => new()
    { ["sustenanceRemaining"] = sustenanceRemaining, ["unavailableRemaining"] = unavailableRemaining };
    public override void Dispose()
    {
        ClearTargets();
        if (worldTime != null) worldTime.TimeAdvanced -= HandleWorldTimeAdvanced;
        worldTime = null;
        if (context.Food != null && pauseNutrition) context.Food.RuntimeNutritionConsumeMultiplier = 1f;
    }
    #endregion
}

/// <summary>按权威世界时间累计产物进度，行为节点只负责播放动作和实际产出。</summary>
public sealed class AnimalTimedProductionCapability : AnimalBehaviorTreeCapability
{
    #region 世界时间与产物
    private readonly AnimalBehaviorTreeContext context;
    private readonly bool enabled;
    private readonly string itemId;
    private readonly float intervalDays;
    private DayTimeSystem timeSource;
    private string sceneName;
    private float elapsed;

    public AnimalTimedProductionCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        enabled = AnimalLandTargetRules.Flag(config, "enabled", true);
        itemId = AnimalLandTargetRules.Text(config, "itemId", "Egg");
        intervalDays = AnimalLandTargetRules.Number(config, "intervalDays", 2f);
        elapsed = saved?["elapsed"]?.Value<float>() ?? 0f;
        EnsureTimeBinding();
    }

    public bool Ready => enabled && !string.IsNullOrWhiteSpace(itemId) && elapsed >= GetDayLength() * Mathf.Max(.1f, intervalDays);
    public override void Tick(float deltaTime) => EnsureTimeBinding();
    public void Produce()
    {
        if (!Ready) return;
        DroppedItemService.SpawnLoot(itemId, context.Position);
        elapsed = Mathf.Max(0f, elapsed - GetDayLength() * Mathf.Max(.1f, intervalDays));
    }

    private void EnsureTimeBinding()
    {
        DayTimeSystem current = DayTimeSystem.Instance;
        string currentScene = context.Actor.gameObject.scene.name;
        if (timeSource == current && sceneName == currentScene) return;
        if (timeSource != null) timeSource.TimeAdvanced -= HandleTimeAdvanced;
        timeSource = current;
        sceneName = currentScene;
        if (timeSource != null) timeSource.TimeAdvanced += HandleTimeAdvanced;
    }
    private void HandleTimeAdvanced(string scene, float previous, float next)
    {
        if (scene != sceneName) return;
        if (enabled && !string.IsNullOrWhiteSpace(itemId)) elapsed += Mathf.Max(0f, next - previous);
        else elapsed = 0f;
    }
    private float GetDayLength() => timeSource != null && timeSource.WorldTimeDict.TryGetValue(sceneName, out TimeData data) && data != null
        ? Mathf.Max(1f, data.DayLength) : 1440f;
    public override JObject CaptureSaveData() => new() { ["elapsed"] = elapsed };
    public override void Dispose()
    {
        if (timeSource != null) timeSource.TimeAdvanced -= HandleTimeAdvanced;
        timeSource = null;
    }
    #endregion
}

/// <summary>处理休息进入与退出阈值，以及受击唤醒和冷却。</summary>
public sealed class AnimalSleepCapability : AnimalBehaviorTreeCapability
{
    #region 睡眠条件与表现
    private readonly AnimalBehaviorTreeContext context;
    private readonly AnimalDamageReactionCapability reaction;
    private readonly ActorSleepVisualEffectController visual;
    private readonly float enterHp;
    private readonly float exitHp;
    private readonly float duration;
    private readonly float cooldown;
    private readonly float dayStart;
    private readonly float dayEnd;
    private readonly bool sustainAtNightOrLowHp;
    private float remainingCooldown;
    private bool sleeping;
    private bool interrupted;
    private float elapsed;

    public AnimalSleepCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        enterHp = AnimalLandTargetRules.Number(config, "enterHp", .5f);
        exitHp = AnimalLandTargetRules.Number(config, "exitHp", .55f);
        duration = AnimalLandTargetRules.Number(config, "duration", 6f);
        cooldown = AnimalLandTargetRules.Number(config, "cooldown", 3f);
        dayStart = AnimalLandTargetRules.Number(config, "dayStart", .25f);
        dayEnd = AnimalLandTargetRules.Number(config, "dayEnd", .75f);
        sustainAtNightOrLowHp = AnimalLandTargetRules.Flag(config, "sustainAtNightOrLowHp", true);
        remainingCooldown = saved?["remainingCooldown"]?.Value<float>() ?? 0f;
        string reactionKey = AnimalLandTargetRules.Text(config, "reactionKey");
        if (!string.IsNullOrWhiteSpace(reactionKey))
            reaction = context.GetOrCreateCapability(reactionKey, data =>
                new AnimalDamageReactionCapability(context, context.GetCapabilityConfiguration(reactionKey), data));
        visual = context.Actor.GetComponentInChildren<ActorSleepVisualEffectController>(true);
        if (context.Health != null) context.Health.OnDamageReceived += HandleDamage;
    }

    public bool ShouldSleep
    {
        get
        {
            if (reaction?.IsSleepSuppressed == true) return false;
            float hp = context.Health != null && context.Health.MaxHp > 0f ? context.Health.Hp / context.Health.MaxHp : 0f;
            bool night = context.IsNightTime(dayStart, dayEnd);
            if (sleeping)
                return !interrupted && (elapsed < duration || sustainAtNightOrLowHp && (night || hp < exitHp));
            return remainingCooldown <= 0f && (night || hp < enterHp);
        }
    }
    public override void Tick(float deltaTime)
    {
        remainingCooldown = Mathf.Max(0f, remainingCooldown - deltaTime);
        if (sleeping) elapsed += Mathf.Max(0f, deltaTime);
    }
    public void EnterSleep()
    {
        sleeping = true;
        interrupted = false;
        elapsed = 0f;
        visual?.SetSleeping(context.Actor.transform, true);
    }
    public void ExitSleep()
    {
        if (sleeping) remainingCooldown = cooldown;
        sleeping = interrupted = false;
        visual?.SetSleeping(context.Actor.transform, false);
    }
    private void HandleDamage(DamageReceiverDamageInfo info)
    {
        if (sleeping && info != null && info.DamageValue > 0f && info.Attacker != context.Actor) interrupted = true;
    }
    public override JObject CaptureSaveData() => new() { ["remainingCooldown"] = remainingCooldown };
    public override void Dispose()
    {
        if (context.Health != null) context.Health.OnDamageReceived -= HandleDamage;
        ExitSleep();
    }
    #endregion
}

/// <summary>外部请求的短时繁殖动作。</summary>
public sealed class AnimalMateRequestCapability : AnimalBehaviorTreeCapability
{
    #region 请求计时
    private readonly float defaultDuration;
    private float remaining;
    public bool Requested => remaining > 0f;
    public AnimalMateRequestCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
        => defaultDuration = AnimalLandTargetRules.Number(config, "duration", 8f);
    public void Request(float duration = -1f) => remaining = duration > 0f ? duration : defaultDuration;
    public override void Tick(float deltaTime) => remaining = Mathf.Max(0f, remaining - deltaTime);
    #endregion
}
