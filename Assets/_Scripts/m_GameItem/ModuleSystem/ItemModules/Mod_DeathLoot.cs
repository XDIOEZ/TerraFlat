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
        //   ModSaveData.WriteData(ref ); // ������Ŀ�߼��������ݱ���
    }

    public override void ModUpdate(float deltaTime)
    {
        // ÿ֡�߼�������Ҫ��
    }

    public override void Act()
    {
        base.Act();
        GenerateLootItems( item.transform.position);
    }

    #region �����߼����ķ���
    /// <summary>���ս��Ʒ�б����Ƿ����ָ��ID����Ʒ</summary>
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
        // ͬ����ƷID
        foreach (var lootData in LootList)
        {
            lootData.SyncItemIDName();
        }
    }

    /// <summary>���ɵ�����Ʒ��ʹ����Ŀͳһ��ʵ�����߼���</summary>
    public void GenerateLootItems(Vector3 spawnPosition, float luckFactor = 1f)
    {
        foreach (var lootData in LootList)
        {
            if (lootData.ItemPrefab == null)
            {
                Debug.LogWarning($"Mod_DeathLoot: ��ƷԤ����[{lootData.ItemIDName}]Ϊ�գ��������ɣ�");
                continue;
            }

            // ��ȡ��Ʒ���ݣ���Ԥ�����У�
            Item originalItem = lootData.ItemPrefab.GetComponent<Item>();
            if (originalItem == null || originalItem.itemData == null)
            {
                Debug.LogWarning($"Mod_DeathLoot: ��ƷԤ����[{lootData.ItemIDName}]ȱ��Item�������Ʒ���ݣ�");
                continue;
            }

            int actualCount = CalculateActualCount(lootData.ItemCountRange, luckFactor);
            if (actualCount <= 0) continue;

            // ��ȡ��ǰchunk��Ϊ������
            Chunk chunk = null;
            ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(
                Chunk.GetChunkPosition(spawnPosition).ToString(),
                out chunk
            );

            if (chunk == null)
            {
                Debug.LogWarning($"Mod_DeathLoot: �޷���ȡ[{spawnPosition}]λ�õ�Chunk��ʹ��Ĭ�ϸ����壡");
            }

            // ʵ����ָ����������Ʒ
            for (int i = 0; i < actualCount; i++)
            {
                // ��¡��Ʒ����
                ItemData newItemData = FastCloner.FastCloner.DeepClone(originalItem.itemData);
                newItemData.Stack.Amount = 1; // ����ʵ��������
                newItemData.Stack.CanBePickedUp = true; // ����ʰȡ������������������ã�

                // ʹ����Ŀͳһ��ʵ��������
                Item newObject = ItemMgr.Instance.InstantiateItem(
                    newItemData.IDName,
                    default,
                    default,
                    default,
                    chunk ? chunk.gameObject : null
                );

                if (newObject == null)
                {
                    Debug.LogError($"Mod_DeathLoot: ʵ����ʧ�ܣ��Ҳ�����Դ��{newItemData.IDName}");
                    continue;
                }

                // ������Ʒ����
                Item newItem = newObject.GetComponent<Item>();
                if (newItem == null)
                {
                    Debug.LogError("Mod_DeathLoot: ��������ȱ��Item�����");
                    Destroy(newObject.gameObject);
                    continue;
                }
                newItem.itemData = newItemData;

                // �������켣����ԭλ�û������������ƫ�ƣ�����ѵ���
                Vector2 randomOffset = new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));
                Vector2 startPos = (Vector2)spawnPosition + randomOffset;
                Vector2 endPos = (Vector2)spawnPosition + new Vector2(
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f)
                ); // �������λ��

                // ���㶯��ʱ��
                float distance = Vector2.Distance(startPos, endPos);

                // �����þ�̬������ִ�е��䶯��
                Mod_ItemDroper.StaticDropItem_Pos(newItem, startPos, endPos, 1);
            }
        }
    }

    /// <summary>��������ϵ������ʵ�ʵ�������</summary>
    private int CalculateActualCount(Vector2 countRange, float luckFactor)
    {
        int min = Mathf.FloorToInt(countRange.x);
        int max = Mathf.CeilToInt(countRange.y);
        min = Mathf.Max(min, 0); // ȷ�������Ǹ�
        max = Mathf.Max(max, min); // ȷ��max��С��min

        if (luckFactor >= -1f && luckFactor < 0f)
        {
            // ����ϵ��-1~0���������������١�����Χ��(1 + luckFactor)������С
            float scale = 1f + luckFactor; // ��Χ0~1
            int newMax = Mathf.FloorToInt(min + (max - min) * scale);
            newMax = Mathf.Max(min, newMax); // ȷ����max��С��min
            return Random.Range(min, newMax + 1);
        }
        else if (luckFactor >= 0f && luckFactor < 1f)
        {
            // ����ϵ��0~1���������������١���һ����ϵ��0ʱֻ����Сֵ��
            if (Mathf.Approximately(luckFactor, 0f))
                return min;

            int offset = Mathf.FloorToInt((max - min) * (1f - luckFactor));
            int newMax = min + offset;
            return Random.Range(min, newMax + 1);
        }
        else if (Mathf.Approximately(luckFactor, 1f))
        {
            // ����ϵ��=1�����޸ĵ���������ȡ������Χ
            return Random.Range(min, max + 1);
        }
        else if (luckFactor > 1f && luckFactor <= 2f)
        {
            // ����ϵ��1~2������������������Χ������ϵ�������Ŵ�
            int baseCount = Random.Range(min, max + 1);
            return Mathf.FloorToInt(baseCount * luckFactor);
        }
        else
        {
            // �쳣ϵ��������������Χ
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
            Debug.LogWarning($"Mod_LootData: ��ƷԤ����Ϊ�գ�����ͬ����");
            return;
        }

        ItemIDName = ItemPrefab.GetComponent<Item>().itemData.IDName;
    }
}
