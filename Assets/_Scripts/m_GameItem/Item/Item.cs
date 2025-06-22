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

    [Tooltip("����Ʒ����˭?")]
    public Item BelongItem;
    [Tooltip("������ʱ�������¼�")]
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

    [Sirenix.OdinInspector.Button("ͬ����Ʒ����")]
    public virtual int SyncItemData()
    {
        if (Item_Data.IDName != gameObject.name)
        {
            Item_Data.IDName = this.gameObject.name;
            Debug.LogWarning("��Ʒ����IDNameΪ�գ����Զ����á�");
        }
        return Item_Data.SyncData();
    }

    // ��Ӳ˵���ť��ͬ������
#if UNITY_EDITOR
    [ContextMenu("��ʼ��ItemData")] // �޸�Ϊ����
    private void SyncName()
    {
        if (Item_Data != null)
        {
            Item_Data.IDName = this.gameObject.name;
            Debug.Log($"��Ϸ����������ͬ���� {Item_Data.IDName}");
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
        Item_Data.Description = Item_Data.ToString();
    }
    public Item()
    {
     //   Item_Data.Guid = Guid.NewGuid().GetHashCode();
    }
#endif
}
