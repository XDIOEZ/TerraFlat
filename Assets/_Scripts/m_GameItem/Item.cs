using UnityEngine;
using System;
using UltEvents;
using System.Reflection;
using NUnit.Framework.Interfaces;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using System.Linq;








#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
#endif

public abstract class Item : MonoBehaviour
{
    public abstract ItemData Item_Data { get; set; }

    [ShowInInspector]
    public Dictionary<string, Module> Mods { get; set; } = new Dictionary<string, Module>();

    #region RunTime

    [Tooltip("����Ʒ����˭?")]
    public Item BelongItem;
    [Tooltip("������ʱ�������¼�")]
    public UltEvent OnStopWork_Event = new();
    public UltEvent UpdatedUI_Event = new();
    public UltEvent DestroyItem_Event = new();
    public UltEvent OnAction_Event = new();
    //��Ϸ����ͼ����
    public SpriteRenderer Sprite;
    #endregion

    public void Start()
    {
        // ��ȡ������Ӷ���� SpriteRenderer
        if (Sprite == null)
            Sprite = GetComponentInChildren<SpriteRenderer>();

        // ��ȡ��ǰ���е�ģ�飨�����Լ��������Ӷ���
        var existingModules = GetComponentsInChildren<Module>(true).ToList();

        // ���ڲ�������ģ���Ƿ����ĳ������
        var existingModuleNames = new HashSet<string>(existingModules.Select(m => m.Data?.Name));

        // ����Ƿ�ȱ��ģ�飬ȱ�˾Ͳ���
        foreach (var modData in Item_Data.ModuleDataDic.Values)
        {
            if (!existingModuleNames.Contains(modData.Name))
            {
                // ���ģ�飨ADDModTOItem ���Զ���ģ����Ϊ�Ӷ�����ص���ǰ��Ʒ�ϣ�
                Module.ADDModTOItem(this, modData.Name);
            }
        }

        // �ٴλ�ȡ��ȫ���ģ���б�
        existingModules = GetComponentsInChildren<Module>(true).ToList();

        // ��ʼ����ע������ģ��
        foreach (var mod in existingModules)
        {
            if (mod == null || mod.Data == null) continue;

            if (!Mods.ContainsKey(mod.Data.Name))
            {
                mod.ModuleInit(this); // ��ʼ��ģ�飬����������Ʒ
                Mods[mod.Data.Name] = mod; // ��ӵ��ֵ�
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
