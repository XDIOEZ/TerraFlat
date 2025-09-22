using Newtonsoft.Json;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using Unity.Collections;
using UnityEngine;
using static Mod_Building;
using ReadOnlyAttribute = Unity.Collections.ReadOnlyAttribute;

/// <summary>
/// 处理模块伤害接收与反馈动画
/// </summary>
public class DamageReceiver : Module
{
    #region 数据引用

    public Ex_ModData modData;
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }

    [SerializeField]
    public DamageReceiver_SaveData Data = new DamageReceiver_SaveData();

    public GameValue_float MaxHp
    {
        get => Data.MaxHp;
        set => Data.MaxHp = value;
    }

    public float Hp
    {
        get => Data.Hp;
        set => Data.Hp = value;
    }

    public UltEvent OnDead = new ();

[System.Serializable]
public class DamageReceiver_SaveData
{
    [Header("生命值设置")]
    public float Hp = 100;
    public GameValue_float MaxHp = new GameValue_float(100);
    public GameValue_float Defense = new GameValue_float(0);
    [Header("伤害者的UID列表")]
    public List<int> AttackersUIDs = new List<int>();

    [Header("是否显示面板")]
    public bool ShowCanvas = false;

    [Header("伤害接收间隔时间 (秒)")]
    [Min(0f)]
    public float DamageInterval = 0.1f;

    [Header("血量归零后多久才销毁物体 (秒)")]//-1表示永久存活 0表示不延迟销毁物体 
    public float DestroyDelay = 0f;
    
    // 修复循环引用问题：使用字符串存储预制体名称而不是直接引用GameObject
    [Header("战利品设置")]
    [ListDrawerSettings()]
    public List<LootEntry> LootTable = new List<LootEntry>();
}

// 战利品条目类
[System.Serializable]
public class LootEntry
{
    [Tooltip("战利品预制体")]
    [JsonIgnore] // 避免JSON序列化此字段
    [UnityEngine.Serialization.FormerlySerializedAs("LootPrefab")]
    public GameObject LootPrefab;
    
    [Tooltip("战利品预制体名称")]
    [SerializeField]
    [ReadOnly]
    public string LootPrefabName = "";
    
    [Tooltip("掉落概率 (0-1)")]
    [Range(0f, 1f)]
    public float DropChance = 1f;
    
    [Tooltip("最小掉落数量")]
    public int MinAmount = 1;
    
    [Tooltip("最大掉落数量")]
    public int MaxAmount = 1;
    
    
    // 公共方法用于更新预制体名称（供编辑器使用）
    public void UpdatePrefabName()
    {
        #if UNITY_EDITOR
        if (LootPrefab != null)
        {
                LootPrefabName = LootPrefab.name;
        }
        #endif
    }
    
    // 重置方法，用于清除引用但保留名称
    public void ClearPrefabReference()
    {
        LootPrefab = null;
    }
}
    private void OnValidate()
    {
        // 自动更新所有战利品条目的预制体名称
        if (Data != null && Data.LootTable != null)
        {
            foreach (var lootEntry in Data.LootTable)
            {
                if (lootEntry != null)
                {
                    lootEntry.UpdatePrefabName();
                }
            }
        }
    }


    /// <summary>
    /// 上一次受到伤害的时间（秒）
    /// </summary>
    private float lastDamageTime = -999f;

    #endregion

    #region 动画相关参数

    [Header("受击动画设置")]
    public int flashCount = 1;

    [Min(0.01f)]
    public float flashDuration = 0.2f;

    public Color flashColor = new Color(1f, 0f, 0f, 1f);
    public float shakeDuration = 0.15f;
    public float shakeMagnitude = 0.1f;

    private bool isFlashing = false;

    public GameObject PanelPrefab;
    [ReadOnly]
    public GameObject PanleInstance;

    public UltEvent DataUpdate = new UltEvent();

    public UI_FloatData_Text UIValues;
    public bool ShowCanvas
    {
        get => Data.ShowCanvas;
        set
        {
            if (Data.ShowCanvas != value)
            {
                Data.ShowCanvas = value;

                // 根据值调用对应的面板函数
                if (Data.ShowCanvas)
                {
                    ShowPanle();
                }
                else
                {
                    HidePanle();
                }
            }
        }
    }

    #endregion

    #region 生命周期函数

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Hp;
        }

    }

    public override void Load()
    {
        modData.ReadData(ref Data);

        if(item.itemMods.ContainsKey_ID(ModText.Equipment))

        Equipment_Inventory = item.itemMods.GetMod_ByID(ModText.Equipment) as Mod_Inventory;

        if (Data.ShowCanvas)
        {
            ShowPanle();
        }
        else
        {
            HidePanle();
        }
    }

    [Button("显示面板")]
    public void ShowPanle()
    {
        if (PanleInstance != null) return;
        if (transform.gameObject.scene.IsValid() == false) return;//表示为Prefab状态，不显示面板
        GameObject panel = Instantiate(PanelPrefab,transform);
        UIValues = panel.GetComponentInChildren<UI_FloatData_Text>();
        panel.transform.position = transform.position + Vector3.up * 1f;
        PanleInstance = panel;
        DataUpdate += RefreshUI;

        RefreshUI();
        Data.ShowCanvas = true;


        // ✅ 从 UI_Drag 中获取 rectTransform 并恢复位置
        var s = panel.GetComponentInChildren<UI_Drag>();
        if (s != null)
        {
           // s.rectTransform.anchoredPosition = Data.PanelPosition;
        }
    }

    [Button("刷新面板")]
    public void RefreshUI()
    {
        UIValues.UpdateText("血量", Hp, MaxHp.Value);
    }



    [Button("隐藏面板")]
    public void HidePanle()
    {
        if (PanleInstance == null) return;
        if (transform.gameObject.scene.IsValid() == false) return;//表示为Prefab状态，不显示面板

        // ✅ 从 UI_Drag 中获取 rectTransform 并保存位置
        var s = PanleInstance.GetComponentInChildren<UI_Drag>();
        if (s != null)
        {
        //    Data.PanelPosition = s.rectTransform.anchoredPosition;
        }

        Destroy(PanleInstance);
        DataUpdate -= RefreshUI;
        UIValues = null;
        Data.ShowCanvas = false;
    }

    [Button]
    public override void Save()
    {
        modData.WriteData(Data);
        item.itemData.ModuleDataDic[_Data.Name] = modData;
    }

    public virtual float Hurt(float damage, Item attacker)
    {
        if (Hp <= 0) return -1;

        // ⏱️ 受伤间隔判断
        if (Time.time - lastDamageTime < Data.DamageInterval)
        {
            return -1;
        }
        lastDamageTime = Time.time;

        Hp -= damage * (1 - (Data.Defense.Value) * 0.01f);

        // 攻击者记录
        if(attacker!= null)
        Data.AttackersUIDs.Add(attacker.itemData.Guid);

        if (Data.AttackersUIDs.Count > 3)
            Data.AttackersUIDs.RemoveAt(0);

        if (Data.ShowCanvas)
            RefreshUI();

        ApplyDurabilityDamageToEquipments(1);

        // UI & 特效处理
        OnAction.Invoke(Hp);

        if (item.Sprite != null && !isFlashing)
        {
            Hit_Flash(item.Sprite);
            StartCoroutine(ShakeSprite(item.Sprite.transform));
        }

        if (Hp <= 0)
        {
            OnDead.Invoke();
            
            // TODO添加战利品掉落逻辑
            DropLoot();
            
            if (Data.DestroyDelay >= 0)
            {
                Destroy(item.gameObject, Data.DestroyDelay);
            }
            return 0; // Ensure a return value for this path
        }

        return Hp; // Ensure a return value for other paths
    }
    public virtual float ForceHurt(float damage, Item attacker)
    {
        if (Hp <= 0) return -1;

        Hp -= damage;

        if (Data.ShowCanvas)
            RefreshUI();

        // UI & 特效处理
        OnAction.Invoke(Hp);

        if (item.Sprite != null && !isFlashing)
        {
            Hit_Flash(item.Sprite);
            StartCoroutine(ShakeSprite(item.Sprite.transform));
        }

        if (Hp <= 0)
        {
            OnDead.Invoke();
            
            // TODO添加战利品掉落逻辑
            DropLoot();
            
            if (Data.DestroyDelay >= 0)
            {
                Destroy(item.gameObject, Data.DestroyDelay);
            }
            return 0; // Ensure a return value for this path
        }

        return Hp; // Ensure a return value for other paths
    }



 public virtual float Heal(float healAmount, Item healer)
{
    Hp = Mathf.Min(Hp + healAmount, MaxHp.Value);
    RefreshUI();
    return Hp;
}
    #endregion

    public Mod_Inventory Equipment_Inventory;

    /// <summary>
    /// 所有装备模块（Tag为"Equipment"）的耐久度下降指定数值，如果耐久为0则移除该装备
    /// </summary>
    /// <param name="amount">耐久下降的数值</param>
    protected virtual void ApplyDurabilityDamageToEquipments(int amount = 1)
    {
        if (Equipment_Inventory == null)
        {
            return;
        }

        foreach (var mod in Equipment_Inventory.inventory.Data.itemSlots)
        {
            if (mod.itemData == null) continue;

            if (mod.itemData.ItemTags.HasTypeTag(Tag.Armor))
            {
                mod.itemData.Durability -= amount;

                if (mod.itemData.Durability <= 0)
                {
                    // 耐久为0，清空该格子
                    mod.ClearData();
                    mod.RefreshUI();
                }
                else
                {
                    // 否则确保耐久不为负
                    mod.itemData.Durability = Mathf.Max(0, mod.itemData.Durability);
                }
            }
        }
    }

   // TODO实现战利品掉落方法
/// <summary>
/// 根据战利品表掉落物品
/// </summary>
protected virtual void DropLoot()
{
    // 检查是否有战利品表
    if (Data.LootTable == null || Data.LootTable.Count == 0)
        return;

    // 遍历战利品表
    foreach (var lootEntry in Data.LootTable)
    {
        // 检查预制体名称是否存在
        if (string.IsNullOrEmpty(lootEntry.LootPrefabName))
            continue;

        // 根据掉落概率决定是否掉落
        if (Random.value > lootEntry.DropChance)
            continue;

        // 确定掉落数量（在MinAmount和MaxAmount之间）
        int dropAmount = Random.Range(lootEntry.MinAmount, lootEntry.MaxAmount + 1);
        
        // 如果数量为0，跳过
        if (dropAmount <= 0)
            continue;

        // 使用自带的实例化方法创建战利品
        for (int i = 0; i < dropAmount; i++)
        {
            // 使用ItemMgr的实例化方法确保一致性
            Item lootItem = ItemMgr.Instance.InstantiateItem(
                lootEntry.LootPrefabName,this.transform.position);
                lootItem.DropInRange();
            // 确保战利品可以被拾取
            if (lootItem != null && lootItem.itemData != null)
            {
                lootItem.itemData.Stack.CanBePickedUp = true;
                lootItem.Load(); // 确保物品正确加载
            }
        }
    }
}

    #region 动画效果实现

    public void Hit_Flash(SpriteRenderer spriteRenderer)
    {
        StartCoroutine(FlashCoroutine(spriteRenderer));
    }

    private IEnumerator FlashCoroutine(SpriteRenderer spriteRenderer)
    {
        isFlashing = true;
        Color originalColor = spriteRenderer.material.color;

        for (int i = 0; i < flashCount; i++)
        {
            yield return StartCoroutine(LerpColor(spriteRenderer, originalColor, flashColor, flashDuration * 0.5f));
            yield return StartCoroutine(LerpColor(spriteRenderer, flashColor, originalColor, flashDuration * 0.5f));
        }

        isFlashing = false;
    }

    private IEnumerator LerpColor(SpriteRenderer spriteRenderer, Color fromColor, Color toColor, float duration)
    {
        float time = 0f;
        while (time < duration)
        {
            spriteRenderer.material.color = Color.Lerp(fromColor, toColor, time / duration);
            time += Time.deltaTime;
            yield return null;
        }
        spriteRenderer.material.color = toColor;
    }

    private IEnumerator ShakeSprite(Transform spriteTransform)
    {
        Vector3 originalPos = spriteTransform.localPosition;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            float x = Random.Range(-1f, 1f) * shakeMagnitude;
            float y = Random.Range(-1f, 1f) * shakeMagnitude;

            spriteTransform.localPosition = originalPos + new Vector3(x, y, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        spriteTransform.localPosition = originalPos;
    }

    #endregion
}