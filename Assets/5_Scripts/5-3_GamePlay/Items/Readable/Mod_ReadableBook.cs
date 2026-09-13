using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>可阅读书籍的一页内容。Key 指向 FlatWorld 内容表，Fallback 用于资源未就绪时兜底显示。</summary>
[Serializable]
public sealed class ReadableBookPageDefinition
{
    public string key;
    [TextArea(3, 12)] public string fallback;
}

/// <summary>
/// 通用可阅读物品模块。玩法层只负责响应 Item.Act 并发出阅读请求，具体书页表现由 UI 层处理。
/// </summary>
public sealed class Mod_ReadableBook : Module
{
    public const string ModuleId = "Mod_ReadableBook";

    public Ex_ModData ModData = new();
    public List<ReadableBookPageDefinition> pages = new();

    /// <summary>本地玩家请求打开一本书；UI 层监听此事件并实例化正式 Prefab。</summary>
    public static event Action<Mod_ReadableBook, Item> OpenRequested;

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public int PageCount => pages?.Count ?? 0;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("可阅读书籍模块数据类型错误。");
    }

    /// <summary>校验内容配置，并复用物品现有的统一使用动作。</summary>
    public override void Load()
    {
        ValidatePages();
        item.OnAct += Act;
    }

    /// <summary>书页内容来自定义配置，本模块没有额外运行态需要保存。</summary>
    public override void Save() { }

    /// <summary>对象池回收前解除动作订阅，避免复用后重复打开面板。</summary>
    public override void Unload()
    {
        if (item != null)
            item.OnAct -= Act;
    }

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
    }
}
