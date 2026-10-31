using UnityEngine;

/// <summary>手持食物只同步进食状态，圆形咬痕由共享 Sprite Shader 裁剪。</summary>
public sealed class Mod_HeldFood : Module, IFoodMechanic, IFoodStateObserver
{
    #region 模块数据与状态
    public const string ModuleId = "手上食物模块";
    private const float ProgressEpsilon = .0001f;
    public Ex_ModData_MemoryPackable ModData = new();
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? new Ex_ModData_MemoryPackable();
    }
    private IFoodRuntimeContext foodContext;
    private HeldFoodBiteRenderEffect biteEffect;
    private SpriteRenderer targetRenderer;
    private int originalSortingLayer, originalSortingOrder;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public string MechanicId => "food.held.visual";
    public int Priority => 20;
    #endregion

    #region 生命周期与状态通知
    protected override void OnLoad()
    {
        item.OnInHandChanged -= HandleInHandChanged;
        item.OnInHandChanged += HandleInHandChanged;
        RefreshBiteRendering();
    }

    protected override void OnUnload()
    {
        item.OnInHandChanged -= HandleInHandChanged;
        ReleaseBiteRendering();
        foodContext = null;
    }

    protected override void OnSave() { }

    public void BindFoodContext(IFoodRuntimeContext context)
    {
        foodContext = context;
        RefreshBiteRendering();
    }

    public void OnFoodStateChanged(FoodStateChangedContext _) => RefreshBiteRendering();

    private void HandleInHandChanged(bool inHand)
    {
        if (inHand) RefreshBiteRendering();
        else ReleaseBiteRendering();
    }
    #endregion

    #region 手持表现
    private void RefreshBiteRendering()
    {
        if (item == null || !item.InHand || foodContext == null)
        {
            ReleaseBiteRendering();
            return;
        }
        if (targetRenderer == null)
        {
            SpriteRenderer renderer = item.Sprite;
            if (renderer == null || renderer.sprite == null)
            {
                foreach (SpriteRenderer child in item.GetComponentsInChildren<SpriteRenderer>(true))
                    if (child.sprite != null) { renderer = child; break; }
            }
            if (renderer == null || renderer.sprite == null) return;
            targetRenderer = renderer;
            originalSortingLayer = targetRenderer.sortingLayerID;
            originalSortingOrder = targetRenderer.sortingOrder;
            WorldRenderingManager.ApplyAvailableRendererSorting(targetRenderer, WorldSortingManager.HeldFoodCategory);
        }
        float maximumProgress = Mathf.Max(ProgressEpsilon, foodContext.Data?.Max_EatingProgress ?? 1f);
        int maximumBites = Mathf.Max(1, Mathf.CeilToInt(maximumProgress));
        int completedBites = foodContext.EatingProgress >= maximumProgress - ProgressEpsilon
            ? maximumBites
            : Mathf.Clamp(Mathf.FloorToInt(foodContext.EatingProgress + ProgressEpsilon), 0, maximumBites - 1);
        // 完整食物不读取网格、不分配咬痕纹理或表现组件。
        if (completedBites == 0)
        {
            biteEffect?.ClearBites();
            return;
        }
        if (targetRenderer.sprite == null) return;
        biteEffect ??= item.GetComponent<HeldFoodBiteRenderEffect>() ?? item.gameObject.AddComponent<HeldFoodBiteRenderEffect>();
        biteEffect.SetBites(targetRenderer, completedBites, maximumBites, CreateBiteSeed(maximumBites));
    }

    private int CreateBiteSeed(int maximumBites)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (item.itemData?.Guid ?? 0);
            hash = hash * 31 + GetStableStringHash(item.itemData?.IDName);
            hash = hash * 31 + GetStableStringHash(targetRenderer.sprite.name);
            hash = hash * 31 + Mathf.RoundToInt(item.itemData?.Stack?.Amount ?? 0f);
            return hash * 31 + maximumBites;
        }
    }

    private static int GetStableStringHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            if (value != null)
                foreach (char character in value) hash = (hash ^ character) * 16777619;
            return (int)hash;
        }
    }

    private void ReleaseBiteRendering()
    {
        biteEffect?.ClearBites();
        if (targetRenderer != null)
        {
            targetRenderer.sortingLayerID = originalSortingLayer;
            targetRenderer.sortingOrder = originalSortingOrder;
        }
        targetRenderer = null;
    }
    #endregion
}
