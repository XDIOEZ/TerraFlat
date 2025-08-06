using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Coal : Item,IFuel
{
    public Data_GeneralItem _coalData;
    public override ItemData itemData { get => _coalData; set => _coalData = value as Data_GeneralItem; }

      #region ȼ�����
    public float MaxBurnTime { get => maxBurnTime; set => maxBurnTime = value; }
    public float MaxTemptrue { get => maxTemptrue; set => maxTemptrue = value; }
    [Header("�����л��ֶ�")]
    [SerializeField]
    private float maxBurnTime = 32;
    [SerializeField]
    private float maxTemptrue = 300;

    public override int SyncItemData()
    {
        if (itemData.IDName == "")
        {
            itemData.IDName = this.gameObject.name;
            Debug.LogWarning("��Ʒ����IDNameΪ�գ����Զ����á�");
        }

        int i = base.SyncItemData();

        IFuel fuel = this as IFuel;
        fuel.SyncFuelData(i);

        return i;
    }
    #endregion

    public override void Act()
    {
        Debug.Log("Coal��ʹ����");
        //get father name 
        string fatherName = transform.parent.gameObject.name;
        //debug father name to debug
        Debug.Log(fatherName);
    }
}
