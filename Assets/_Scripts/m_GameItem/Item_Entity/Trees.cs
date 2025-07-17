using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Trees : Entity
{
    public void GrowUp()
    {

    }
/*    public override Item_Data GetData()
    {
        throw new System.NotImplementedException();
    }

    public override void SetData(Item_Data data)
    {
        throw new System.NotImplementedException();
    }*/

    public override void Act()
    {
        throw new System.NotImplementedException();
    }



    // Update is called once per frame
    void Update()
    {
        
    }

    public virtual void DropItemByList(List<DropItem> dropItemList)
    {
        int pendingTasks = 0; // ������������δ��ɵ��첽��������

        foreach (var dropItem in dropItemList)
        {
            if (!string.IsNullOrEmpty(dropItem.itemName))
            {
                pendingTasks++; // ÿ���첽�������� +1

                XDTool.InstantiateAddressableAsync(dropItem.itemName, transform.position, transform.rotation, (go) =>
                {
                    if (go != null)
                    {
                        go.GetComponent<Item>().Item_Data.Stack.Amount = dropItem.amount;
                    }

                    pendingTasks--; // ������� -1
                    TryDestroy();   // ��������
                });
            }
        }

        TryDestroy(); // ����û���첽��������

        void TryDestroy()
        {
            if (pendingTasks <= 0)
            {
                Destroy(gameObject);
            }
        }
    }



    public override void Die()
    {

    }
}
