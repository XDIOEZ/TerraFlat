using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameplayFixedStepSimulationPhase
{
    WorldTime,
    WorldSimulation,
    Navigation,
    Gameplay,
    PhysicsPreparation,
    SpaceSimulation,
    Physics,
    AfterPhysics
}

/// <summary>逻辑系统的目标频率；物理和游戏时钟跟随玩家选择的基础 TPS。</summary>
public enum GameplayFixedStepTickRate
{
    Hz60 = 60,
    Hz30 = 30,
    Hz20 = 20,
    Hz10 = 10,
    Hz5 = 5
}

public interface IGameplayFixedStepSimulationSystem
{
    void SimulateFixedStep(float deltaTime);
}

/// <summary>跨世界、暂停或加载期禁止积累低频 Tick 的旧会话时间。</summary>
public interface IGameplayFixedStepSimulationReadiness
{
    bool CanAccumulateFixedStepTime { get; }
}

/// <summary>只编排一次固定步的系统顺序，补步与倍速统一交给 Unity。</summary>
public sealed class GameplayFixedStepSimulationScheduler
{
    #region 固定步系统登记与执行

    private sealed class Registration
    {
        public IGameplayFixedStepSimulationSystem System;
        public LinkedListNode<Registration> Node;
        public GameplayFixedStepTickRate Rate;
        public double AccumulatedSeconds;
        public double PhaseSeconds;
        public bool Active = true;
    }

    private readonly Dictionary<IGameplayFixedStepSimulationSystem, Registration> systems = new();
    private readonly LinkedList<Registration>[] phases = new LinkedList<Registration>[(int)GameplayFixedStepSimulationPhase.AfterPhysics + 1];
    private readonly List<Registration> dispatch = new();
    public ulong TickIndex { get; private set; }
    public int RegisteredSystemCount => systems.Count;

    public void Register(IGameplayFixedStepSimulationSystem system, GameplayFixedStepSimulationPhase phase,
        GameplayFixedStepTickRate rate = GameplayFixedStepTickRate.Hz60)
    {
        if (system == null) throw new ArgumentNullException(nameof(system));
        if (rate is not (GameplayFixedStepTickRate.Hz60 or GameplayFixedStepTickRate.Hz30 or
            GameplayFixedStepTickRate.Hz20 or GameplayFixedStepTickRate.Hz10 or GameplayFixedStepTickRate.Hz5))
            throw new ArgumentOutOfRangeException(nameof(rate));
        if ((phase is GameplayFixedStepSimulationPhase.WorldTime or GameplayFixedStepSimulationPhase.PhysicsPreparation or
            GameplayFixedStepSimulationPhase.SpaceSimulation or GameplayFixedStepSimulationPhase.Physics or
            GameplayFixedStepSimulationPhase.AfterPhysics) && rate != GameplayFixedStepTickRate.Hz60)
            throw new ArgumentException("时间与物理阶段必须每个固定步执行。", nameof(rate));
        if (systems.ContainsKey(system)) return;
        var registration = new Registration { System = system, Rate = rate };
        LinkedList<Registration> phaseSystems = phases[(int)phase] ??= new LinkedList<Registration>();
        registration.Node = phaseSystems.AddLast(registration);
        systems.Add(system, registration);
    }

    public void Unregister(IGameplayFixedStepSimulationSystem system)
    {
        if (!systems.TryGetValue(system, out Registration registration)) return;
        registration.Active = false;
        registration.Node.List.Remove(registration.Node);
        systems.Remove(system);
    }

    public void SimulateFixedStep(float deltaTime)
    {
        if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        TickIndex++;
        dispatch.Clear();
        foreach (LinkedList<Registration> phase in phases)
            if (phase != null) dispatch.AddRange(phase);
        foreach (Registration registration in dispatch)
        {
            if (!registration.Active) continue;
            if (registration.System is Behaviour behaviour && (behaviour == null || !behaviour.isActiveAndEnabled))
            {
                registration.AccumulatedSeconds = 0d;
                registration.PhaseSeconds = 0d;
                continue;
            }
            if (registration.System is IGameplayFixedStepSimulationReadiness readiness &&
                !readiness.CanAccumulateFixedStepTime)
            {
                registration.AccumulatedSeconds = 0d;
                registration.PhaseSeconds = 0d;
                continue;
            }
            float elapsed = deltaTime;
            if (registration.Rate != GameplayFixedStepTickRate.Hz60)
            {
                // 低频系统一次只消费真实累计时间，绝不在单个物理步内循环追债。
                registration.AccumulatedSeconds += deltaTime;
                registration.PhaseSeconds += deltaTime;
                double interval = 1d / (int)registration.Rate;
                if (registration.PhaseSeconds + 0.00000001d < interval) continue;
                elapsed = (float)registration.AccumulatedSeconds;
                registration.AccumulatedSeconds = 0d;
                registration.PhaseSeconds -= Math.Floor((registration.PhaseSeconds + 0.00000001d) / interval) * interval;
                if (registration.PhaseSeconds < 0d) registration.PhaseSeconds = 0d;
            }
            try
            {
                registration.System.SimulateFixedStep(elapsed);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, registration.System as UnityEngine.Object);
            }
        }
        dispatch.Clear();
    }

    #endregion
}

public static class GameplayFixedStepSimulation
{
    #region Unity 固定步公共入口

    public const int DefaultTicksPerSecond = 60;
    public const float DefaultFixedDeltaTime = 1f / DefaultTicksPerSecond;
    private static GameplayFixedStepSimulationScheduler scheduler = new();
    public static ulong TickIndex => scheduler.TickIndex;
    public static double CurrentTime => Time.fixedTimeAsDouble;
    public static int RegisteredSystemCount => scheduler.RegisteredSystemCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeSystems() => scheduler = new GameplayFixedStepSimulationScheduler();

    public static void Register(IGameplayFixedStepSimulationSystem system, GameplayFixedStepSimulationPhase phase,
        GameplayFixedStepTickRate rate = GameplayFixedStepTickRate.Hz60)
        => scheduler.Register(system, phase, rate);

    public static void Unregister(IGameplayFixedStepSimulationSystem system) => scheduler.Unregister(system);

    internal static void SimulateFixedStep(float deltaTime) => scheduler.SimulateFixedStep(deltaTime);

    #endregion
}
