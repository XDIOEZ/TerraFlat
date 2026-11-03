using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Simulation;
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
    AfterPhysics,
    SnapshotCapture
}

/// <summary>逻辑系统的目标频率；Hz60 表示每个基础 Tick，其余档位按游戏秒累计。</summary>
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

/// <summary>只编排一次游戏 Tick 的系统顺序，帧累积和追赶由游戏时钟管理。</summary>
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
    private readonly LinkedList<Registration>[] phases = new LinkedList<Registration>[(int)GameplayFixedStepSimulationPhase.SnapshotCapture + 1];
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
            GameplayFixedStepSimulationPhase.AfterPhysics or GameplayFixedStepSimulationPhase.SnapshotCapture) && rate != GameplayFixedStepTickRate.Hz60)
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

/// <summary>独立于 Unity FixedUpdate 的游戏时间累积器，超额追赶时间不留到下一渲染帧。</summary>
public sealed class GameplaySimulationFrameClock
{
    #region 游戏时间累积与限次追赶

    public const int MaximumCatchUpTicks = 4;
    private double accumulatedSeconds;
    private int ticksPerSecond = GameplayFixedStepSimulation.DefaultTicksPerSecond;
    public int TicksPerSecond => ticksPerSecond;
    public float TickDeltaTime => 1f / ticksPerSecond;
    public double CurrentTime { get; private set; }
    public double PresentationTime => CurrentTime + accumulatedSeconds;

    public int AdvanceFrame(float unscaledDeltaTime, float timeScale, int targetTicksPerSecond,
        Action<float> simulateTick)
    {
        if (simulateTick == null) throw new ArgumentNullException(nameof(simulateTick));
        if (targetTicksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(targetTicksPerSecond));
        if (ticksPerSecond != targetTicksPerSecond)
        {
            ticksPerSecond = targetTicksPerSecond;
            // 切换 TPS 保留已过去的余量，不能让表现时间倒退或丢掉半个 Tick。
        }
        if (!float.IsFinite(unscaledDeltaTime) || !float.IsFinite(timeScale) ||
            unscaledDeltaTime <= 0f || timeScale <= 0f) return 0;

        double interval = 1d / ticksPerSecond;
        double budget = interval * MaximumCatchUpTicks;
        accumulatedSeconds = Math.Min(budget, accumulatedSeconds + (double)unscaledDeltaTime * timeScale);
        int count = Math.Min(MaximumCatchUpTicks, (int)Math.Floor(accumulatedSeconds / interval + 0.00000001d));
        for (int i = 0; i < count; i++)
        {
            accumulatedSeconds = Math.Max(0d, accumulatedSeconds - interval);
            CurrentTime += interval;
            simulateTick((float)interval);
        }
        return count;
    }

    #endregion
}

public static class GameplayFixedStepSimulation
{
    #region 游戏模拟时钟公共入口

    public const int DefaultTicksPerSecond = 30;
    public const float DefaultTickDeltaTime = 1f / DefaultTicksPerSecond;
    private static GameplayFixedStepSimulationScheduler scheduler = new();
    private static GameplaySimulationFrameClock clock = new();
    private static readonly Action<float> simulateTick = SimulateFixedStep;
    private static bool isSimulatingTick;
    public static ulong TickIndex => scheduler.TickIndex;
    public static double CurrentTime => clock.CurrentTime;
    public static double PresentationTime => clock.PresentationTime;
    public static float TickDeltaTime => clock.TickDeltaTime;
    public static int TicksPerSecond => clock.TicksPerSecond;
    public static bool IsSimulatingTick => isSimulatingTick;
    public static int RegisteredSystemCount => scheduler.RegisteredSystemCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeSystems()
    {
        scheduler = new GameplayFixedStepSimulationScheduler();
        clock = new GameplaySimulationFrameClock();
        GameplaySimulationTime.Publish(0d, 0d);
        isSimulatingTick = false;
    }

    public static void Register(IGameplayFixedStepSimulationSystem system, GameplayFixedStepSimulationPhase phase,
        GameplayFixedStepTickRate rate = GameplayFixedStepTickRate.Hz60)
        => scheduler.Register(system, phase, rate);

    public static void Unregister(IGameplayFixedStepSimulationSystem system) => scheduler.Unregister(system);

    /// <summary>等待下一次由游戏时钟驱动的逻辑与物理 Tick，而非 Unity FixedUpdate。</summary>
    public static IEnumerator WaitForNextTick()
    {
        ulong current = TickIndex;
        while (TickIndex == current) yield return null;
    }

    internal static int SimulateFrame(float unscaledDeltaTime, float timeScale, int ticksPerSecond)
    {
        int steps = clock.AdvanceFrame(unscaledDeltaTime, timeScale, ticksPerSecond, simulateTick);
        GameplaySimulationTime.Publish(clock.CurrentTime, clock.PresentationTime);
        return steps;
    }

    private static void SimulateFixedStep(float deltaTime)
    {
        if (isSimulatingTick) throw new InvalidOperationException("游戏 Tick 不允许重入。");
        GameplaySimulationTime.Publish(clock.CurrentTime, clock.PresentationTime);
        isSimulatingTick = true;
        try { scheduler.SimulateFixedStep(deltaTime); }
        finally { isSimulatingTick = false; }
    }

    #endregion
}
