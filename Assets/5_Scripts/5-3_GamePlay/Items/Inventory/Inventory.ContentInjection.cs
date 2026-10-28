using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections.Generic;

// 库存内容注入。
public partial class Inventory
{
    #region 库存内容注入
    /// <summary>
    /// 随机顺序自动注入物品列表到容器中
    /// </summary>
    public void RandomOrderAutoInjectItemDataList(List<GameObject> prefabList, List<int> countList)
    {
        if (prefabList == null || countList == null) return;
        if (prefabList.Count != countList.Count) return;

        // --- Step1: 打乱物品顺序 ---
        List<int> itemIndices = new List<int>();
        for (int i = 0; i < prefabList.Count; i++)
        {
            itemIndices.Add(i);
        }

        for (int i = itemIndices.Count - 1; i > 0; i--)
        {
            int r = UnityEngine.Random.Range(0, i + 1);
            int temp = itemIndices[i];
            itemIndices[i] = itemIndices[r];
            itemIndices[r] = temp;
        }

        // --- Step2: 收集所有空槽位并打乱 ---
        List<int> emptySlots = new List<int>();
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            if (Data.itemSlots[i].itemData == null)
                emptySlots.Add(i);
        }

        for (int i = emptySlots.Count - 1; i > 0; i--)
        {
            int r = UnityEngine.Random.Range(0, i + 1);
            int temp = emptySlots[i];
            emptySlots[i] = emptySlots[r];
            emptySlots[r] = temp;
        }

        // --- Step3: 按随机顺序把物品塞进随机槽位 ---
        int successCount = 0;
        int failCount = 0;

        for (int i = 0; i < itemIndices.Count && i < emptySlots.Count; i++)
        {
            int randomItemIndex = itemIndices[i];
            int slotIndex = emptySlots[i];

            GameObject prefab = prefabList[randomItemIndex];
            int count = countList[randomItemIndex];

            if (prefab == null || count <= 0) { failCount++; continue; }

            var itemComp = prefab.GetComponent<Item>();
            if (itemComp == null) { failCount++; continue; }

            string itemId = itemComp.itemData?.IDName ?? prefab.name;
            ItemData itemData = GameRes.Instance?.CreateItemData(itemId) ?? itemComp.Get_NewItemData();
            if (itemData == null) { failCount++; continue; }

            itemData.Stack.Amount = count;
            itemData.Stack.CanBePickedUp = false;

            Data.SetOne_ItemData(slotIndex, itemData);
            Data.Event_RefreshUI.Invoke(slotIndex);

            successCount++;
        }

        Debug.Log($"随机注入完成：成功 {successCount}，失败 {failCount}");
    }

    /// <summary>
    /// 自动注入物品列表到容器中，智能查找空槽位或可堆叠槽位，避免覆盖已有物品
    /// </summary>
    /// <param name="prefabList">物品预制体列表</param>
    /// <param name="countList">对应物品数量列表</param>
    [Button("自动注入物品列表")]
    public void AutoInjectItemDataList(
        [LabelText("物品预制体列表")] List<GameObject> prefabList,
        [LabelText("数量列表")] List<int> countList)
    {
        // 参数验证
        if (prefabList == null || countList == null)
        {
            Debug.LogError("自动注入失败：Prefab列表或数量列表不能为空");
            return;
        }

        if (prefabList.Count != countList.Count)
        {
            Debug.LogError($"自动注入失败：Prefab列表数量({prefabList.Count})与数量列表数量({countList.Count})不匹配");
            return;
        }

        if (prefabList.Count == 0)
        {
            Debug.LogWarning("自动注入失败：Prefab列表为空");
            return;
        }

        int successCount = 0;
        int failCount = 0;

        // 遍历并自动注入每个物品
        for (int i = 0; i < prefabList.Count; i++)
        {
            GameObject prefab = prefabList[i];
            int count = countList[i];

            if (prefab == null)
            {
                Debug.LogWarning($"跳过空的Prefab（索引 {i}）");
                failCount++;
                continue;
            }

            if (count <= 0)
            {
                Debug.LogWarning($"跳过无效数量 {count} 的物品 {prefab.name}（索引 {i}）");
                failCount++;
                continue;
            }

            // 获取Prefab上的Item组件
            Item itemComponent = prefab.GetComponent<Item>();
            if (itemComponent == null)
            {
                Debug.LogError($"自动注入失败：Prefab {prefab.name} 上找不到Item组件（索引 {i}）");
                failCount++;
                continue;
            }

            // 克隆ItemData
            string itemId = itemComponent.itemData?.IDName ?? prefab.name;
            ItemData itemData = GameRes.Instance?.CreateItemData(itemId) ?? itemComponent.Get_NewItemData();
            if (itemData == null)
            {
                Debug.LogError($"自动注入失败：无法克隆 {prefab.name} 的ItemData（索引 {i}）");
                failCount++;
                continue;
            }

            // 设置数量
            itemData.Stack.Amount = count;
            itemData.Stack.CanBePickedUp = false;

            // 尝试添加物品
            if (Data.TryAddItem(itemData, true))
            {
                Debug.Log($"成功自动注入物品 {prefab.name} x{count}");
                successCount++;
            }
            else
            {
                Debug.LogError($"自动注入失败：容器空间不足，无法注入物品 {prefab.name} x{count}");
                failCount++;
            }
        }

        Debug.Log($"自动注入物品列表完成：成功 {successCount} 个，失败 {failCount} 个");
    }

    // 重载方法：支持统一数量
    [Button("自动注入物品列表(统一数量)")]
    public void AutoInjectItemDataList(
        [LabelText("物品预制体列表")] List<GameObject> prefabList,
        [LabelText("统一数量"), MinValue(1)] int uniformCount = 1)
    {
        if (prefabList == null)
        {
            Debug.LogError("自动注入失败：Prefab列表不能为空");
            return;
        }

        // 创建统一数量列表
        List<int> countList = new List<int>();
        for (int i = 0; i < prefabList.Count; i++)
        {
            countList.Add(uniformCount);
        }

        AutoInjectItemDataList(prefabList, countList);
    }
    #endregion
}
