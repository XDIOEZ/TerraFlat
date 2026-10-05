using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 行为图共享的感知能力：按 Actor 配置刷新 Detector 快照，再以距离、视线、Tag 和阵营筛选目标。
/// 条件与节点通过同一服务读取目标，避免物种重新实现玩家识别或食物筛选。
/// </summary>
internal sealed class AIBehaviorGraphPerception
{
    #region 感知请求与筛选

    private readonly Item _actor; // 当前动物。
    private readonly Mod_ItemDetector _detector; // 权威感知快照。
    private readonly float _refreshInterval; // Detector 请求间隔。
    private float _refreshTimer; // 下次请求计时。

    public AIBehaviorGraphPerception(Item actor, Mod_ItemDetector detector, float refreshInterval)
    {
        _actor = actor;
        _detector = detector;
        _refreshInterval = refreshInterval;
        _refreshTimer = refreshInterval;
    }

    public void Tick(float deltaTime)
    {
        if (_detector == null)
            return;
        _refreshTimer += Mathf.Max(0f, deltaTime);
        if (_refreshTimer < _refreshInterval)
            return;
        _refreshTimer = 0f;
        _detector.RequestDetectorUpdate();
    }

    /// <summary>查找最近且仍在有效感知范围内的玩家或标签威胁。</summary>
    public Item FindClosestThreat(float distance, string[] tags, bool includePlayers)
    {
        RequireDetector();
        List<Item> candidates = _detector.CurrentItemsInArea;
        Item closest = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            Item candidate = candidates[i];
            if (candidate == null || candidate == _actor || candidate.DestructionHandled)
                continue;
            bool isPlayer = candidate is Player || HasTag(candidate, "Player");
            if ((!includePlayers || !isPlayer) && !HasAnyTag(candidate, tags))
                continue;
            if (!AIFleeUtility.IsWithinEscapeRange(_actor.transform.position, candidate, _detector, distance))
                continue;
            float candidateDistance = WorldTopologyRuntime.SqrDistance(
                _actor.transform.position, candidate.transform.position);
            if (candidateDistance >= closestDistance)
                continue;
            closest = candidate;
            closestDistance = candidateDistance;
        }
        return closest;
    }

    /// <summary>查找带食物标签、可实际摄食且仍在有效感知范围内的物品。</summary>
    public Item FindClosestTaggedFood(float distance, string[] tags)
    {
        RequireDetector();
        List<Item> candidates = _detector.CurrentItemsInArea;
        Item closest = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            Item candidate = candidates[i];
            if (candidate == null || candidate == _actor || candidate.DestructionHandled ||
                !HasAnyTag(candidate, tags) || !IsConsumableFood(candidate))
                continue;
            float effectiveDistance = Mod_ItemDetector.CalculateEffectiveDetectionRadius(distance, candidate);
            float candidateDistance = WorldTopologyRuntime.SqrDistance(
                _actor.transform.position, candidate.transform.position);
            if (candidateDistance > effectiveDistance * effectiveDistance || candidateDistance >= closestDistance)
                continue;
            closest = candidate;
            closestDistance = candidateDistance;
        }
        return closest;
    }

    /// <summary>在威胁目标中按阵营和生命值选择攻击对象。</summary>
    public Item FindClosestAttackTarget(float distance, string[] tags, bool includePlayers)
    {
        RequireDetector();
        List<Item> candidates = _detector.CurrentItemsInArea;
        Item closest = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            Item candidate = candidates[i];
            if (!IsLivingAttackTarget(candidate))
                continue;
            bool isPlayer = candidate is Player || HasTag(candidate, "Player");
            if ((!includePlayers || !isPlayer) && !HasAnyTag(candidate, tags))
                continue;
            if (!AIFleeUtility.IsWithinEscapeRange(_actor.transform.position, candidate, _detector, distance))
                continue;
            float candidateDistance = WorldTopologyRuntime.SqrDistance(
                _actor.transform.position, candidate.transform.position);
            if (candidateDistance >= closestDistance)
                continue;
            closest = candidate;
            closestDistance = candidateDistance;
        }
        return closest;
    }

    public bool IsLivingAttackTarget(Item target)
    {
        if (target == null || target == _actor || target.DestructionHandled)
            return false;
        Mod_DamageReceiver receiver = target.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        return receiver != null && receiver.Hp > 0f && FactionRelationService.CanAttack(_actor, target);
    }

    private void RequireDetector()
    {
        if (_detector == null)
            throw new InvalidOperationException("AI 行为图的目标搜索需要 Mod_ItemDetector。");
    }

    private static bool HasAnyTag(Item candidate, string[] expectedTags)
    {
        if (expectedTags == null)
            return false;
        for (int i = 0; i < expectedTags.Length; i++)
        {
            if (HasTag(candidate, expectedTags[i]))
                return true;
        }
        return false;
    }

    private static bool HasTag(Item candidate, string tag)
    {
        List<string> tags = candidate.itemData?.Tags;
        if (tags == null || string.IsNullOrWhiteSpace(tag))
            return false;
        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsConsumableFood(Item candidate)
    {
        if (candidate.itemData?.Stack == null || candidate.itemData.Stack.Amount <= 0f)
            return false;
        Mod_Food food = candidate.GetComponentInChildren<Mod_Food>(true);
        return food != null && food.ConsumptionEnabled;
    }

    #endregion
}
