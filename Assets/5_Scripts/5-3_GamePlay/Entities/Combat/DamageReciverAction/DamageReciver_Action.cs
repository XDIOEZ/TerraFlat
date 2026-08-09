using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DamageReceiverDamageInfo
{
    public DamageReceiver Receiver;
    public Item ReceiverItem;
    public IDamageSender DamageSender;
    public Item Attacker;
    public float DamageValue;
    public float SenderDamageValue;
    public float HpBefore;
    public float HpAfter;
    public bool IsFatal;
    public Vector3 HitPosition;
    public float Time;
    public List<BodyPartDamageInfo> BodyPartHits;
}

[System.Serializable]
public class DamageReciver_Action
{
    public bool Enabled = true;

    public virtual void Execute(DamageReceiver receiver, DamageReceiverDamageInfo damageInfo)
    {
        // 默认实现：子类可重写（基类不执行任何操作）
    }

    public virtual void OnValidate()
    {
    }
}

[System.Serializable]
public class DamageReciver_Action_SpawnItem : DamageReciver_Action
{
    [Header("生成物")]
    public LootEntry Loot = new LootEntry();

    [Header("飞出动画")]
    public float SpawnRadius = 1.2f;

    [Min(0.05f)]
    public float ThrowDuration = 0.5f;

    public float ThrowBezierOffset = 0.8f;
    public float ThrowArcHeight = 0.6f;

    [Header("生成位置偏移")]
    [Tooltip("生成点的Y轴偏移，用于高处掉落的物品（例如树上的椰子）。正值向上偏移。")]
    public float SpawnYOffset = 0f;

    public override void Execute(DamageReceiver receiver, DamageReceiverDamageInfo damageInfo)
    {
        if (receiver == null || Loot == null || string.IsNullOrEmpty(Loot.LootPrefabName))
            return;

        if (Random.value > Loot.DropChance)
            return;

        int dropAmount = GameDifficultyService.ScaleRandomizedAmount(
            Random.Range(Loot.MinAmount, Loot.MaxAmount + 1),
            GameDifficultyService.Current.World.LootAmountMultiplier);
        if (dropAmount <= 0)
            return;

        Vector2 groundOrigin = damageInfo != null
            ? (Vector2)damageInfo.HitPosition
            : (Vector2)receiver.transform.position;
        Vector2 startPos = groundOrigin + Vector2.up * SpawnYOffset;

        for (int i = 0; i < dropAmount; i++)
        {
            Item spawnedItem = InstantiateDropItem(Loot.LootPrefabName, startPos);
            if (spawnedItem == null)
                continue;

            spawnedItem.Load();
            spawnedItem.SetInHand(false);
            spawnedItem.itemData.Stack.Amount = 1;

            Vector2 randomDir = Random.insideUnitCircle.normalized;
            if (randomDir == Vector2.zero)
                randomDir = Vector2.right;

            float randomDist = Random.Range(0.5f * SpawnRadius, SpawnRadius);
            // 生成点可以位于树冠等高处，但落点始终回到物品根节点周围。
            Vector2 endPos = groundOrigin + randomDir * randomDist;

            Mod_BaseDroper.StaticDropItem_Pos(
                spawnedItem,
                startPos,
                endPos,
                Mathf.Max(0.05f, ThrowDuration),
                Mod_BaseDroper.MoveMode.BezierCurve,
                ThrowBezierOffset,
                ThrowArcHeight);
        }
    }

    public override void OnValidate()
    {
        Loot?.OnValidate();
    }

    private static Item InstantiateDropItem(string itemName, Vector2 spawnPos)
    {
        // 归属由 Mod_Droping.Load 统一绑定到新版 ChunkView，
        // 这里不再同步查询旧 Chunk，也不在击杀瞬间触发旧区块加载。
        Item spawnedItem = ItemMgr.Instance.InstantiateItem(
            itemName, spawnPos, Quaternion.identity, Vector3.one);

        if (spawnedItem == null)
        {
            Debug.LogWarning($"[DamageReceiver] Spawn item action failed. ItemName={itemName}");
        }

        return spawnedItem;
    }
}
