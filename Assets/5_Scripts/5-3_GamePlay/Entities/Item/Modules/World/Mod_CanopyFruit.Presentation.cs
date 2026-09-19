using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>树果的纯视觉与短暂碰撞桥；查询只覆盖当帧真实飞行区间，离线历史不追溯伤害。</summary>
public sealed partial class Mod_CanopyFruit
{
    #region 飞行碰撞
    private readonly List<RaycastHit2D> sweepHits = new(); // 无固定容量截断的短期碰撞结果。
    private readonly HashSet<DamageReceiver> hitReceivers = new(); // 同一目标的兄弟 Collider 去重。
    private DamageReceiver hitTarget; // 具名伤害调用的当前目标。
    private FallingFruitDamage hitSource; // 仅查询期间存在的伤害请求。

    /// <summary>扫掠本帧飞行路径，临时危险不附着到任何正式掉落实体。</summary>
    private void SweepFlight(CanopyFruitRecord fruit)
    {
        if (!liveStep || fruit.HitConsumed || stepTo <= fruit.FallAt || stepFrom >= fruit.LandAt) return;
        Vector2 start = FlightPosition(fruit, Math.Max(stepFrom, fruit.FallAt));
        Vector2 end = FlightPosition(fruit, Math.Min(stepTo, fruit.LandAt));
        Vector2 displacement = end - start;
        if (displacement.sqrMagnitude <= 0) return;
        int receiverMask = LayerMask.GetMask("DamageReciver");
        if (receiverMask == 0) throw new InvalidOperationException("缺少正式 DamageReciver 碰撞层。");
        ContactFilter2D filter = new() { useTriggers = true, useLayerMask = true, layerMask = receiverMask, useDepth = false };
        sweepHits.Clear();
        hitReceivers.Clear();
        Physics2D.CircleCast(start, CollisionRadius, displacement.normalized, filter, sweepHits, displacement.magnitude);
        sweepHits.Sort(CompareHits);
        hitSource = new FallingFruitDamage(BluntDamage);
        try
        {
            foreach (RaycastHit2D hit in sweepHits)
            {
                DamageReceiver target = GameplayPhysics2D.ResolveComponent<DamageReceiver>(hit.collider);
                if (target == null || target.item == item || !hitReceivers.Add(target)) continue;
                hitTarget = target;
                double hitTime = Math.Min(stepTo, fruit.LandAt - 0.000001);
                if (FallingFruitDamage.TryHit(fruit, hitTime, ApplyImpactDamage, NextImpactRandom, SplitChance)) break;
            }
        }
        finally { hitTarget = null; hitSource = null; }
    }

    /// <summary>最近命中优先，等距时以对象标识稳定排序。</summary>
    private static int CompareHits(RaycastHit2D left, RaycastHit2D right)
    {
        int order = left.distance.CompareTo(right.distance);
        return order != 0 ? order : left.collider.GetInstanceID().CompareTo(right.collider.GetInstanceID());
    }

    /// <summary>伤害通过正式生命结算；显式请求头部，缺失时由接收器选择有效部位。</summary>
    private float ApplyImpactDamage() => hitTarget.Hurt(hitSource);

    /// <summary>撞击随机也进入持久流，保存恢复不会重新抽已命中的转换结果。</summary>
    private double NextImpactRandom() => CanopyFruitTimeline.Next(state, 0, 1000000) / 1000000d;

    /// <summary>匀加速下落，轨迹由存档中冻结的端点重建。</summary>
    public static Vector2 FlightPosition(CanopyFruitRecord fruit, double time)
    {
        float progress = (float)Math.Max(0, Math.Min(1, (time - fruit.FallAt) / (fruit.LandAt - fruit.FallAt)));
        return Vector2.Lerp(new Vector2(fruit.StartX, fruit.StartY), new Vector2(fruit.EndX, fruit.EndY), progress * progress);
    }
    #endregion

    #region 独立果实表现
    /// <summary>只引用已加载本体定义的青椰子和半椰子素材，不创建库存实例。</summary>
    private static Sprite RequireSprite(string itemId)
    {
        if (!GameRes.ExistingInstance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition) || definition.Sprite == null)
            throw new InvalidOperationException($"树果引用的物品定义或贴图不存在：{itemId}");
        return definition.Sprite;
    }

    /// <summary>椭圆果位属于树 Sprite 的局部坐标，随树龄缩放一起变化。</summary>
    private Vector2 CrownPosition(int slot)
    {
        float angle = slot * Mathf.PI * 2f / Settings.MaximumCount;
        Vector2 local = CrownCenter + new Vector2(Mathf.Cos(angle) * CrownSpread.x, Mathf.Sin(angle) * CrownSpread.y);
        SpriteRenderer renderer = item.Sprite;
        Sprite sprite = renderer.sprite;
        if (UseNormalizedCrownAnchor && sprite != null)
        {
            Vector2 uv = CrownAnchorUV + new Vector2(Mathf.Cos(angle) * CrownSpreadUV.x, Mathf.Sin(angle) * CrownSpreadUV.y);
            local = (Vector2.Scale(uv, sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit;
        }
        if (renderer.flipX) local.x = -local.x;
        if (renderer.flipY) local.y = -local.y;
        return item.Sprite.transform.TransformPoint(local);
    }

    /// <summary>刷新在冠和飞行果，所有节点仅有 SpriteRenderer。</summary>
    private void RefreshVisuals()
    {
        foreach (CanopyFruitRecord fruit in state.Fruits)
            ShowFruit(fruit, CrownPosition(fruit.Slot), Mathf.Lerp(SmallScale, 1, fruit.Growth01(state.Time)));
        foreach (CanopyFruitRecord fruit in state.Flights)
            ShowFruit(fruit, FlightPosition(fruit, state.Time), 1);
    }

    /// <summary>按共享贴图原始宽度归一大小，排序跟随源树，不烘焙进树图。</summary>
    private void ShowFruit(CanopyFruitRecord fruit, Vector2 position, float scale)
    {
        if (!visuals.TryGetValue(fruit.Id, out SpriteRenderer visual))
        {
            GameObject node = new($"CanopyFruit_{fruit.Id}");
            node.transform.SetParent(transform, false);
            visual = node.AddComponent<SpriteRenderer>();
            visuals.Add(fruit.Id, visual);
        }
        visual.sprite = fruit.Split ? splitSprite : fruitSprite;
        visual.transform.position = position;
        float worldScale = FruitWidth * scale / visual.sprite.bounds.size.x;
        Vector3 parentScale = transform.lossyScale;
        visual.transform.localScale = new Vector3(worldScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            worldScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)), 1);
        visual.sortingLayerID = item.Sprite.sortingLayerID;
        visual.sortingOrder = item.Sprite.sortingOrder + 1;
    }

    /// <summary>移交后立即隐藏并销毁纯表现，删除字典身份防止池复用残留。</summary>
    private void RemoveVisual(int id)
    {
        if (!visuals.TryGetValue(id, out SpriteRenderer visual)) return;
        visuals.Remove(id);
        visual.gameObject.SetActive(false);
        Destroy(visual.gameObject);
    }
    #endregion
}
