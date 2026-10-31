using UnityEngine;

/// <summary>手持食物只提交进食状态，贴图、排序和咬痕资源由世界渲染管理器持有。</summary>
public sealed class Mod_HeldFood : Module, IFoodMechanic, IFoodStateObserver
{
    #region 模块数据与状态
    public const string ModuleId = "手上食物模块";
    public Ex_ModData_MemoryPackable ModData = new();
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? new Ex_ModData_MemoryPackable();
    }
    private IFoodRuntimeContext foodContext;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public string MechanicId => "food.held.visual";
    public int Priority => 20;
    #endregion

    #region 生命周期与状态通知
    protected override void OnLoad()
    {
        item.OnInHandChanged -= HandleInHandChanged;
        item.OnInHandChanged += HandleInHandChanged;
        SubmitFoodPresentation();
    }

    protected override void OnUnload()
    {
        item.OnInHandChanged -= HandleInHandChanged;
        WorldRenderingManager.ExistingInstance?.ClearHeldFoodPresentation(item);
        foodContext = null;
    }

    protected override void OnSave() { }

    public void BindFoodContext(IFoodRuntimeContext context)
    {
        foodContext = context;
        SubmitFoodPresentation();
    }

    public void OnFoodStateChanged(FoodStateChangedContext _) => SubmitFoodPresentation();
    private void HandleInHandChanged(bool _) => SubmitFoodPresentation();

    private void SubmitFoodPresentation()
    {
        WorldRenderingManager rendering = WorldRenderingManager.ExistingInstance;
        if (rendering == null || item == null) return;
        if (!item.InHand || foodContext == null)
        {
            rendering.ClearHeldFoodPresentation(item);
            return;
        }
        rendering.SetHeldFoodPresentation(item, foodContext.EatingProgress,
            foodContext.Data?.Max_EatingProgress ?? 1f);
    }
    #endregion
}
