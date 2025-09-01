using Meryel.UnityCodeAssist.YamlDotNet.Core;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
/// <summary>
/// 管理自己附属的Item
/// </summary>
public class Chunk : MonoBehaviour
{
    [ShowInInspector]
    public Dictionary<int, Item> RunTimeItems = new();
    [ShowInInspector]
    public Dictionary<string, List<Item>> RuntimeItemsGroup = new();
    public Map Map;
    public MapSave MapSave;
    public string ChunkOwner;


    public Chunk LoadChunk()
    {
        MapSave.items.ForEach(items =>
        {
            foreach(var itemData in items.Value)
            {
               Item item = GameItemManager.Instance.InstantiateItem(itemData,this.gameObject);
               item.Load();
                item.transform.position = itemData._transform.Position;
                item.transform.rotation = itemData._transform.Rotation;
                item.transform.localScale = itemData._transform.Scale;
                RunTimeItems.Add(item.itemData.Guid, item);
               AddToGroup(item);
            }
        });
        return this;
    }

    public void SaveChunk()
    { 
      //调用所有item的Save方法
        foreach (var item in RunTimeItems.Values)
        {
            if (item != null)
                item.Save();
        }
        SaveDataManager.Instance.SaveData.Active_MapsData_Dict[MapSave.MapName] = MapSave;
    }

    public void DestroyChunk()
    {
          Destroy(this.gameObject);
    }

    // ✅ 添加到分组s
    public void AddToGroup(Item item)
    {
        string key = item.itemData.IDName;
        if (!RuntimeItemsGroup.TryGetValue(key, out var list))
        {
            list = new List<Item>();
            RuntimeItemsGroup[key] = list;
        }
        list.Add(item);
    }

    /// <summary>
    /// 获取指定玩家或物体所在的区块坐标
    /// </summary>
    [Button]
    public static Vector2Int GetChunkPosition(Vector2 objPos)
    {//TODO 因为Transformpos 是在左下角 相对于绘制的中心来说 所以需要微调玩家的位置 来输出确切的区块坐标
        Vector2 chunkSize = SaveDataManager.Instance.SaveData.ChunkSize;

       Vector2Int chunkPos = new Vector2Int(
            Mathf.FloorToInt(objPos.x / chunkSize.x) * (int)chunkSize.x,
            Mathf.FloorToInt(objPos.y / chunkSize.y) * (int)chunkSize.y
        );
        return chunkPos;
    }

    public void AddItem(Item item)
    {
        RunTimeItems[item.itemData.Guid] = item;
        //TODO 设置为子对象
        item.transform.SetParent(this.transform);
        AddToGroup(item);
    }

    public void RemoveItem(Item item)
    {
        RunTimeItems.Remove(item.itemData.Guid);
        string key = item.itemData.IDName;
        if (RuntimeItemsGroup.TryGetValue(key, out var list))
        {
            list.Remove(item);
        }
    }

   public void DestroyItem(Item item)
    {
        RunTimeItems.TryGetValue(item.itemData.Guid, out var itemData);
        if (itemData!= null)
        {
            RemoveItem(item);
            Destroy(item.gameObject);
        }
        else
        {
            Debug.Log("Item not found in chunk");
            RunTimeItems.Remove(item.itemData.Guid);
        }
    }
    public void ClearNullItems()
    {
        var keysToRemove = RunTimeItems
            .Where(kvp => kvp.Value == null)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            RunTimeItems.Remove(key);
        }
    }

}
