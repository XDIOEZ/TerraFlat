using UnityEngine;
using System;
using UltEvents;
using System.Reflection;
using NUnit.Framework.Interfaces;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using System.Linq;
using Force.DeepCloner;
using FastCloner.Code;
using NUnit;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
#endif

/// <summary>
/// 物品基类，游戏中所有物品的抽象基类
/// 负责物品的基本功能、模块管理和生命周期
/// </summary>
public abstract class Item : MonoBehaviour
{
    #region 核心属性

    /// <summary>
    /// 物品数据，由子类实现具体获取和设置逻辑
    /// </summary>
    public abstract ItemData itemData { get; set; }

    /// <summary>
    /// 物品模块管理器，管理物品上的所有模块
    /// </summary>
    [FastClonerIgnore]
    [ShowInInspector]
    public ItemMods itemMods = new ItemMods();

    /// <summary>
    /// 模块字典，通过名称访问各个模块
    /// </summary>
    [FastClonerIgnore]
    public Dictionary<string, Module> Mods { get => itemMods.Mods; set => itemMods.Mods = value; }

    #endregion

    #region 运行时属性

    [Tooltip("此物品属于谁?")]
    public Item Owner;
    [Tooltip("此物品是否在手上?")]
    public bool InHand = false;

    [HideInInspector]
    /// <summary>
    /// 物品UI更新事件
    /// </summary>
    public UltEvent OnUIRefresh = new();

    [HideInInspector]
    /// <summary>
    /// 物品被销毁时触发的事件
    /// </summary>
    public UltEvent<Item> OnItemDestroy = new();

    [HideInInspector]
    /// <summary>
    /// 物品被激活时触发的事件
    /// </summary>
    public UltEvent OnAct = new();

    [HideInInspector]
    /// <summary>
    /// 游戏的贴图对象
    /// </summary>
    public SpriteRenderer Sprite;

    private bool isInitialized = false;
    [Tooltip("物品初始化时触发的事件，用于根据环境因素初始化物品")]
    public UltEvent<EnvironmentFactors> OnInit_Env = new();

    [Tooltip("物品耐久度改变时触发的事件，参数为当前耐久度")]
    public UltEvent<float> OnDurabilityModified = new();
    #endregion

    #region 生命周期方法

    /// <summary>
    /// 加载物品数据和模块
    /// </summary>
    [Button("加载模块")]
    public virtual void Load()
    {
        isInitialized = true;
        ModuleLoad();
    }


    /// <summary>
    /// 初始化物品组件和模块
    /// </summary>
    public virtual void Start()
    {
        // 获取或初始化精灵渲染器
        if (Sprite == null)
            Sprite = GetComponentInChildren<SpriteRenderer>();

        // // 初始化新物品（如果没有Guid）
        // if (itemData.Guid == 0)
        // {
        //     // 自动生成Guid
        //     itemData.Guid = Guid.NewGuid().GetHashCode();
        //     var mods = GetComponentsInChildren<Module>(true).ToList();
        //     foreach (var mod in mods)
        //     {
        //         mod.Awake();
        //     }
        //     Load();
        //     ChunkMgr.Instance.UpdateItem_ChunkOwner(this);
        // }
    }

    /// <summary>
    /// 物品的更新逻辑，处理模块更新
    /// </summary>
    public void Update()
    {
        if (!isInitialized)
            return;

        // 高频更新逻辑（无间隔）
        if (updateInterval <= 0.1f)
        {
            foreach (Module mod in Mods.Values.ToList()) // 创建快照，避免在更新过程中修改集合导致的异常
            {
                mod.ModUpdate(Time.deltaTime);
            }
            return;
        }

        // 低频更新逻辑（有间隔）
        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;

            foreach (Module mod in Mods.Values.ToList()) // 创建快照
            {
                mod.ModUpdate(updateInterval);
            }
        }
    }

    /// <summary>
    /// 物品销毁时调用，触发事件并保存数据
    /// </summary>
    public void OnDestroy()
    {
        OnItemDestroy.Invoke(this);
        ModuleSave();
    }

    /// <summary>
    /// 编辑器验证逻辑
    /// </summary>
    public void OnValidate()
    {
        itemData.Tags.EnsureTagStructure();
    }

    public virtual void Initialize_Env(EnvironmentFactors env)
    {
        // 职责：由Item负责根据环境因素调整各模块的初始参数
        // 这符合单一职责原则：各模块只关心自己的功能实现，参数由Item统一管理
        // RandomMapGenerator → Item.Initialize_Env(env) → Modules.AdjustByEnvironment(env)

        if (itemData == null || Mods == null || Mods.Count == 0)
            return;

        // 遍历所有模块，调用其环境初始化方法（如果实现了的话）
        foreach (var mod in Mods.Values.ToList())
        {
            if (mod != null)
            {
                // 如果模块实现了IEnvironmentAdjustable接口，调用其环境调整方法
                if (mod is IEnvironmentAdjustable adjustable)
                {
                    adjustable.AdjustByEnvironment(env);
                }
            }
        }

        OnInit_Env.Invoke(env);
    }

    #endregion

    #region 数据管理

    /// <summary>
    /// 物品的更新频率，单位：秒
    /// </summary>
    public float updateInterval = 0f; // 每0.1秒执行一次

    /// <summary>
    /// 更新计时器
    /// </summary>
    float updateTimer = 0f;

    /// <summary>
    /// 创建新的物品数据
    /// 用于生成物品的模板数据
    /// </summary>
    /// <returns>新的物品数据实例</returns>
    public ItemData Get_NewItemData()
    {
        // 创建一个临时的游戏对象实例来处理初始化
        GameObject tempGO = null;
        tempGO = Instantiate(gameObject);
        tempGO.hideFlags = HideFlags.HideAndDontSave; // 隐藏临时对象

        Item tempItem = tempGO.GetComponent<Item>();
        if (tempItem == null)
        {
            Debug.LogError($"[Item] 无法创建 {gameObject.name} 的ItemData: 临时对象缺少Item组件");
            return null;
        }

        // 获取所有子对象的Module并初始化
        var modules = tempGO.GetComponentsInChildren<Module>(true).ToList();

        // 为每个模块调用Awake方法
        foreach (var mod in modules)
        {
            if (mod != null)
            {
                mod.Awake();
            }
        }

        // 生成新的Guid
        tempItem.itemData.Guid = Guid.NewGuid().GetHashCode();

        // 加载模块
        tempItem.Load();

        // 保存模块数据
        tempItem.Save();

        // 克隆最终的itemData作为返回值
        ItemData result = FastCloner.FastCloner.DeepClone(tempItem.itemData);

        // 销毁临时对象，确保不留下任何痕迹
        if (tempGO != null)
        {
            DestroyImmediate(tempGO);
        }

        return result;
    }

    #endregion

    #region 模块管理

    /// <summary>
    /// 加载并初始化模块（包括缺失补齐、初始化字典等）
    /// 可手动调用
    /// </summary>
    public void ModuleLoad()
    {
        bool firstStart = itemData.ModuleDataDic.Count == 0;

        var modules = GetComponentsInChildren<Module>().ToList();

        if (firstStart)//第一次启动
        {
            foreach (var mod in modules)
            {
                if (string.IsNullOrWhiteSpace(mod._Data.Name))
                    mod._Data.Name = Module.GenerateUniqueModName(mod._Data.ID);

                itemMods.AddMod(mod);
                itemData.ModuleDataDic[mod._Data.Name] = mod._Data;
            }

            // 所有模块加入Mods后统一初始化
            foreach (var mod in Mods.Values)
            {
                mod.ModuleInit(this, null);
            }
            foreach (var mod in Mods.Values)
            {
                mod.Load();
            }
        }

        if (!firstStart)//非第一次启动
        {
            ItemMods tempMods = new ItemMods();
            List<Module> modsToInit = new();

            foreach (var mod in modules)
            {
                tempMods.AddMod(mod);
            }


            //通过数据进行匹配修复
            foreach (ModuleData modData in itemData.ModuleDataDic.Values)
            {
                var modList = tempMods.GetModList_ByID(modData.ID);
                Module mod;//待安装数据模块引用

                //不存在模块
                if (modList == null || modList.Count == 0)
                {

                    Debug.LogWarning($"物品 {gameObject.name} 丢失了模块 {modData.Name} " +
                        $" ID: {modData.ID}，下面开始尝试自动修复。");

                    GameObject @object = GameRes.Instance.InstantiatePrefab(modData.ID);

                    @object.transform.SetParent(transform);

                    mod = @object.GetComponentInChildren<Module>();

                    mod._Data = modData;

                    itemMods.AddMod(mod);

                    modsToInit.Add(mod);//添加到待初始化列表
                }

                else//存在模块
                {
                    mod = modList[^1];

                    mod._Data = modData;

                    tempMods.RemoveMod(mod);

                    modsToInit.Add(mod);//添加到待初始化列表

                    itemMods.AddMod(mod);
                }
            }

            //收集未解决的模块 并添加修复
            if (tempMods.Mods_List.Count > 0)
            {
                foreach (var LostMod in tempMods.Mods_List.Values)
                {
                    foreach (var mod in LostMod)
                    {
                        if (string.IsNullOrWhiteSpace(mod._Data.Name))
                        {
                            Debug.LogWarning($"物品 {gameObject.name} 额外添加了模块 {mod._Data.Name} " +
                          $" ID: {mod._Data.ID}，已自动修复。");
                            mod._Data.Name = Module.GenerateUniqueModName(mod._Data.ID);
                            itemMods.AddMod(mod);
                            itemData.ModuleDataDic[mod._Data.Name] = mod._Data;
                            modsToInit.Add(mod);
                        }
                    }

                }
            }


            // 全部加入Mods后再统一初始化（防止初始化中找不到其他模块）
            foreach (var mod in modsToInit)
            {
                mod.ModuleInit(this, mod._Data);

            }
            foreach (var mod in modsToInit)
            {
                mod.Load();
            }
        }
    }

    /// <summary>
    /// 保存所有模块数据
    /// 使用副本遍历避免在保存过程中修改集合导致的异常
    /// </summary>
    public void ModuleSave()
    {
        // 创建Mods.Values的副本，使用ToList()生成新的列表
        var modsCopy = Mods.Values.ToList();

        // 遍历副本而非原始集合
        foreach (Module mod in modsCopy)
        {
            // 即使Save()过程中修改了原始Mods集合，也不会影响当前遍历
            mod.Save();
        }
    }

    #endregion

    #region 公共方法


    /// <summary>
    /// 加载物品位置数据
    /// </summary>
    public void LoadDataPosition()
    {
        transform.position = itemData.transform.position;
        transform.rotation = itemData.transform.rotation;
        transform.localScale = itemData.transform.scale;
    }

    /// <summary>
    /// 保存物品数据和模块
    /// </summary>
    [Button("保存模块")]
    public virtual void Save()
    {
        itemData.transform.position = transform.position;
        itemData.transform.rotation = transform.rotation;
        itemData.transform.scale = transform.localScale;
        ModuleSave();
    }

    /// <summary>
    /// 激活物品行为
    /// </summary>
    [Button("激活(Act)")]
    public virtual void Act()
    {
        Debug.Log("Item Act");
        OnAct.Invoke();
    }

    /// <summary>
    /// 同步物品数据
    /// </summary>
    [Sirenix.OdinInspector.Button("同步物品数据")]
    public virtual int SyncItemData()
    {
        if (itemData.IDName != gameObject.name)
        {
            itemData.IDName = this.gameObject.name;
            Debug.LogWarning("物品数据IDName为空，已自动设置。");
        }
        updateInterval = 0f;
        return itemData.SyncData();
    }

    /// <summary>
    /// 在范围内丢弃物品
    /// </summary>
    public void DropInRange()
    {
        Mod_BaseDroper.DropItemInARange(this, transform.position, UnityEngine.Random.Range(0.5f, 2f), 0.5f);
    }

    /// <summary>
    /// 销毁自身
    /// </summary>
    public void DestroySelf()
    {
        Destroy(gameObject);
    }

    /// <summary>
    /// 设置模块
    /// </summary>
    /// <param name="modName">模块名称</param>
    [Button]
    public void SetUpModeule(string modName)
    {
        Module.ADDModTOItem(this, modName).Load();
    }

    /// <summary>
    /// 减少耐久度
    /// </summary>
    /// <param name="amount">减少量</param>
    public void DecreaseDurability(int amount)
    {
        if (itemData.Durability > 0)
        {
            itemData.Durability -= amount;
            // 物品耐久为0时触发事件
            if (itemData.Durability <= 0)
            {
                itemData.Durability = 0;
                // 物品耐久为0时触发事件
                OnDurabilityModified?.Invoke(itemData.Durability);
                
            }
            else
            {
                // 物品耐久改变时触发事件
                OnDurabilityModified?.Invoke(itemData.Durability);
            }
        }
    }

    #endregion

    #region 编辑器方法

#if UNITY_EDITOR
    /// <summary>
    /// 初始化ItemData（编辑器上下文菜单）
    /// </summary>
    [ContextMenu("初始化ItemData")]
    private void SyncName()
    {
        itemData.IDName = this.gameObject.name;
        itemData.GameName = this.gameObject.name;

        itemData.Description = "";
        itemData.Description = itemData.ToString();
    }

    /// <summary>
    /// 构造函数（仅编辑器使用）
    /// </summary>
    public Item()
    {
        // 注意：Unity中不要在构造函数中初始化数据，使用Awake或Start代替
    }
#endif

    #endregion
}