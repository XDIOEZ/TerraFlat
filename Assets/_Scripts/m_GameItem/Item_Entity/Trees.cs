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
        int pendingTasks = 0; // 计数器，跟踪未完成的异步加载任务

        foreach (var dropItem in dropItemList)
        {
            if (!string.IsNullOrEmpty(dropItem.itemName))
            {
                pendingTasks++; // 每个异步加载任务 +1

                XDTool.InstantiateAddressableAsync(dropItem.itemName, transform.position, transform.rotation, (go) =>
                {
                    if (go != null)
                    {
                        go.GetComponent<Item>().Item_Data.Stack.Amount = dropItem.amount;
                    }

                    pendingTasks--; // 任务完成 -1
                    TryDestroy();   // 尝试销毁
                });
            }
        }

        TryDestroy(); // 处理没有异步任务的情况

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
