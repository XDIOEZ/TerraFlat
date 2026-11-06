using UnityEngine;

public sealed partial class Mod_AnimalFlightMovement
{
    #region 飞行避光
    public bool avoidLight;
    [Range(0f, 1f)] public float lightAvoidanceThreshold = 0.35f;
    [Min(1f)] public float lightAvoidanceSearchRadius = 12f;

    private Vector2 flightLightTarget;
    private bool hasFlightLightTarget;
    private float flightLightSearchRemaining;

    public bool IsCurrentFlightPositionTooBright => avoidLight && TryGetFlightLight(body.position, out float light) &&
        light >= Mathf.Clamp01(lightAvoidanceThreshold);

    public bool CanSeekDarkerFlightPosition
    {
        get
        {
            if (!avoidLight || (state.Phase == BirdFlightPhase.Ground && state.MustRecoverStamina) ||
                !TryGetFlightLight(body.position, out float currentLight) ||
                currentLight < Mathf.Clamp01(lightAvoidanceThreshold)) return false;
            if (hasFlightLightTarget && IsDarkerFlightTargetUsable(currentLight)) return true;
            hasFlightLightTarget = false;
            if (flightLightSearchRemaining > 0f) return false;
            flightLightSearchRemaining = 0.5f;
            hasFlightLightTarget = TryFindDarkerFlightPosition(currentLight, out flightLightTarget);
            return hasFlightLightTarget;
        }
    }

    public void BeginFlightLightAvoidance()
    {
        ResetFatigueLanding();
        SetWanderTarget(flightLightTarget);
        if (state.Phase is BirdFlightPhase.Ground or BirdFlightPhase.Landing) BeginTakeoff();
    }

    public bool TickFlightLightAvoidance(float deltaTime)
    {
        if (!hasFlightLightTarget || !TryGetFlightLight(body.position, out float currentLight) ||
            !IsDarkerFlightTargetUsable(currentLight)) return false;
        if (state.Phase is BirdFlightPhase.RunUp or BirdFlightPhase.TakingOff or BirdFlightPhase.Landing)
        {
            TickFlightTransition(deltaTime);
            return true;
        }
        if (state.Phase != BirdFlightPhase.Flying) return false;
        FlyTowards(flightLightTarget, deltaTime);
        return true;
    }

    public void EndFlightLightAvoidance()
    {
        hasFlightLightTarget = false;
        state.HasTarget = false;
        StopFlightAction();
    }

    private void ResetFlightLightAvoidance()
    {
        hasFlightLightTarget = false;
        flightLightSearchRemaining = 0f;
        flightLightTarget = default;
    }

    private static bool TryGetFlightLight(Vector2 position, out float light)
    {
        LightLayerMgr manager = LightLayerMgr.Instance;
        light = 0f;
        return manager != null && manager.TryGetLightLevel(position, out light);
    }

    private bool IsDarkerFlightTargetUsable(float currentLight) => CanLand(flightLightTarget) &&
        TryGetFlightLight(flightLightTarget, out float targetLight) && targetLight + 0.01f < currentLight &&
        flightNavigation.CanTraverse(body.position, flightLightTarget);

    // 只选择真实可落脚且完整航线可达的更暗地点，无候选时交回普通巡飞。
    private bool TryFindDarkerFlightPosition(float currentLight, out Vector2 destination)
    {
        destination = default;
        float bestLight = currentLight - 0.01f;
        bool found = false;
        float radius = Mathf.Max(1f, lightAvoidanceSearchRadius);
        for (int ring = 1; ring <= 3; ring++)
        {
            float distance = radius * ring / 3f;
            for (int index = 0; index < 8; index++)
            {
                float angle = index * Mathf.PI * 0.25f;
                Vector2 candidate = WorldTopologyRuntime.NormalizePosition(body.position +
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance);
                if (!CanLand(candidate) || !TryGetFlightLight(candidate, out float light) || light >= bestLight ||
                    !flightNavigation.CanTraverse(body.position, candidate)) continue;
                bestLight = light;
                destination = candidate;
                found = true;
            }
        }
        return found;
    }
    #endregion
}
