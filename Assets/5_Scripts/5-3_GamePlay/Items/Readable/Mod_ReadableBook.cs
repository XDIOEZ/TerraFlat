using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

/// <summary>可阅读书籍的静态页定义；key 指向本地化内容，fallback 保存初始标题和正文。</summary>
[Serializable]
public sealed class ReadableBookPageDefinition
{
    public string key;
    [TextArea(3, 12)] public string fallback;
}

/// <summary>单本书的一页玩家编辑内容，按页码保存在书籍物品数据中。</summary>
[Serializable]
[MemoryPackable]
public partial class ReadableBookPageContent
{
    /// <summary>该本笔记中的零基页码。</summary>
    public int PageIndex;
    /// <summary>玩家自定义标题。</summary>
    public string Title;
    /// <summary>玩家自定义正文。</summary>
    public string Body;
    /// <summary>标题是否覆盖静态本地化标题。</summary>
    public bool HasCustomTitle;
    /// <summary>正文是否覆盖静态本地化正文。</summary>
    public bool HasCustomBody;
}

/// <summary>单本书的可变状态；纸张扩页、正文和两个专用物品槽随这本物品保存。</summary>
[Serializable]
public class ReadableBookInstanceData
{
    /// <summary>玩家追加的空白页数。</summary>
    public int AddedPageCount;
    /// <summary>按零基页码保存的玩家内容。</summary>
    public List<ReadableBookPageContent> PageContents = new();
    /// <summary>索引零为书写材料，索引一为扩页纸张。</summary>
    public Inventory_Data InputInventory = new(
        new List<ItemSlot> { new ItemSlot(0), new ItemSlot(1) },
        "readableBook.materials");
}

/// <summary>书籍实例进度和库存快照；当前输入标签与布局不进入持久化负载。</summary>
[MemoryPackable]
public sealed partial class ReadableBookInstanceSnapshot
{
    #region 书籍实例状态

    public int AddedPageCount;
    public List<ReadableBookPageContent> PageContents;
    public InventoryInstanceSnapshot InputInventory;

    #endregion
}

/// <summary>
/// 通用可阅读物品模块。配置页数是书籍的初始容量；实例编辑内容与扩页状态独立保存在该本物品上。
/// 书写材料与纸张通过物品标签配置，煤炭、木炭及 MOD 染料可共用同一库存事务接口。
/// </summary>
public sealed class Mod_ReadableBook : Module
{
    #region 配置与状态

    public const string ModuleId = "Mod_ReadableBook";
    public const string DefaultWritingMaterialTag = "readable.writing.ink";
    public const string DefaultPaperTag = "readable.paper";
    private const int WritingMaterialSlotIndex = 0;
    private const int PaperSlotIndex = 1;

    /// <summary>序列化该本笔记的书页与物品槽状态。</summary>
    public Ex_ModData_MemoryPackable ModData = new();
    /// <summary>该笔记的静态初始页面。</summary>
    public List<ReadableBookPageDefinition> pages = new();
    /// <summary>是否启用书写材料槽与内容编辑。</summary>
    public bool allowHandwriting;
    /// <summary>是否允许通过纸张扩充页数。</summary>
    public bool allowPageExpansion;
    /// <summary>可编辑文本的物品标签。</summary>
    public string writingMaterialTag = DefaultWritingMaterialTag;
    /// <summary>可用于扩页的物品标签。</summary>
    public string paperTag = DefaultPaperTag;

    /// <summary>当前物品实例独有的书页、扩页数和输入库存。</summary>
    private ReadableBookInstanceData instanceData = new();

    /// <summary>本地玩家请求打开一本书；UI 层监听此事件并实例化正式 Prefab。</summary>
    public static event Action<Mod_ReadableBook, Item> OpenRequested;

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public int BasePageCount => pages?.Count ?? 0;
    public int PageCount => BasePageCount + Mathf.Max(0, instanceData?.AddedPageCount ?? 0);
    public Inventory_Data InputInventoryData => instanceData?.InputInventory;
    public bool CanEditContent => allowHandwriting && IsWritingMaterial(InputInventoryData?.GetItemSlot(WritingMaterialSlotIndex)?.itemData);
    public bool CanAddPage => allowPageExpansion && IsPaper(InputInventoryData?.GetItemSlot(PaperSlotIndex)?.itemData);

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ??
            throw new ArgumentException("可阅读书籍模块数据类型错误。");
    }

    #endregion

    #region 生命周期与存档

    /// <summary>恢复单本书状态，建立输入槽的标签限制并绑定统一使用动作。</summary>
    protected override void OnLoad()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        EnsureInstanceData();
        bool hasSavedState = ModData.BitData != null && ModData.BitData.Length > 0;
        ReadableBookInstanceSnapshot saved = hasSavedState ? ModData.GetData<ReadableBookInstanceSnapshot>()
            : new ReadableBookInstanceSnapshot
            {
                InputInventory = InventoryInstanceSnapshot.Capture(new ReadableBookInstanceData().InputInventory)
            };
        if (saved?.InputInventory == null) throw new InvalidOperationException("书籍实例快照为空。");
        instanceData.AddedPageCount = Mathf.Max(0, saved.AddedPageCount);
        instanceData.PageContents = saved.PageContents ?? new();
        // 玩家书页和库存内容原位恢复，已绑定的输入槽继续保留引用。
        saved.InputInventory.RestoreTo(instanceData.InputInventory,
            data => data.SharedConfiguration == null
                ? ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data) : data);
        ValidatePages();

        InputInventoryData.Event_OnDataChanged -= OnInputInventoryChanged;
        InputInventoryData.Event_OnDataChanged += OnInputInventoryChanged;
        item.OnAct -= Act;
        item.OnAct += Act;
    }

    /// <summary>保存实例页、扩页数量和专用槽位的实例状态。</summary>
    protected override void OnSave()
    {
        if (ModData == null || instanceData == null)
            return;

        ModData.WriteData(new ReadableBookInstanceSnapshot
        {
            AddedPageCount = instanceData.AddedPageCount, PageContents = instanceData.PageContents,
            InputInventory = InventoryInstanceSnapshot.Capture(instanceData.InputInventory)
        });
        if (Item_Data?.ModuleDataDic != null && !string.IsNullOrWhiteSpace(ModData.Name))
        {
            Item_Data.ModuleDataDic[ModData.Name] = ModData;
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        }
    }

    /// <summary>卸载时解除动作和库存事件，避免对象池复用后重复保存。</summary>
    protected override void OnUnload()
    {
        if (item != null)
            item.OnAct -= Act;

        if (instanceData?.InputInventory != null)
            instanceData.InputInventory.Event_OnDataChanged -= OnInputInventoryChanged;
    }

    /// <summary>编辑器构建 Prefab 时保持稳定模块身份。</summary>
    private void OnValidate()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        ModData.ID = ModuleId;
        ModData.Name = ModuleId;
    }

    #endregion

    #region 阅读、书写与扩页

    /// <summary>只有本地玩家当前手持这件物品时，使用动作才打开阅读界面。</summary>
    public override void Act()
    {
        if (!CanRead(item?.Owner))
            return;

        OpenRequested?.Invoke(this, item.Owner);
    }

    /// <summary>面板持续用同一契约确认目标仍然是当前手持读物。</summary>
    public bool CanRead(Item actor)
    {
        return PageCount > 0 &&
               item != null &&
               !item.DestructionHandled &&
               item.InHand &&
               item.Owner == actor &&
               actor is Player player &&
               player.IsLocalProfile &&
               !actor.DestructionHandled;
    }

    /// <summary>读取一本书的初始静态页定义；玩家新增的空白页由 PageCount 单独提供。</summary>
    public bool TryGetPage(int index, out ReadableBookPageDefinition page)
    {
        if (pages != null && index >= 0 && index < pages.Count)
        {
            page = pages[index];
            return page != null;
        }

        page = null;
        return false;
    }

    /// <summary>将指定页保存的标题和正文覆盖到当前语言解析出的默认文本上。</summary>
    public void ResolvePageContent(int index, string defaultTitle, string defaultBody, out string title, out string body)
    {
        title = defaultTitle ?? string.Empty;
        body = defaultBody ?? string.Empty;
        ReadableBookPageContent saved = FindPageContent(index);
        if (saved == null)
            return;

        if (saved.HasCustomTitle)
            title = saved.Title ?? string.Empty;
        if (saved.HasCustomBody)
            body = saved.Body ?? string.Empty;
    }

    /// <summary>保存页标题；没有书写材料或页码越界时拒绝修改。</summary>
    public bool TrySetPageTitle(int index, string title)
    {
        return TrySetPageContent(index, title, true);
    }

    /// <summary>保存页正文；没有书写材料或页码越界时拒绝修改。</summary>
    public bool TrySetPageBody(int index, string body)
    {
        return TrySetPageContent(index, body, false);
    }

    /// <summary>从纸张槽消耗一张纸，为当前笔记追加一页空白页。</summary>
    public bool TryAddPageFromPaper()
    {
        if (!CanAddPage)
            return false;

        ItemSlot paperSlot = InputInventoryData.GetItemSlot(PaperSlotIndex);
        instanceData.AddedPageCount++;
        if (!InputInventoryData.TryConsumeFromSlot(paperSlot, 1, out _))
        {
            instanceData.AddedPageCount--;
            return false;
        }

        Save();
        return true;
    }

    private bool TrySetPageContent(int index, string value, bool isTitle)
    {
        if (!CanEditContent || index < 0 || index >= PageCount)
            return false;

        ReadableBookPageContent saved = GetOrCreatePageContent(index);
        if (isTitle)
        {
            saved.Title = value ?? string.Empty;
            saved.HasCustomTitle = true;
        }
        else
        {
            saved.Body = value ?? string.Empty;
            saved.HasCustomBody = true;
        }

        Save();
        return true;
    }

    private bool IsWritingMaterial(ItemData data)
    {
        return data?.Stack != null && data.Stack.Amount > 0f &&
               data.Tags != null && data.Tags.Contains(writingMaterialTag);
    }

    private bool IsPaper(ItemData data)
    {
        return data?.Stack != null && data.Stack.Amount > 0f &&
               data.Tags != null && data.Tags.Contains(paperTag);
    }

    #endregion

    #region 运行态数据与配置校验

    private void EnsureInstanceData()
    {
        instanceData ??= new ReadableBookInstanceData();
        instanceData.PageContents ??= new List<ReadableBookPageContent>();
        instanceData.AddedPageCount = Mathf.Max(0, instanceData.AddedPageCount);
        instanceData.InputInventory ??= new Inventory_Data(
            new List<ItemSlot> { new ItemSlot(0), new ItemSlot(1) },
            "readableBook.materials");
        instanceData.InputInventory.itemSlots ??= new List<ItemSlot>();

        while (instanceData.InputInventory.itemSlots.Count < 2)
            instanceData.InputInventory.itemSlots.Add(new ItemSlot(instanceData.InputInventory.itemSlots.Count));

        RebaseInputInventoryItems();
        ConfigureInputSlot(WritingMaterialSlotIndex, writingMaterialTag);
        ConfigureInputSlot(PaperSlotIndex, paperTag);
    }

    /// <summary>恢复专用槽物品当前定义，确保保存笔记随定义更新图标、标签与模块配置。</summary>
    private void RebaseInputInventoryItems()
    {
        GameRes gameRes = GameRes.Instance;
        for (int i = 0; i < instanceData.InputInventory.itemSlots.Count; i++)
        {
            ItemSlot slot = instanceData.InputInventory.itemSlots[i];
            if (slot?.itemData != null && slot.itemData.SharedConfiguration == null)
                slot.itemData = ItemDefinitionRuntime.RebasePersistedData(gameRes, slot.itemData);
        }
    }

    private void ConfigureInputSlot(int index, string acceptedTag)
    {
        ItemSlot slot = instanceData.InputInventory.itemSlots[index];
        if (slot == null)
            slot = instanceData.InputInventory.itemSlots[index] = new ItemSlot(index);

        slot.Index = index;
        slot.CanAcceptTags ??= new List<string>();
        slot.CanAcceptTags.Clear();
        if (!string.IsNullOrWhiteSpace(acceptedTag))
            slot.CanAcceptTags.Add(acceptedTag);
    }

    private ReadableBookPageContent FindPageContent(int index)
    {
        for (int i = 0; i < instanceData.PageContents.Count; i++)
        {
            ReadableBookPageContent page = instanceData.PageContents[i];
            if (page != null && page.PageIndex == index)
                return page;
        }

        return null;
    }

    private ReadableBookPageContent GetOrCreatePageContent(int index)
    {
        ReadableBookPageContent page = FindPageContent(index);
        if (page != null)
            return page;

        page = new ReadableBookPageContent { PageIndex = index };
        instanceData.PageContents.Add(page);
        return page;
    }

    private void OnInputInventoryChanged(ItemSlot _)
    {
        Save();
    }

    private void ValidatePages()
    {
        if (pages == null || pages.Count == 0)
            throw new InvalidOperationException($"可阅读物品 {Item_Data?.IDName ?? item?.name ?? "<unknown>"} 没有书页内容。");

        for (int i = 0; i < pages.Count; i++)
        {
            ReadableBookPageDefinition page = pages[i];
            if (page == null || string.IsNullOrWhiteSpace(page.key) || string.IsNullOrWhiteSpace(page.fallback))
                throw new InvalidOperationException($"可阅读物品 {Item_Data?.IDName ?? item?.name ?? "<unknown>"} 的第 {i + 1} 页缺少 key 或 fallback。");
        }

        if (allowPageExpansion && string.IsNullOrWhiteSpace(paperTag))
            throw new InvalidOperationException($"可阅读物品 {Item_Data?.IDName ?? item?.name ?? "<unknown>"} 的扩页物品或纸张标签未配置。");
        if (allowHandwriting && string.IsNullOrWhiteSpace(writingMaterialTag))
            throw new InvalidOperationException($"可阅读物品 {Item_Data?.IDName ?? item?.name ?? "<unknown>"} 的书写材料标签未配置。");
    }

    #endregion
}
