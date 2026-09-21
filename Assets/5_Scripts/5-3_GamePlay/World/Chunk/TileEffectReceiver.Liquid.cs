using UnityEngine;

public partial class TileEffectReceiver
{
    #region 独立液体接触
    private LiquidDefinition activeLiquid;
    private WorldLiquidContactData activeLiquidData;
    private bool activeLiquidEdge;
    private bool liquidCallback;
    private bool liquidContactTransition;
    private bool groundCallback;
    private EnvironmentInteractionRunner groundEnvironmentInteractions;

    /// <summary>正在深水中消耗游泳储备维持上浮，只暂停 Ground Behaviour。</summary>
    public bool LiquidFloating { get; private set; }
    public bool HasLiquidContact => activeLiquid != null && !activeLiquidEdge;
    public string ActiveLiquidId => activeLiquid?.Id;
    public float LiquidDepth => activeLiquidData != null && !activeLiquidEdge ? activeLiquidData.LiquidDepth : 0f;
    public EnvironmentInteractionRunner GroundEnvironmentInteractions => EnsureGroundEnvironmentInteractions();

    /// <summary>浮沉边界各触发一次 Ground Exit/Enter；液体生存与动作始终独立继续。</summary>
    private void SetLiquidFloating(bool value)
    {
        if (LiquidFloating == value) return;
        LiquidFloating = value;
        if (value) ExitCurrentTileEffects();
        else if (!liquidContactTransition && !isPreparedForWorldTransition && effectSuppressors.Count == 0 && !hasActiveTileEffects)
            EnterTile(GetCurrentGridPos());
    }

    protected virtual bool TryResolveLiquidContact(Vector2Int grid, out WorldLiquidSourceTarget target, out bool edge)
    {
        edge = false;
        if (WorldLiquidSourceResolver.TryResolve(transform.position, out target)) return true;
        foreach (Vector2Int offset in WaterEdgeDirections)
        {
            Vector2Int cell = grid + offset;
            if (!WorldLiquidSourceResolver.TryResolve(new Vector2(cell.x + 0.5f, cell.y + 0.5f), out target)) continue;
            edge = true;
            return true;
        }
        return false;
    }

    /// <summary>深度变化直接刷新快照，不重复退出进入；液体身份或接触模式改变才切换行为。</summary>
    private void RefreshLiquidContact(Vector2Int grid, float deltaTime)
    {
        WorldLiquidSourceTarget target = default;
        bool edge = false;
        bool found = item != null && TryResolveLiquidContact(grid, out target, out edge);
        // 显式初始化，避免短路时使用未赋值的 out 局部变量。
        if (!found) { ExitLiquidContact(); return; }
        liquidContactTransition = true;
        try
        {
            if (activeLiquid != target.Liquid || activeLiquidEdge != edge)
            {
                ExitLiquidContact();
                activeLiquid = target.Liquid;
                activeLiquidEdge = edge;
                activeLiquidData = new WorldLiquidContactData(
                    activeLiquid, target.WorldCell, target.Sample.LiquidDepth);
                liquidCallback = true;
                activeLiquid.WorldWater.Behaviour.OnEnter(item, activeLiquidData, this);
            }
            activeLiquidData.WorldCell = target.WorldCell;
            activeLiquidData.LiquidDepth = target.Sample.LiquidDepth;
            liquidCallback = true;
            activeLiquid.WorldWater.Behaviour.OnUpdate(item, activeLiquidData, this, Mathf.Max(0f, deltaTime));
        }
        finally { liquidCallback = false; liquidContactTransition = false; }
    }

    private void ExitLiquidContact()
    {
        if (activeLiquid == null) return;
        liquidCallback = true;
        try { activeLiquid.WorldWater.Behaviour.OnExit(item, this); }
        finally
        {
            liquidCallback = false;
            activeLiquid = null;
            activeLiquidData = null;
            activeLiquidEdge = false;
        }
    }

    /// <summary>Ground 单独持有环境效果实例，退出泥地/雪地不能清除液体减速和饮用动作。</summary>
    private EnvironmentInteractionRunner EnsureGroundEnvironmentInteractions()
    {
        EnsureEnvironmentInteractions();
        if (groundEnvironmentInteractions == null)
            groundEnvironmentInteractions = gameObject.AddComponent<EnvironmentInteractionRunner>();
        groundEnvironmentInteractions.Bind(item != null ? item : GetComponentInParent<Item>());
        return groundEnvironmentInteractions;
    }

    private void InvokeGroundEnter(RuntimeTileDefinition definition, TileData data, Map map)
    {
        groundCallback = true;
        try { definition.OnEnter(item, data, map, this); }
        finally { groundCallback = false; }
    }
    private void InvokeGroundExit(RuntimeTileDefinition definition, TileData data, Map map)
    {
        groundCallback = true;
        try { definition.OnExit(item, data, map, this); }
        finally { groundCallback = false; }
    }
    private void InvokeGroundUpdate(float deltaTime)
    {
        groundCallback = true;
        try { activeTileBlock.OnUpdate(item, activeTileData, activeTileMap, this, deltaTime); }
        finally { groundCallback = false; }
    }

    public override void Unload()
    {
        PrepareForWorldTransition();
        effectSuppressors.Clear();
        base.Unload();
    }
    #endregion
}
