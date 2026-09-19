using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>
/// 通用周期树冠结果模块：独立保存 1～5 个果实及短期飞行，不创建果实 Item；
/// 成熟果等待 1～1440 游戏秒后脱冠，落地进入 DroppedItemService，砍伐仅结算成熟库存。
/// </summary>
public sealed partial class Mod_CanopyFruit : Module, INaturalResourceInitializer, IItemModuleDependencyBinder, ICanopyFruitSink
{
    #region 配置与运行状态
    public Ex_ModData ModData = new(); // 通用 JSON 模块快照。
    public CanopyFruitSettings Settings = new(); // 可由 JSON/MOD 替换的时间规则。
    public string FruitItemId = "Coconut_Green"; // 成熟果定义。
    public string SplitItemId = "Coconut_Half"; // 撞击转换定义。
    public float BluntDamage = 10; // 坠落钝击伤害。
    public double SplitChance = 0.5; // 有效命中后的转半果概率。
    public float CollisionRadius = 0.12f; // 飞行扫掠半径。
    public float FruitWidth = 0.28f; // 成熟果的标准世界宽度。
    public float SmallScale = 0.2f; // 新果相对成熟果大小。
    public Vector2 CrownCenter = new(0, 2.25f); // 树贴图局部冠心。
    public Vector2 CrownSpread = new(0.28f, 0.14f); // 果位椭圆半轴。
    public bool UseNormalizedCrownAnchor; // 按贴图矩形绑定，避免改 PPU/Pivot 后果实悬空。
    public Vector2 CrownAnchorUV = new(0.5f, 0.625f);
    public Vector2 CrownSpreadUV = new(0.048f, 0.02f);
    [Min(0f)] public float DropScatterRadius = 0.85f;
    private CanopyFruitState state; // 唯一运行态权威。
    private DamageReceiver receiver; // 树本身的死亡权威。
    private Mod_Grow growth; // 可选树龄门禁。
    private Sprite fruitSprite, splitSprite; // 已加载定义的共享贴图。
    private bool wasTicked, liveStep; // 首次加载和历史补算不追溯伤害。
    private double stepFrom, stepTo; // 当前活跃步的时间区间。
    private readonly Dictionary<int, SpriteRenderer> visuals = new(); // 无 Collider 的临时视觉。
    public override string CanonicalModuleId => "Mod_CanopyFruit";
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    #endregion

    #region 生命周期与存档
    /// <summary>从正式模块注册表解析唯一死亡权威和可选生长门禁。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        receiver = null;
        growth = null;
        foreach (Module module in modules.Mods.Values)
        {
            if (module is DamageReceiver health)
            {
                if (receiver != null) throw new InvalidOperationException("树果模块不允许多个生命权威。");
                receiver = health;
            }
            if (module is Mod_Grow grow)
            {
                if (growth != null) throw new InvalidOperationException("树果模块不允许多个生长门禁。");
                growth = grow;
            }
        }
        if (receiver == null) throw new InvalidOperationException("树果模块必须组合生命模块。");
    }

    /// <summary>只恢复状态和订阅，不在模块 Load 栈内生成掉落。</summary>
    public override void Load()
    {
        Settings.Validate();
        if (BluntDamage < 0 || float.IsNaN(BluntDamage) || float.IsInfinity(BluntDamage) ||
            !(SplitChance >= 0 && SplitChance <= 1) || !(CollisionRadius > 0) || !(FruitWidth > 0) ||
            !(SmallScale > 0 && SmallScale <= 1)) throw new InvalidOperationException("树果伤害或表现配置无效。");
        state = ModData.GetData<CanopyFruitState>() ?? new CanopyFruitState();
        fruitSprite = RequireSprite(FruitItemId);
        splitSprite = RequireSprite(SplitItemId);
        receiver.DeathStarted -= HandleDeath;
        receiver.DeathStarted += HandleDeath;
        wasTicked = false;
        liveStep = false;
    }

    /// <summary>保存不可变字符串快照；不推进、不解绑、不产生副作用。</summary>
    public override void Save() => ModData.WriteData(state);

    /// <summary>卸载只销毁视觉，未落地轨迹随树的模块快照保留，加载后补算。</summary>
    public override void Unload()
    {
        if (receiver != null) receiver.DeathStarted -= HandleDeath;
        foreach (SpriteRenderer visual in visuals.Values)
            if (visual != null) Destroy(visual.gameObject);
        visuals.Clear();
        wasTicked = false;
        liveStep = false;
    }

    /// <summary>自然初始化只设置新记录的种子，不覆盖已恢复的果实。</summary>
    public void InitializeNaturalResource(uint deterministicRandomValue)
    {
        if (state != null && !state.Initialized)
            state.RandomState = deterministicRandomValue;
    }

    /// <summary>使用世界游戏秒推进，禁止将渲染 deltaTime 当成成熟倒计时。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || !_Data.isRunning || state.Stopped) return;
        if (!TryGetClock(out double now)) return;
        if (!state.Initialized)
        {
            if (growth != null && growth.Data.growState != Mod_Grow.GrowState.成熟) return;
            uint seed = state.RandomState == 0 ? unchecked((uint)item.itemData.Guid) : state.RandomState;
            CanopyFruitTimeline.Initialize(state, now, seed);
        }
        stepFrom = state.Time;
        stepTo = now;
        liveStep = wasTicked && now >= stepFrom && now - stepFrom <= 0.25;
        foreach (CanopyFruitRecord flight in state.Flights) SweepFlight(flight);
        bool caughtUp = CanopyFruitTimeline.Advance(state, Settings, now, this);
        wasTicked = caughtUp;
        RefreshVisuals();
    }

    /// <summary>按实体所在世界解析引用时钟，不跨维度使用活动世界时间。</summary>
    private bool TryGetClock(out double now)
    {
        now = 0;
        if (!DayTimeSystem.Instance.TryGetResolvedTimeData(item.gameObject.scene.name, out _, out TimeData clock)) return false;
        now = clock.TotalDays * (double)clock.DayLength + clock.CurrentTime;
        return true;
    }
    #endregion

    #region 落地与砍伐
    /// <summary>脱冠时冻结轨迹；初始位置来自树冠独立果位。</summary>
    public void BeginFall(CanopyFruitRecord fruit)
    {
        Vector2 start = CrownPosition(fruit.Slot);
        Vector2 end = ChooseGroundLandingPosition();
        fruit.StartX = start.x; fruit.StartY = start.y;
        fruit.EndX = end.x; fruit.EndY = end.y;
        SweepFlight(fruit);
    }

    /// <summary>每次脱冠只抽取一次圆盘落点并存入果实轨迹，读档不会重抽，也不再排成树底一条直线。</summary>
    private Vector2 ChooseGroundLandingPosition()
    {
        float angle = (float)NextImpactRandom() * Mathf.PI * 2f;
        float radius = Mathf.Sqrt((float)NextImpactRandom()) * Mathf.Max(0f, DropScatterRadius);
        return (Vector2)item.transform.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    /// <summary>先移除临时伤害权限，再移交正式掉落链；不实例化可拾取 Item。</summary>
    public void Land(CanopyFruitRecord fruit)
    {
        fruit.HitConsumed = true;
        SpawnFruit(fruit, false);
        RemoveVisual(fruit.Id);
    }

    /// <summary>产量修饰只在此边界使用一次；自然落果像采摘一样为一份，砍伐额外服从死亡战利品难度。</summary>
    public void SpawnFruit(CanopyFruitRecord fruit, bool chopped)
    {
        string output = fruit.Split ? SplitItemId : FruitItemId;
        if (fruit.OutputAmount < 0)
        {
            float multiplier = ResourceYieldUtility.GetMultiplier(item, FruitItemId);
            if (chopped) multiplier *= GameDifficultyService.Current.World.LootAmountMultiplier;
            fruit.OutputAmount = GameDifficultyService.ScaleRandomizedAmount(1, multiplier);
        }
        if (fruit.OutputAmount > 0)
            DroppedItemService.SpawnLoot(output, new Vector2(fruit.EndX, fruit.EndY), fruit.OutputAmount,
                radius: 0, duration: 0);
    }

    /// <summary>死亡前结清历史；未成熟果清除，已脱冠果安全落地，事件重入不会重复产出。</summary>
    private void HandleDeath(DamageReceiver source)
    {
        if (state.Stopped || !GameNetwork.HasStateAuthority) return;
        liveStep = false;
        double now = state.Time;
        if (TryGetClock(out double current)) now = current;
        while (!CanopyFruitTimeline.Advance(state, Settings, now, this)) { }
        state.Stopped = true;
        for (int index = state.Flights.Count - 1; index >= 0; index--)
        {
            Land(state.Flights[index]);
            state.Flights.RemoveAt(index);
        }
        for (int index = state.Fruits.Count - 1; index >= 0; index--)
        {
            CanopyFruitRecord fruit = state.Fruits[index];
            if (CanopyFruitTimeline.CanDropOnDeath(fruit, now))
            {
                Vector2 end = ChooseGroundLandingPosition();
                fruit.EndX = end.x;
                fruit.EndY = end.y;
                SpawnFruit(fruit, true);
            }
            RemoveVisual(fruit.Id);
            state.Fruits.RemoveAt(index);
        }
    }
    #endregion
}
