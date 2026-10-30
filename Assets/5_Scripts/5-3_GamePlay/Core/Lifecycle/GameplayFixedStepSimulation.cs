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

public interface IGameplayFixedStepSimulationSystem
{
    void SimulateFixedStep(float deltaTime);
}

/// <summary>只编排一次固定步的系统顺序，补步与倍速统一交给 Unity。</summary>
public sealed class GameplayFixedStepSimulationScheduler
{
    #region 固定步系统登记与执行

    private sealed class Registration
    {
        public IGameplayFixedStepSimulationSystem System;
        public LinkedListNode<Registration> Node;
        public bool Active = true;
    }

    private readonly Dictionary<IGameplayFixedStepSimulationSystem, Registration> systems = new();
    private readonly LinkedList<Registration>[] phases = new LinkedList<Registration>[(int)GameplayFixedStepSimulationPhase.AfterPhysics + 1];
    private readonly List<Registration> dispatch = new();
    public ulong TickIndex { get; private set; }
    public int RegisteredSystemCount => systems.Count;

    public void Register(IGameplayFixedStepSimulationSystem system, GameplayFixedStepSimulationPhase phase)
    {
        if (system == null) throw new ArgumentNullException(nameof(system));
        if (systems.ContainsKey(system)) return;
        var registration = new Registration { System = system };
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
        TickIndex++;
        dispatch.Clear();
        foreach (LinkedList<Registration> phase in phases)
            if (phase != null) dispatch.AddRange(phase);
        foreach (Registration registration in dispatch)
        {
            if (!registration.Active) continue;
            if (registration.System is Behaviour behaviour && (behaviour == null || !behaviour.isActiveAndEnabled)) continue;
            try
            {
                registration.System.SimulateFixedStep(deltaTime);
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

    public static void Register(IGameplayFixedStepSimulationSystem system, GameplayFixedStepSimulationPhase phase)
        => scheduler.Register(system, phase);

    public static void Unregister(IGameplayFixedStepSimulationSystem system) => scheduler.Unregister(system);

    internal static void SimulateFixedStep(float deltaTime) => scheduler.SimulateFixedStep(deltaTime);

    #endregion
}
