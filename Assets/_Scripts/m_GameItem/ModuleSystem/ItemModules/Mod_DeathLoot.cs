using MemoryPack;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using UnityEditorInternal.Profiling.Memory.Experimental;
using UnityEngine;

public class Mod_DeathLoot : Module
{
    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public List<Mod_LootData> LootList = new List<Mod_LootData>();

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Grow;
        }
    }

    public override void Load()
    {
        item.itemMods.GetMod_ByID(ModText.Hp, out DamageReceiver mod);
        mod.OnDead += Act;
    }

    public override void Save()
    {
        //   ModSaveData.WriteData(ref ); // 根据项目逻辑补充数据保存
    }

    public override void ModUpdate(float deltaTime)
    {
        // 每帧逻辑（若需要）
    }

    public override void Act()
    {
        base.Act();
        GenerateLootItems( item.transform.position);
    }

    #region 掉落逻辑核心方法
    /// <summary>检查战利品列表中是否存在指定ID的物品</summary>
    public bool CheckLootExists(string itemIDName)
    {
        foreach (var lootData in LootList)
        {
            if (lootData.ItemIDName == itemIDName)
            {
                return true;
            }
        }
        return false;
    }

    public void OnValidate()
    {
        // 同步物品ID
        foreach (var lootData in LootList)
        {
            lootData.SyncItemIDName();
        }
    }

    /// <summary>生成掉落物品（使用项目统一的实例化逻辑）</summary>
    public void GenerateLootItems(Vector3 spawnPosition, float luckFactor = 1f)
    {
        foreach (var lootData in LootList)
        {
            if (lootData.ItemPrefab == null)
            {
                Debug.LogWarning($"Mod_DeathLoot: 物品预制体[{lootData.ItemIDName}]为空，跳过生成！");
                continue;
            }

            // 获取物品数据（从预制体中）
            Item originalItem = lootData.ItemPrefab.GetComponent<Item>();
            if (originalItem == null || originalItem.itemData == null)
            {
                Debug.LogWarning($"Mod_DeathLoot: 物品预制体[{lootData.ItemIDName}]缺少Item组件或物品数据！");
                continue;
            }

            int actualCount = CalculateActualCount(lootData.ItemCountRange, luckFactor);
            if (actualCount <= 0) continue;

            // 获取当前chunk作为父物体
            Chunk chunk = null;
            ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(
                Chunk.GetChunkPosition(spawnPosition).ToString(),
                out chunk
            );

            if (chunk == null)
            {
                Debug.LogWarning($"Mod_DeathLoot: 无法获取[{spawnPosition}]位置的Chunk，使用默认父物体！");
            }

            // 实例化指定数量的物品
            for (int i = 0; i < actualCount; i++)
            {
                // 克隆物品数据
                ItemData newItemData = FastCloner.FastCloner.DeepClone(originalItem.itemData);
                newItemData.Stack.Amount = 1; // 单个实例的数量
                newItemData.Stack.CanBePickedUp = true; // 允许拾取（动画结束后会再设置）

                // 使用项目统一的实例化方法
                Item newObject = ItemMgr.Instance.InstantiateItem(
                    newItemData.IDName,
                    default,
                    default,
                    default,
                    chunk ? chunk.gameObject : null
                );

                if (newObject == null)
                {
                    Debug.LogError($"Mod_DeathLoot: 实例化失败，找不到资源：{newItemData.IDName}");
                    continue;
                }

                // 设置物品数据
                Item newItem = newObject.GetComponent<Item>();
                if (newItem == null)
                {
                    Debug.LogError("Mod_DeathLoot: 新物体中缺少Item组件！");
                    Destroy(newObject.gameObject);
                    continue;
                }
                newItem.itemData = newItemData;

                // 计算掉落轨迹（在原位置基础上增加随机偏移，避免堆叠）
                Vector2 randomOffset = new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));
                Vector2 startPos = (Vector2)spawnPosition + randomOffset;
                Vector2 endPos = (Vector2)spawnPosition + new Vector2(
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f)
                ); // 随机掉落位置

                // 计算动画时间
                float distance = Vector2.Distance(startPos, endPos);

                // 【调用静态方法】执行掉落动画
                Mod_ItemDroper.StaticDropItem_Pos(newItem, startPos, endPos, 1);
            }
        }
    }

    /// <summary>根据幸运系数计算实际掉落数量</summary>
    private int CalculateActualCount(Vector2 countRange, float luckFactor)
    {
        int min = Mathf.FloorToInt(countRange.x);
        int max = Mathf.CeilToInt(countRange.y);
        min = Mathf.Max(min, 0); // 确保数量非负
        max = Mathf.Max(max, min); // 确保max不小于min

        if (luckFactor >= -1f && luckFactor < 0f)
        {
            // 幸运系数-1~0：掉落数量“减少”，范围按(1 + luckFactor)比例缩小
            float scale = 1f + luckFactor; // 范围0~1
            int newMax = Mathf.FloorToInt(min + (max - min) * scale);
            newMax = Mathf.Max(min, newMax); // 确保新max不小于min
            return Random.Range(min, newMax + 1);
        }
        else if (luckFactor >= 0f && luckFactor < 1f)
        {
            // 幸运系数0~1：掉落倾向“数量少”的一方（系数0时只掉最小值）
            if (Mathf.Approximately(luckFactor, 0f))
                return min;

            int offset = Mathf.FloorToInt((max - min) * (1f - luckFactor));
            int newMax = min + offset;
            return Random.Range(min, newMax + 1);
        }
        else if (Mathf.Approximately(luckFactor, 1f))
        {
            // 幸运系数=1：不修改掉落数量，取正常范围
            return Random.Range(min, max + 1);
        }
        else if (luckFactor > 1f && luckFactor <= 2f)
        {
            // 幸运系数1~2：掉落数量“超出范围”，按系数倍数放大
            int baseCount = Random.Range(min, max + 1);
            return Mathf.FloorToInt(baseCount * luckFactor);
        }
        else
        {
            // 异常系数：返回正常范围
            return Random.Range(min, max + 1);
        }
    }
    #endregion
}
[System.Serializable]
[MemoryPackable]
public partial class Mod_LootData
{
    public string ItemIDName;
    [MemoryPackIgnore]
    [JsonIgnore]
    public GameObject ItemPrefab;
    public Vector2 ItemCountRange = new Vector2(1, 1);
    public float LuckFactor = 1f;

    public void SyncItemIDName()
    {

        if (ItemPrefab == null)
        {
            Debug.LogWarning($"Mod_LootData: 物品预制体为空，跳过同步！");
            return;
        }

        ItemIDName = ItemPrefab.GetComponent<Item>().itemData.IDName;
    }
}
