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
        Debug.LogWarning("������Ʒ���ݣ�" + itemData.Name);
        Item_Data = itemData;
    }

    public void SyncPosition()
    {
        Item_Data._transform.Position = transform.position;    
        Item_Data._transform.Rotation = transform.rotation;    
        Item_Data._transform.Scale = transform.localScale;     
    }

    public abstract void Act();

    // ��Ӳ˵���ť��ͬ������
#if UNITY_EDITOR
    [ContextMenu("��ʼ��ItemData")] // �޸�Ϊ����
    private void SyncName()
    {
        if (Item_Data != null)
        {
            Item_Data.Name = this.gameObject.name;
            Debug.Log($"��Ϸ����������ͬ���� {Item_Data.Name}");
        }
        else
        {
            Debug.LogWarning("��Ʒ����Ϊ�գ��޷�ͬ�����ơ�");
        }
        //TODO ��ȡ�����Prefab·�� ͨ��Addressable�ҵ���Ӧ����Դ,�޸�AddressableNameΪPrefab������
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(this.gameObject);
        if (!string.IsNullOrEmpty(prefabPath))
        {
            // ͨ��Addressable�ҵ���Ӧ����Դ�����޸�AddressableName
            AddressableAssetSettings settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(prefabPath));
            if (entry != null)
            {
                entry.SetAddress(this.gameObject.name);
                Debug.Log($"Addressable ��Դ�������޸�Ϊ {this.gameObject.name}");
            }
            else
            {
                Debug.LogError("δ�ҵ���Ӧ�� Addressable ��Դ��+�����Ƿ�����ӵ� Addressable �����С�");
            }
        }
        else
        {
            Debug.LogWarning("�޷���ȡ Prefab ·�������ܲ��� Prefab ʵ����");
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