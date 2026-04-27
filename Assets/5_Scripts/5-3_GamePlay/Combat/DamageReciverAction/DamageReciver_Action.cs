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

        int dropAmount = Random.Range(Loot.MinAmount, Loot.MaxAmount + 1);
        if (dropAmount <= 0)
            return;

        Vector2 startPos = damageInfo != null ? (Vector2)damageInfo.HitPosition : (Vector2)receiver.transform.position;
        // 支持Y轴偏移，用于从高处生成物品（例如树上的椰子）
        startPos.y += SpawnYOffset;

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
            Vector2 endPos = startPos + randomDir * randomDist;

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
        Chunk chunk = null;
        if (ChunkMgr.Instance != null)
        {
            ChunkMgr.Instance.TryGetActiveChunkByPos(Chunk.GetChunkPosition(spawnPos), out chunk);
        }

        Item spawnedItem = chunk != null
            ? ItemMgr.Instance.InstantiateItem(itemName, spawnPos, Quaternion.identity, Vector3.one, chunk.gameObject)
            : ItemMgr.Instance.InstantiateItem(itemName, spawnPos);

        if (spawnedItem == null)
        {
            Debug.LogWarning($"[DamageReceiver] Spawn item action failed. ItemName={itemName}");
        }

        return spawnedItem;
    }
}
