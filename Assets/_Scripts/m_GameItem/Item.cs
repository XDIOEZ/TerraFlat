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








#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
#endif
public abstract class Item : MonoBehaviour
{
    public abstract ItemData itemData { get; set; }

    [FastClonerIgnore]
    [ShowInInspector]
    public ItemMods itemMods = new ItemMods();

    [FastClonerIgnore]
    public Dictionary<string, Module> Mods { get=> itemMods.Mods; set => itemMods.Mods = value; }

    #region RunTime

    [Tooltip("此物品属于谁?")]
    public Item Owner;
    [HideInInspector]
    //物品UI更新事件
    public UltEvent OnUIRefresh = new();
    [HideInInspector]
    //物品被销毁时触发的事件
    public UltEvent<Item> OnItemDestroy = new();
    [HideInInspector]
    //物品被激活时触发的事件
    public UltEvent OnAct = new();
    [HideInInspector]

    //游戏的贴图对象
    public SpriteRenderer Sprite;
    #endregion

    public virtual void Start()
    {
        if (Sprite == null)
            Sprite = GetComponentInChildren<SpriteRenderer>();


        if(itemData.Guid == 0)
        {
            // 自动生成Guid
            itemData.Guid = Guid.NewGuid().GetHashCode();
            Load();
            ChunkMgr.Instance.UpdateItem_ChunkOwner(this);
        }
    }


public ItemData IsPrefabInit()
{
    // 创建一个临时的游戏对象实例来处理初始化
    GameObject tempGO = Instantiate(gameObject);
    tempGO.hideFlags = HideFlags.HideAndDontSave; // 隐藏临时对象
    
    try
    {
        Item tempItem = tempGO.GetComponent<Item>();
        
        // 获取所有子对象的Module并初始化
        var modules = tempGO.GetComponentsInChildren<Module>(true).ToList();
        
        // 为每个模块调用Awake方法
        foreach (var mod in modules)
        {
            mod.Awake();
        }
        
        // 生成新的Guid
        tempItem.itemData.Guid = Guid.NewGuid().GetHashCode();
        
        // 加载模块
        tempItem.Load();
        
        // 保存模块数据
        tempItem.Save();
        
        // 克隆最终的itemData作为返回值
        ItemData result = tempItem.itemData.DeepClone();
        
        return result;
    }
    finally
    {
        // 销毁临时对象，确保不留下任何痕迹
        if (tempGO != null)
        {
            DestroyImmediate(tempGO);
        }
    }
}

    [Button]
    public void SetUpModeule(string modName)
    {
        Module.ADDModTOItem(this, modName).Load();

     /*   foreach(var mod in Mods.Values)
        {
            mod.Load();
        }*/
    }

    [Tooltip("物品的更新频率，单位：秒")]
    public float updateInterval = 0f; // 每0.1秒执行一次
    float updateTimer = 0f;
    public void Update()
    {
        if (updateInterval <= 0.1f)
        {
            foreach (Module mod in Mods.Values.ToList()) // 创建快照
            {
                mod.ModUpdate(Time.deltaTime);
            }
            return;
        }

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
    /// 加载并初始化模块（包括缺失补齐、初始化字典等）
    /// 可手动调用
    /// </summary>
    public void ModuleLoad()
    {
        bool firstStart = itemData.ModuleDataDic.Count == 0;

        var modules = GetComponentsInChildren<Module>(true).ToList();


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
        
        if(!firstStart)//非第一次启动
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
                        $" ID: {modData.ID}，已自动修复。");

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
                    foreach(var mod in LostMod)
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


    public void OnDestroy()
    {
        OnItemDestroy.Invoke(this);
        ModuleSave();
    }

    [Button("加载模块")]
    public virtual void Load()
    {
        ModuleLoad();
    }
    public void LoadDataPosition()
    {
        transform.position = itemData._transform.Position;
        transform.rotation = itemData._transform.Rotation;
        transform.localScale = itemData._transform.Scale;
    }
    [Button("保存模块")]
    public virtual void Save()
    {
        itemData._transform.Position = transform.position;
        itemData._transform.Rotation = transform.rotation;
        itemData._transform.Scale = transform.localScale;
        ModuleSave();
    }

    public virtual void Act()
    {
        Debug.Log("Item Act");
        OnAct.Invoke();
    }

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

    // 添加菜单按钮以同步名称
#if UNITY_EDITOR
    [ContextMenu("初始化ItemData")] // 修改为中文
    private void SyncName()
    {
        itemData.IDName = this.gameObject.name;
        itemData.GameName = this.gameObject.name;

        itemData.Description = "";
        itemData.Description = itemData.ToString();
    }
    public Item()
    {
     //   Item_Data.Guid = Guid.NewGuid().GetHashCode();
    }
#endif

    public void OnValidate()
    {
        itemData.Tags.EnsureTagStructure();
    }

    public void DropInRange()
    {
        Mod_ItemDroper.DropItemInARange(this, transform.position, UnityEngine.Random.Range(0.5f, 2f), 0.5f);
    }
}
