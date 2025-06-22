using UnityEngine;
using System;
using UltEvents;
using System.Reflection;
using NUnit.Framework.Interfaces;





#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
#endif

public abstract class Item : MonoBehaviour
{
    public abstract ItemData Item_Data { get; set; }
    #region RunTime

    [Tooltip("此物品属于谁?")]
    public Item BelongItem;
    [Tooltip("被销毁时触发的事件")]
    public UltEvent OnStopWork_Event = new();
    #endregion

    public UltEvent UpdatedUI_Event = new();
    public UltEvent DestroyItem_Event = new();
    public UltEvent OnAction_Event = new();
    public void SyncPosition()
    {
        Item_Data._transform.Position = transform.position;    
        Item_Data._transform.Rotation = transform.rotation;    
        Item_Data._transform.Scale = transform.localScale;     
    }

    public virtual void Act()
    {
        Debug.Log("Item Act");
        OnAction_Event.Invoke();
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
