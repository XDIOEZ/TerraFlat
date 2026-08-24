using Action = System.Action;
using System.Collections.Generic;
using FlatWorld.Audio;
using UnityEngine;

/// <summary>环境动作定义，由环境来源提供规则，每次开始时为角色创建独立运行实例。</summary>
public interface IEnvironmentActionDefinition
{
    string ActionId { get; }
    string DisplayNameKey { get; }
    int Priority { get; }
    IEnvironmentActionInstance CreateInstance(Item actor);
}

/// <summary>单个角色的一次环境动作实例，负责开始、持续更新与取消。</summary>
public interface IEnvironmentActionInstance
{
    string ActionId { get; }
    bool IsHeld { get; }
    bool IsExecuting { get; }
    bool Begin();
    void Tick(float deltaTime);
    void Cancel();
}

/// <summary>环境被动效果定义；进入环境时为角色创建实例，离开时由同一实例精确撤销。</summary>
public interface IEnvironmentEffectDefinition
{
    string EffectId { get; }
    IEnvironmentEffectInstance CreateInstance(Item actor);
}

/// <summary>单个角色持有的环境被动效果实例，不进入 Buff 列表，也不参与 Buff 清理。</summary>
public interface IEnvironmentEffectInstance
{
    string EffectId { get; }
    bool IsApplied { get; }
    bool Apply();
    void Remove();
}

/// <summary>
/// 角色侧环境动作运行器。只负责接收定义、选择动作并运行独立实例，
/// 不包含喝水、采集等具体规则，后续新增动作时无需扩充本类。
/// </summary>
public sealed class EnvironmentInteractionRunner : MonoBehaviour
{
    #region 运行时状态

    private readonly List<IEnvironmentActionDefinition> definitions = new(4);
    private readonly List<IEnvironmentEffectDefinition> effectDefinitions = new(4);
    private readonly List<IEnvironmentEffectInstance> activeEffects = new(4);
    private Item actor;
    private IEnvironmentActionInstance activeAction;
    private IEnvironmentActionInstance lastAction;

    public int AvailableActionCount => definitions.Count;
    public IEnvironmentActionInstance ActiveAction => activeAction;
    public IEnvironmentActionInstance LastAction => lastAction;
    public int ActiveEffectCount => activeEffects.Count;
    public event Action AvailableActionsChanged;

    #endregion

    #region 环境被动效果

    /// <summary>替换当前环境效果；每个定义为当前角色创建独立实例并立即应用。</summary>
    public void SetAvailableEffects(params IEnvironmentEffectDefinition[] availableEffects)
    {
        ClearAvailableEffects();
        if (availableEffects == null || actor == null)
            return;

        for (int i = 0; i < availableEffects.Length; i++)
        {
            IEnvironmentEffectDefinition definition = availableEffects[i];
            if (definition == null)
                continue;

            IEnvironmentEffectInstance instance = definition.CreateInstance(actor);
            if (instance == null || !instance.Apply())
            {
                instance?.Remove();
                continue;
            }

            effectDefinitions.Add(definition);
            activeEffects.Add(instance);
        }
    }

    /// <summary>离开环境时使用原实例撤销效果，避免按当前数值重新猜测来源。</summary>
    public void ClearAvailableEffects()
    {
        for (int i = activeEffects.Count - 1; i >= 0; i--)
            activeEffects[i]?.Remove();

        activeEffects.Clear();
        effectDefinitions.Clear();
    }

    /// <summary>更新当前环境移速效果的倍率；没有该效果时返回 false。</summary>
    public bool TryUpdateMoveSpeedMultiplier(float multiplier)
    {
        for (int i = 0; i < activeEffects.Count; i++)
        {
            if (activeEffects[i] is MoveSpeedEnvironmentEffectInstance moveSpeedEffect)
                return moveSpeedEffect.UpdateMultiplier(multiplier);
        }

        return false;
    }

    public bool TryGetEffectDefinition<TDefinition>(out TDefinition definition)
        where TDefinition : class, IEnvironmentEffectDefinition
    {
        for (int i = 0; i < effectDefinitions.Count; i++)
        {
            if (effectDefinitions[i] is TDefinition typed)
            {
                definition = typed;
                return true;
            }
        }

        definition = null;
        return false;
    }

    #endregion

    #region 环境定义

    public void Bind(Item actionActor) => actor = actionActor;

    public void SetAvailableActions(params IEnvironmentActionDefinition[] availableDefinitions)
    {
        CancelActiveAction();
        definitions.Clear();
        if (availableDefinitions == null)
        {
            AvailableActionsChanged?.Invoke();
            return;
        }

        for (int i = 0; i < availableDefinitions.Length; i++)
        {
            if (availableDefinitions[i] != null)
                definitions.Add(availableDefinitions[i]);
        }

        AvailableActionsChanged?.Invoke();
    }

    public void ClearAvailableActions()
    {
        CancelActiveAction();
        definitions.Clear();
        AvailableActionsChanged?.Invoke();
    }

    public bool TryGetDefinition<TDefinition>(out TDefinition definition)
        where TDefinition : class, IEnvironmentActionDefinition
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] is TDefinition typed)
            {
                definition = typed;
                return true;
            }
        }

        definition = null;
        return false;
    }

    /// <summary>返回当前优先级最高环境动作的本地化名称键，供手机交互按钮显示。</summary>
    public string GetPreferredActionDisplayNameKey()
    {
        IEnvironmentActionDefinition selected = null;
        for (int i = 0; i < definitions.Count; i++)
        {
            IEnvironmentActionDefinition candidate = definitions[i];
            if (candidate != null && (selected == null || candidate.Priority > selected.Priority))
                selected = candidate;
        }

        return selected?.DisplayNameKey;
    }

    #endregion

    #region 动作运行

    public bool BeginPreferredAction()
    {
        CancelActiveAction();
        IEnvironmentActionDefinition selected = null;
        for (int i = 0; i < definitions.Count; i++)
        {
            IEnvironmentActionDefinition candidate = definitions[i];
            if (selected == null || candidate.Priority > selected.Priority)
                selected = candidate;
        }

        if (actor == null || selected == null)
            return false;

        IEnvironmentActionInstance instance = selected.CreateInstance(actor);
        if (instance == null || !instance.Begin())
        {
            instance?.Cancel();
            return false;
        }

        activeAction = instance;
        lastAction = instance;
        return true;
    }

    public void TickActiveAction(float deltaTime) =>
        activeAction?.Tick(Mathf.Max(0f, deltaTime));

    public void CancelActiveAction()
    {
        if (activeAction == null)
            return;

        activeAction.Cancel();
        lastAction = activeAction;
        activeAction = null;
    }

    private void OnDisable()
    {
        CancelActiveAction();
        ClearAvailableEffects();
    }

    #endregion
}

/// <summary>环境移动速度倍率定义；用于水体、泥地等只在环境内生效的被动影响。</summary>
public sealed class MoveSpeedEnvironmentEffectDefinition : IEnvironmentEffectDefinition
{
    public const string StableEffectId = "environment.move_speed_multiplier";

    public MoveSpeedEnvironmentEffectDefinition(float multiplier)
    {
        Multiplier = Mathf.Max(0.01f, multiplier);
    }

    public string EffectId => StableEffectId;
    public float Multiplier { get; }
    public IEnvironmentEffectInstance CreateInstance(Item actor) =>
        new MoveSpeedEnvironmentEffectInstance(actor, this);
}

/// <summary>角色独享的环境移速实例；应用时乘入，离开时仅撤销自己贡献的倍率。</summary>
public sealed class MoveSpeedEnvironmentEffectInstance : IEnvironmentEffectInstance
{
    private readonly Mover mover;
    private float multiplier;

    public MoveSpeedEnvironmentEffectInstance(Item actor,
        MoveSpeedEnvironmentEffectDefinition definition)
    {
        mover = actor?.itemMods?.GetMod_ByID<Mover>(ModText.Mover);
        multiplier = definition?.Multiplier ?? 1f;
    }

    public string EffectId => MoveSpeedEnvironmentEffectDefinition.StableEffectId;
    public bool IsApplied { get; private set; }

    public bool Apply()
    {
        if (IsApplied || mover?.Speed == null || multiplier <= 0f)
            return false;

        mover.Speed.MultiplicativeModifier *= multiplier;
        IsApplied = true;
        return true;
    }

    /// <summary>替换自身贡献的倍率，并保持其他移动速度来源不变。</summary>
    public bool UpdateMultiplier(float nextMultiplier)
    {
        nextMultiplier = Mathf.Max(0.01f, nextMultiplier);
        if (!IsApplied)
        {
            multiplier = nextMultiplier;
            return Apply();
        }

        if (Mathf.Approximately(multiplier, nextMultiplier))
            return true;

        if (mover?.Speed == null)
            return false;

        mover.Speed.MultiplicativeModifier /= multiplier;
        multiplier = nextMultiplier;
        mover.Speed.MultiplicativeModifier *= multiplier;
        return true;
    }

    public void Remove()
    {
        if (!IsApplied)
            return;

        if (mover?.Speed != null && multiplier > 0f)
            mover.Speed.MultiplicativeModifier /= multiplier;
        IsApplied = false;
    }
}

/// <summary>移动表面效果定义，统一改变玩家和动物的加减速度。</summary>
public sealed class MovementSurfaceResponseEnvironmentEffectDefinition : IEnvironmentEffectDefinition
{
    public const string StableEffectId = "environment.movement_surface_response";

    public MovementSurfaceResponseEnvironmentEffectDefinition(
        float accelerationMultiplier,
        float decelerationMultiplier)
    {
        AccelerationMultiplier = Mathf.Max(0.01f, accelerationMultiplier);
        DecelerationMultiplier = Mathf.Max(0.01f, decelerationMultiplier);
    }

    public string EffectId => StableEffectId;
    public float AccelerationMultiplier { get; }
    public float DecelerationMultiplier { get; }

    public IEnvironmentEffectInstance CreateInstance(Item actor) =>
        new MovementSurfaceResponseEnvironmentEffectInstance(actor, this);
}

/// <summary>移动表面效果实例，退出表面时恢复角色原本的移动响应。</summary>
public sealed class MovementSurfaceResponseEnvironmentEffectInstance : IEnvironmentEffectInstance
{
    private readonly Item actor;
    private readonly MovementSurfaceResponseEnvironmentEffectDefinition definition;
    private Mover mover;
    private float previousAccelerationMultiplier;
    private float previousDecelerationMultiplier;

    public MovementSurfaceResponseEnvironmentEffectInstance(
        Item actor,
        MovementSurfaceResponseEnvironmentEffectDefinition definition)
    {
        this.actor = actor;
        this.definition = definition;
    }

    public string EffectId => MovementSurfaceResponseEnvironmentEffectDefinition.StableEffectId;
    public bool IsApplied { get; private set; }

    public bool Apply()
    {
        if (IsApplied || actor == null || definition == null)
            return false;

        mover = actor.itemMods?.GetMod_ByID<Mover>(ModText.Mover);
        if (mover == null)
            return false;

        previousAccelerationMultiplier = mover.surfaceAccelerationMultiplier;
        previousDecelerationMultiplier = mover.surfaceDecelerationMultiplier;
        mover.SetSurfaceMovementResponse(
            definition.AccelerationMultiplier,
            definition.DecelerationMultiplier);
        IsApplied = true;
        return true;
    }

    public void Remove()
    {
        if (!IsApplied || mover == null)
            return;

        mover.SetSurfaceMovementResponse(
            previousAccelerationMultiplier,
            previousDecelerationMultiplier);
        IsApplied = false;
    }
}

/// <summary>水体来源类型，是当前环境事实，不属于可驱散或持久化的 Buff。</summary>
public enum WaterEnvironmentKind
{
    CleanFresh,
    DirtyFresh,
    Salt
}

/// <summary>水体提供的喝水动作定义；保存规则，不保存任何玩家运行状态。</summary>
public sealed class DrinkWaterActionDefinition : IEnvironmentActionDefinition
{
    public const string StableActionId = "environment.drink_water";

    public DrinkWaterActionDefinition(WaterEnvironmentKind waterKind, float holdSeconds,
        float tickSeconds, float waterGainPerTick, float dirtyInfectionChance)
    {
        WaterKind = waterKind;
        HoldSeconds = Mathf.Max(0f, holdSeconds);
        TickSeconds = Mathf.Max(0.05f, tickSeconds);
        WaterGainPerTick = Mathf.Max(0f, waterGainPerTick);
        DirtyInfectionChance = Mathf.Clamp01(dirtyInfectionChance);
    }

    public string ActionId => StableActionId;
    public string DisplayNameKey => "喝水";
    public int Priority => 100;
    public WaterEnvironmentKind WaterKind { get; }
    public float HoldSeconds { get; }
    public float TickSeconds { get; }
    public float WaterGainPerTick { get; }
    public float DirtyInfectionChance { get; }
    public IEnvironmentActionInstance CreateInstance(Item actor) =>
        new DrinkWaterActionInstance(actor, this);
}

/// <summary>单个角色的一次喝水实例，维护长按、Tick、补水、感染与反馈。</summary>
public sealed class DrinkWaterActionInstance : IEnvironmentActionInstance
{
    private const string DrinkEffectName = "Particle_BeEat";
    private readonly Item actor;
    private readonly DrinkWaterActionDefinition definition;
    private readonly BuffManager buffManager;
    private readonly Mod_Food food;
    private float holdElapsed;
    private float tickElapsed;

    public DrinkWaterActionInstance(Item actor, DrinkWaterActionDefinition definition)
    {
        this.actor = actor;
        this.definition = definition;
        buffManager = actor?.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
        food = actor?.itemMods?.GetMod_ByID(ModText.Food) as Mod_Food;
    }

    public string ActionId => DrinkWaterActionDefinition.StableActionId;
    public bool IsHeld { get; private set; }
    public bool IsExecuting { get; private set; }
    public DrinkWaterActionDefinition Definition => definition;
    public AudioHandle LastAudioHandle { get; private set; }
    public GameObject LastEffect { get; private set; }

    public bool Begin()
    {
        if (actor == null || definition == null || food?.Data?.nutrition == null)
            return false;

        IsHeld = true;
        IsExecuting = false;
        holdElapsed = 0f;
        tickElapsed = 0f;
        return true;
    }

    public void Tick(float deltaTime)
    {
        if (!IsHeld)
            return;

        float safeDelta = Mathf.Max(0f, deltaTime);
        if (!IsExecuting)
        {
            holdElapsed += safeDelta;
            if (holdElapsed < definition.HoldSeconds)
                return;

            IsExecuting = true;
            tickElapsed = 0f;
            ProcessPulse(Random.value);
            return;
        }

        tickElapsed += safeDelta;
        while (tickElapsed >= definition.TickSeconds && IsHeld)
        {
            tickElapsed -= definition.TickSeconds;
            ProcessPulse(Random.value);
        }
    }

    public void Cancel()
    {
        IsHeld = false;
        IsExecuting = false;
        holdElapsed = 0f;
        tickElapsed = 0f;
    }

    public bool ProcessPulse(float infectionRoll, bool playFeedback = true)
    {
        if (food?.Data?.nutrition == null || definition == null)
            return false;

        Nutrition nutrition = food.Data.nutrition;
        nutrition.Water = Mathf.Clamp(
            nutrition.Water + definition.WaterGainPerTick, 0f, nutrition.Max_Water);
        food.NotifyStateChanged();

        if (definition.WaterKind == WaterEnvironmentKind.DirtyFresh &&
            Mathf.Clamp01(infectionRoll) < definition.DirtyInfectionChance)
            buffManager?.AddBuff(InfectionBuffIds.Infection);

        if (playFeedback)
            PlayFeedback();
        return true;
    }

    #region 表现反馈

    private void PlayFeedback()
    {
        LastAudioHandle = AudioService.Instance.Play(
            AudioEventIds.FoodDrink,
            AudioPlayOptions.Attached(actor.transform, 0.75f, 1f));

        VisualEffectManager manager = VisualEffectManager.Instance;
        if (manager == null)
            return;

        // 饮水特效属于可选反馈；资源目录尚未加载或测试环境未注册时静默跳过，
        // 不能让缺少表现资源把已经成功的饮水动作升级为玩法错误。
        GameRes gameRes = GameRes.ExistingInstance;
        if (gameRes == null || gameRes.GetPrefab(DrinkEffectName, false) == null)
            return;

        LastEffect = manager.PlayEffect(actor.transform, DrinkEffectName, actor.transform,
            new Vector3(0f, 0.15f, 0f), 0.8f, EffectStackMode.Stackable);
        ConfigureBlueWaterParticles(LastEffect);
    }

    private static void ConfigureBlueWaterParticles(GameObject effect)
    {
        if (effect == null)
            return;

        ParticleSystem[] systems = effect.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.2f, 0.7f, 1f, 0.9f),
                new Color(0.05f, 0.3f, 0.95f, 0.95f));
            system.Play(true);
        }
    }

    #endregion
}

/// <summary>玩家交互输入桥，只转发环境动作的按下、持续与松开。</summary>
public partial class Mod_InteractSender
{
    #region 环境动作转发

    private EnvironmentInteractionRunner environmentInteractionRunner;

    public bool BeginEnvironmentActionHold()
    {
        ResolveEnvironmentInteractionRunner();
        return environmentInteractionRunner != null &&
               environmentInteractionRunner.BeginPreferredAction();
    }

    public void EndEnvironmentActionHold() =>
        environmentInteractionRunner?.CancelActiveAction();

    public void TickEnvironmentInteraction(float deltaTime)
    {
        ResolveEnvironmentInteractionRunner();
        environmentInteractionRunner?.TickActiveAction(deltaTime);
    }

    private void ResolveEnvironmentInteractionRunner()
    {
        if (environmentInteractionRunner != null || item == null)
            return;

        TileEffectReceiver receiver =
            item.itemMods?.GetMod_ByID<TileEffectReceiver>(ModText.TileEffectReceiver) ??
            item.GetComponentInChildren<TileEffectReceiver>(true);
        environmentInteractionRunner = receiver?.EnvironmentInteractions;
    }

    #endregion
}
