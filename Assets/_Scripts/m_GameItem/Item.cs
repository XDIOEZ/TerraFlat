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
    public abstract ItemData Item_Data { get; set; }

    [ShowInInspector]
    [FastClonerIgnore]
    public Dictionary<string, Module> Mods { get; set; } = new Dictionary<string, Module>();

    #region RunTime

    [Tooltip("此物品属于谁?")]
    public Item BelongItem;
    [Tooltip("被销毁时触发的事件")]
    public UltEvent OnStopWork_Event = new();
    public UltEvent UpdatedUI_Event = new();
    public UltEvent DestroyItem_Event = new();
    public UltEvent OnAct = new();
    //游戏的贴图对象
    public SpriteRenderer Sprite;
    #endregion

    public void Start()
    {
        if (Sprite == null)
            Sprite = GetComponentInChildren<SpriteRenderer>();

        ModuleLoad();
    }

    /// <summary>
    /// 加载并初始化模块（包括缺失补齐、初始化字典等）
    /// 可手动调用
    /// </summary>
    public void ModuleLoad()
    {
        //TODO 检查是否是第一次启动 检查ModData是否为空
        bool firstStart = false;

        if(Item_Data.ModuleDataDic.Count == 0)
        {
            // 第一次启动
            firstStart = true;
        }

        //第一次启动
        if (firstStart == true)
        {
            var Modules = GetComponentsInChildren<Module>(true).ToList();
            foreach (var mod in Modules)
            {
                Mods[mod.Data.Name] = mod; // 添加到字典
                Item_Data.ModuleDataDic[mod.Data.Name] = mod.Data; // 添加到数据字典
            }
            foreach(var mod in Mods.Values)
            {
                mod.ModuleInit(this, null); // 初始化模块，传入宿主物品
            }
        }
        //不是第一次启动
        if(firstStart == false)
        {
            var Modules = GetComponentsInChildren<Module>(true).ToList();

            foreach (var mod in Modules)
            {
                Mods[mod.Data.Name] = mod; // 添加到字典
            }

            foreach (ModuleData modData in Item_Data.ModuleDataDic.Values)
            {
                //检查Mods中是否有该模块
                if (Mods.ContainsKey(modData.Name) == true)
                { 
                    //如果有，则更新数据
                    Mods[modData.Name].ModuleInit(this, modData); // 初始化模块，传入宿主物品
                }
                else
                {
                    //如果没有，则创建模块
                    Module mod = Module.ADDModTOItem(this, modData);
                    mod.ModuleInit(this, modData); // 初始化模块，传入宿主物品
                }
            }
        }

    }

    public void SyncPosition()
    {
        Item_Data._transform.Position = transform.position;    
        Item_Data._transform.Rotation = transform.rotation;    
        Item_Data._transform.Scale = transform.localScale;     
    }

    public virtual void Act()
    {
        Debug.Log("Item Act");
        OnAct.Invoke();
    }

    [Sirenix.OdinInspector.Button("同步物品数据")]
    public virtual int SyncItemData()
    {
        if (Item_Data.IDName != gameObject.name)
        {
            Item_Data.IDName = this.gameObject.name;
            Debug.LogWarning("物品数据IDName为空，已自动设置。");
        }
        return Item_Data.SyncData();
    }

    // 添加菜单按钮以同步名称
#if UNITY_EDITOR
    [ContextMenu("初始化ItemData")] // 修改为中文
    private void SyncName()
    {
        if (Item_Data != null)
        {
            Item_Data.IDName = this.gameObject.name;
            Debug.Log($"游戏对象名称已同步至 {Item_Data.IDName}");
        }
        else
        {
            Debug.LogWarning("物品数据为空，无法同步名称。");
        }
        //TODO 获取物体的Prefab路径 通过Addressable找到对应的资源,修改AddressableName为Prefab的名称
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(this.gameObject);
        if (!string.IsNullOrEmpty(prefabPath))
        {
            // 通过Addressable找到对应的资源，并修改AddressableName
            AddressableAssetSettings settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(prefabPath));
            if (entry != null)
            {
                entry.SetAddress(this.gameObject.name);
                Debug.Log($"Addressable 资源名称已修改为 {this.gameObject.name}");
            }
            else
            {
                Debug.LogError("未找到对应的 Addressable 资源。+请检查是否已添加到 Addressable 设置中。");
            }
        }
        else
        {
            Debug.LogWarning("无法获取 Prefab 路径，可能不是 Prefab 实例。");
        }
        Item_Data.Description = Item_Data.ToString();
    }
    public Item()
    {
     //   Item_Data.Guid = Guid.NewGuid().GetHashCode();
    }
#endif
}
