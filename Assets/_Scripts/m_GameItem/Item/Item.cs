using UnityEngine;
using System;
using UltEvents;



#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
#endif

public abstract class Item : MonoBehaviour
{
    public abstract ItemData Item_Data { get; set; }

    public UltEvent UpdatedUI_Event = new();

    public void SetItemData(ItemData itemData)
    {
        Debug.LogWarning("设置物品数据：" + itemData.Name);
        Item_Data = itemData;
    }

    public void SyncPosition()
    {
        Item_Data._transform.Position = transform.position;    
        Item_Data._transform.Rotation = transform.rotation;    
        Item_Data._transform.Scale = transform.localScale;     
    }

    public abstract void Act();

    // 添加菜单按钮以同步名称
#if UNITY_EDITOR
    [ContextMenu("初始化ItemData")] // 修改为中文
    private void SyncName()
    {
        if (Item_Data != null)
        {
            Item_Data.Name = this.gameObject.name;
            Debug.Log($"游戏对象名称已同步至 {Item_Data.Name}");
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
        Item_Data.PrefabPath = prefabPath;
        Item_Data.ID = ++XDTool.ItemId;
        Item_Data.Guid = XDTool.NextGuid;
        Item_Data.Description = Item_Data.ToString();
    }
    public Item()
    {
     //   Item_Data.Guid = Guid.NewGuid().GetHashCode();
    }
     void Start()
    {
        Item_Data.Guid = Guid.NewGuid().GetHashCode();
    }
#endif
}