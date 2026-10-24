using UnityEngine;

/// <summary>单格铁路按邻轨变化更新直线或弯道，仅在安装后登记。</summary>
public sealed class Mod_RailTrack : Module, IItemModuleDependencyBinder, IBuildingPlacementCommitted
{
    #region 铁路生命周期
    public const string ModuleId = "Mod_RailTrack";
    public Ex_ModData Data = new();
    public override string CanonicalModuleId => ModuleId;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public ConveyorPath Path { get; private set; }
    public int RotationQuarterTurns { get; private set; }
    public bool IsAvailable => loaded && isActiveAndEnabled && item != null && !item.DestructionHandled && building != null && building.IsInstalled();
    private Mod_Building building;
    private bool loaded, registered;
    private SpriteRenderer visual;
    private Quaternion visualBaseRotation;
    private Vector3 visualBaseScale;
    private MaterialPropertyBlock properties;
    private static readonly int AnimationId = Shader.PropertyToID("_ConveyorAnimation");
    private static readonly int SurfaceId = Shader.PropertyToID("_ConveyorSurface");
    private static readonly int RegionId = Shader.PropertyToID("_ConveyorRegion");
    public void BindModuleDependencies(ItemMods modules) => building = modules.RequireSingleModById<Mod_Building>(ModText.Building);
    protected override void OnLoad()
    {
        loaded = true;
        visual = item.Sprite != null ? item.Sprite : item.GetComponentInChildren<SpriteRenderer>(true);
        if (visual != null) { visualBaseRotation = visual.transform.localRotation; visualBaseScale = visual.transform.localScale; }
        RotationQuarterTurns = Mathf.RoundToInt((item.itemData?.transform?.rotation ?? Quaternion.identity).eulerAngles.z / 90f) & 3;
        building.OnStateChanged += HandleBuildingState;
        Item.RuntimeStructureChanged += HandleOwnerReady;
        TryRegister();
    }
    public void OnBuildingPlacementCommitted() => TryRegister();
    protected override void OnSave() { }
    private void TryRegister()
    {
        if (registered || !IsAvailable) return;
        RailTrackNetwork.Register(this); registered = true;
        Item.RuntimeStructureChanged -= HandleOwnerReady;
    }
    private void HandleOwnerReady(Item owner)
    {
        if (owner != item || !item.IsInitialized) return;
        Item.RuntimeStructureChanged -= HandleOwnerReady;
        TryRegister();
    }
    private void HandleBuildingState(BuildingState previous, BuildingState current)
    {
        if (current is BuildingState.Installed or BuildingState.Damaged) TryRegister();
        else Release();
    }
    protected override void OnUnload()
    {
        Item.RuntimeStructureChanged -= HandleOwnerReady;
        if (building != null) building.OnStateChanged -= HandleBuildingState;
        Release(); loaded = false;
        if (visual != null && properties != null)
        { visual.GetPropertyBlock(properties); properties.SetVector(AnimationId, Vector4.zero); visual.SetPropertyBlock(properties); }
        if (visual != null) { visual.transform.localRotation = visualBaseRotation; visual.transform.localScale = visualBaseScale; }
    }
    private void OnEnable() { if (loaded) TryRegister(); }
    private void OnDisable() => Release();
    private void OnDestroy() => Unload();
    private void Release() { if (registered) RailTrackNetwork.Unregister(this); registered = false; }
    internal void SetPath(ConveyorPath path)
    {
        Path = path;
        if (visual == null || GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetVisualStateSprite("straight", out Sprite sprite) || sprite == null) return;
        visual.sprite = sprite;
        visual.transform.rotation = Quaternion.Euler(0f, 0f, path.Rotation * 90f);
        visual.transform.localScale = new Vector3(sprite.pixelsPerUnit / sprite.rect.width, sprite.pixelsPerUnit / sprite.rect.height, 1f);
        // 只复用传送带静态曲面映射，让弯轨与轨道半格端点严格重合。
        properties ??= new MaterialPropertyBlock(); visual.GetPropertyBlock(properties);
        properties.SetVector(AnimationId, new Vector4(path.Curved ? 7f : 6f, 0f, 0f, 1f));
        properties.SetVector(SurfaceId, new Vector4(2f, 3f, 0f, 1f));
        properties.SetVector(RegionId, ConveyorPresentation.Region(sprite));
        visual.SetPropertyBlock(properties);
    }
    #endregion
}
