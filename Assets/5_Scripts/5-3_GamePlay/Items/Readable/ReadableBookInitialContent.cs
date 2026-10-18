using System;
using System.Collections.Generic;

/// <summary>为新书的数据载荷写入独立初始内容，不需要生成书籍世界对象或消耗书写材料。</summary>
internal static class ReadableBookInitialContent
{
    #region 初始书页快照

    internal static bool TryWrite(Ex_ModData_MemoryPackable target, int basePageCount,
        IReadOnlyList<ReadableBookPageContent> pages, out string failureReason, ItemData writingMaterial = null)
    {
        if (target == null || basePageCount <= 0 || pages == null || pages.Count == 0)
        {
            failureReason = "读物缺少有效书页或初始内容。";
            return false;
        }

        var copiedPages = new List<ReadableBookPageContent>(pages.Count);
        for (int index = 0; index < pages.Count; index++)
        {
            ReadableBookPageContent page = pages[index];
            if (page == null)
            {
                failureReason = $"读物第 {index + 1} 页内容为空。";
                return false;
            }

            copiedPages.Add(new ReadableBookPageContent
            {
                PageIndex = index, Title = page.Title ?? string.Empty, Body = page.Body ?? string.Empty,
                HasCustomTitle = true, HasCustomBody = true
            });
        }

        var inputs = new ReadableBookInstanceData().InputInventory;
        // 随书材料和书页一起写入该本书的快照，不占玩家背包的额外槽位。
        inputs.GetItemSlot(0).itemData = writingMaterial;
        // 初始文本随该本物品序列化，调用方修改页面列表也不会改动已经发出的书。
        target.WriteData(new ReadableBookInstanceSnapshot
        {
            AddedPageCount = Math.Max(0, copiedPages.Count - basePageCount),
            PageContents = copiedPages,
            InputInventory = InventoryInstanceSnapshot.Capture(inputs)
        });
        failureReason = string.Empty;
        return true;
    }

    #endregion
}
