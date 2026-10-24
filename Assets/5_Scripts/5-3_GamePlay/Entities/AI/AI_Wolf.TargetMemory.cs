using Sirenix.OdinInspector;
using UnityEngine;

public partial class AI_Wolf
{
    #region 丢失目标后的追踪记忆

    [TabGroup("配置", "行为"), BoxGroup("配置/行为/搜索"), LabelText("赶往最后位置上限"), MinValue(0.1f)]
    public float lostTargetTravelTimeout = 15f;
    [TabGroup("配置", "行为"), BoxGroup("配置/行为/搜索"), LabelText("到点搜索时长"), MinValue(0.1f)]
    public float lostTargetSearchDuration = 3f;
    [TabGroup("配置", "行为"), BoxGroup("配置/行为/搜索"), LabelText("最后位置到达距离"), MinValue(0.05f)]
    public float lostTargetArrivalDistance = 0.6f;

    private bool _threatVisible;
    private bool _hasThreatMemory;
    private bool _canSearchForThreat;
    private bool _waitingAtLastThreatPosition;
    private bool _searchArrivalDetectionRequested;
    private Vector3 _lastKnownThreatPosition;
    private int _rememberedThreatGuid;
    private uint _rememberedThreatGeneration;
    private float _searchTravelRemaining;
    private float _searchWaitRemaining;

    internal bool HasThreatMemory => _hasThreatMemory;
    internal bool ThreatVisible => _threatVisible;
    internal Vector3 LastKnownThreatPosition => _lastKnownThreatPosition;
    internal bool WaitingAtLastThreatPosition => _waitingAtLastThreatPosition;

    // 目标引用同时核对生命、世界和运行代际，避免追踪回池后的另一只实体。
    private bool IsUsableThreat(Item target)
    {
        if (item == null || target == null || target == item || target.itemData == null ||
            target.DestructionHandled || target.IsUnloadingModules ||
            target.gameObject.scene != item.gameObject.scene ||
            !FactionRelationService.CanAttack(item, target))
            return false;
        Mod_DamageReceiver health = target.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        return health == null || health.Hp > 0f;
    }

    private bool MatchesRememberedThreat(Item target)
        => _hasThreatMemory && target != null && target == _currentThreat &&
           target.itemData?.Guid == _rememberedThreatGuid &&
           target.RuntimeGeneration == _rememberedThreatGeneration;

    // 仅真正看见或同伴报告的新位置才能更新记忆，丢失视线后不读取目标实时坐标。
    private void RememberThreat(Item target, Vector3 knownPosition, bool visible, bool pursue = false)
    {
        if (!MatchesRememberedThreat(target))
            ForgetThreat();
        _currentThreat = target;
        _rememberedThreatGuid = target.itemData.Guid;
        _rememberedThreatGeneration = target.RuntimeGeneration;
        _lastKnownThreatPosition = knownPosition;
        _hasThreatMemory = true;
        _threatVisible = visible;
        _canSearchForThreat |= pursue || _currentState == WolfState.Chase ||
                               _currentState == WolfState.Attack || _currentState == WolfState.Search;
        _waitingAtLastThreatPosition = false;
        _searchArrivalDetectionRequested = false;
        _searchTravelRemaining = Mathf.Max(0.1f, lostTargetTravelTimeout);
        _searchWaitRemaining = Mathf.Max(0.1f, lostTargetSearchDuration);
    }

    private void ForgetThreat()
    {
        _currentThreat = null;
        _hasThreatMemory = false;
        _threatVisible = false;
        _canSearchForThreat = false;
        _waitingAtLastThreatPosition = false;
        _searchArrivalDetectionRequested = false;
        _lastKnownThreatPosition = default;
        _rememberedThreatGuid = 0;
        _rememberedThreatGeneration = 0;
        _searchTravelRemaining = _searchWaitRemaining = 0f;
        _packAssistTimer = _alertTimer = 0f;
        ClearChaseFormation();
    }

    private bool ShouldSearch()
        => !_threatVisible && _canSearchForThreat && MatchesRememberedThreat(_currentThreat) &&
           !IsChaseTargetBlockedByPathCost(_currentThreat);

    private void UpdateSearchArrival()
    {
        if (!ShouldSearch() || _waitingAtLastThreatPosition)
            return;
        float arrivalDistance = Mathf.Max(0.05f, lostTargetArrivalDistance);
        if (WorldTopologyRuntime.SqrDistance(transform.position, _lastKnownThreatPosition) <=
            arrivalDistance * arrivalDistance)
            _waitingAtLastThreatPosition = true;
    }

    private void TickThreatMemory(float deltaTime)
    {
        if (_currentState != WolfState.Search || !ShouldSearch())
            return;
        UpdateSearchArrival();
        if (_waitingAtLastThreatPosition)
            _searchWaitRemaining = DecrementTimer(_searchWaitRemaining, deltaTime);
        else
            _searchTravelRemaining = DecrementTimer(_searchTravelRemaining, deltaTime);
        if ((_waitingAtLastThreatPosition ? _searchWaitRemaining : _searchTravelRemaining) > 0f)
            return;
        ForgetThreat();
        _mover?.StopMovement();
    }

    private void TickSearch()
    {
        if (!ShouldSearch()) { StopMove(); return; }
        UpdateSearchArrival();
        if (_waitingAtLastThreatPosition)
        {
            StopMove();
            // 到点额外请求一次新感知，之后仍沿用公共检测周期，不在此反复扫描。
            if (!_searchArrivalDetectionRequested)
            {
                _searchArrivalDetectionRequested = true;
                _detector.RequestDetectorUpdate();
            }
            return;
        }

        WorldNavigationDestinationResult result = MoveToChaseTarget(
            _lastKnownThreatPosition, chasePathCostLimit, Mathf.Max(0.05f, lostTargetArrivalDistance));
        if (TryHandleRejectedChasePath(result, _currentThreat, chasePathRetryDelay) ||
            result == WorldNavigationDestinationResult.Failed)
        {
            ForgetThreat();
            StopMove();
            return;
        }
        FaceTarget(_lastKnownThreatPosition);
    }

    #endregion
}
