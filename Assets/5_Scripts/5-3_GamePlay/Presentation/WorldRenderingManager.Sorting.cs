using System;
using UnityEngine;

public sealed partial class WorldRenderingManager
{
    #region 排序功能模块

    private WorldSortingManager sortingRenderingBackend;

    private void InitializeSortingRenderingModule()
    {
        sortingRenderingBackend = WorldSortingManager.GetInstance();
    }

    internal void AttachSortingRenderingBackend(WorldSortingManager backend)
    {
        sortingRenderingBackend = backend;
        foreach (Item item in itemRenderingRegistrations.Keys) RegisterItemSorting(item);
    }

    /// <summary>各类物品和独立效果共用同一份排序类别与排序键。</summary>
    public void GetSortingKey(string category, out int sortingLayerId, out int sortingOrder)
    {
        if (sortingRenderingBackend != null)
            sortingRenderingBackend.GetSortingKey(category, out sortingLayerId, out sortingOrder);
        else
            WorldSortingManager.GetResourceSortingKey(category, out sortingLayerId, out sortingOrder);
    }

    public void ApplyRendererSorting(Renderer renderer, string category, int orderOffset = 0)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        GetSortingKey(category, out int layerId, out int order);
        renderer.sortingLayerID = layerId;
        renderer.sortingOrder = checked(order + orderOffset);
    }

    /// <summary>启动前使用已存在的后端或正式资源，不创建排序管理器。</summary>
    public static void ApplyAvailableRendererSorting(Renderer renderer, string category)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        WorldRenderingManager manager = ExistingInstance;
        if (manager != null) { manager.ApplyRendererSorting(renderer, category); return; }
        WorldSortingManager sorting = WorldSortingManager.GetExistingInstance();
        int liveLayer, liveOrder;
        if (sorting != null) sorting.GetSortingKey(category, out liveLayer, out liveOrder);
        else WorldSortingManager.GetResourceSortingKey(category, out liveLayer, out liveOrder);
        renderer.sortingLayerID = liveLayer;
        renderer.sortingOrder = liveOrder;
    }

    public void RegisterSortingMember(WorldSortingMember member)
    {
        if (sortingRenderingBackend == null) sortingRenderingBackend = WorldSortingManager.GetInstance();
        if (sortingRenderingBackend != null) sortingRenderingBackend.Register(member);
    }

    /// <summary>独立成员可早于会话入口启用，只复用已有后端而不创建管理对象。</summary>
    public static void RegisterAvailableSortingMember(WorldSortingMember member)
    {
        WorldRenderingManager manager = ExistingInstance;
        if (manager != null) manager.RegisterSortingMember(member);
        else WorldSortingManager.GetInstance()?.Register(member);
    }

    /// <summary>MOD 扩展排序类别继续交给原有类别校验规则。</summary>
    public void RegisterSortingProfile(string json)
    {
        if (sortingRenderingBackend == null)
            throw new InvalidOperationException("世界排序后端尚未初始化。");
        sortingRenderingBackend.RegisterProfile(json);
    }

    private void RegisterItemSorting(Item item)
    {
        if (sortingRenderingBackend != null) sortingRenderingBackend.RegisterItemRendering(item);
    }

    private static void UnregisterItemSorting(Item item)
    {
        if (item != null) item.GetComponent<WorldSortingMember>()?.UnbindWorldItemRendering();
    }

    #endregion
}
