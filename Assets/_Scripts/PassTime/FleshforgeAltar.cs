using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FleshForgeAltar : Organ,IFun_FleshForgeAltar
{
    // 祭品列表
    public List<Item> Sacrifice;
    // 熔炉的最大容量
    public int MaxCapacity = 2;
    // 当祭品列表已满时，新的祭品将被添加到这个列表中
    public List<Item> trashBin = new List<Item>();

    // 初始化熔炉
    public void Start()
    {
        // 初始化祭品列表，设置熔炉的最大容量
        Sacrifice = new List<Item>(MaxCapacity);
    }

    // 添加祭品到熔炉
public void PushSacrifice(Item item)
{
    // 检测 Item 是否为空
    if (item == null)
    {
        Debug.LogWarning("无法添加空的祭品到熔炉。");
        return; // 如果 item 为空，则不进行任何处理
    }

    Debug.Log("添加祭品到熔炉");
    // todo 判断熔炉是否已满
    if (Sacrifice.Count >= MaxCapacity)
    {
        // 将 Sacrifice 的最后一个元素移到 trashBin 中
        trashBin.Add(Sacrifice[Sacrifice.Count - 1]);
        Sacrifice.RemoveAt(Sacrifice.Count - 1);
        // 将新的祭品添加到 Sacrifice 中
        Sacrifice.Add(item);
    }
    else
    {
        Sacrifice.Add(item);
    }
}


    public bool Composite()
    {
        // 检查熔炉是否有物品可以合成
        if (Sacrifice.Count > 0)
        {
            // 将熔炉中的所有物品移动到垃圾桶中
            foreach (Item item in Sacrifice)
            {
                trashBin.Add(item);
            }
            // 清空熔炉
            Sacrifice.Clear();

            Debug.Log("熔炉中的物品已合成并移至垃圾桶。");
            return true;
        }
        else
        {
            Debug.LogWarning("熔炉为空，无法合成。");
            return false;
        }
    }
}
public interface IFun_FleshForgeAltar
{
    void PushSacrifice(Item item); // 祭品增加

    public bool Composite(); // 合成

}
