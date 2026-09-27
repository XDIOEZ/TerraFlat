using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>蜂巢头顶调试参数的独立总开关，由 GM 面板控制并持久化。</summary>
public static class HiveColonyDebugOverlay
{
    public static bool Visible { get; private set; }

    public static void SetVisible(bool visible) => Visible = visible;

    public static bool Toggle()
    {
        Visible = !Visible;
        return Visible;
    }
}

/// <summary>
/// 蜂巢保存成员身份、每只蜜蜂的行为状态、蜂蜜库存和繁殖时钟。
/// 初始三只、最多十只；此后的新蜂必须由满蜜和超过三个游戏日的间隔产生。
/// 成员随蜂巢卸载，独立 Actor 不重复保存自由 AI 快照。
/// </summary>
public sealed partial class Mod_HiveColony : Module
{
    #region 配置与存档
    [Serializable]
    public sealed class ResidentState
    {
        public int Guid; // 成员的稳定身份。
        public Mod_BeeBehavior.BeeState Bee = new(); // 该蜂的行为快照。
        public bool SleepingInHive; // 夜间已回到蜂巢并卸载本体。
        public float SleepDrainPerSecond = 1f; // 入睡时记录该蜂正常饱食消耗，睡眠按倍率推进。
    }

    [Serializable]
    public sealed class ColonyState
    {
        public List<ResidentState> Residents = new(); // 巢群成员。
        public int Honey; // 当前蜂蜜点数。
        public int HoneyCapacity = 10; // 每次繁殖后增加一的容量。
        public double LastBirthTime; // 上次诞蜂的绝对游戏时间。
        public bool BirthClockInitialized; // 新蜂巢从首次装载起计时。
        public bool InitialResidentsCreated; // 死亡成员不免费补足初始数量。
    }

    public Ex_ModData ModData = new(); // 模块数据契约。
    public string ActorId = "Bee"; // 可由内容定义替换的成员物种。
    [Min(1)] public int MinimumResidents = 3; // 新蜂巢的初始成员数。
    [Min(1)] public int MaximumResidents = 10; // 巢群成员上限。
    [Min(0.1f)] public float SpawnRadius = 0.45f; // 巢边出生半径。
    [Min(0.1f)] public float ReconcileInterval = 1f; // 成员与繁殖检查间隔。
    [Min(0)] public int TerritoryBaseRadiusCells = 2; // 空巢基础半径两格，即五乘五；每名成员再向四周扩一格。
    [Min(0.1f)] public float AlarmSeconds = 10f; // 玩家连续停留警戒时长。
    [Range(0f, 1f)] public float DayStartRatio = 0.25f; // 早晨六点开始出巢。
    [Range(0f, 1f)] public float DayEndRatio = 0.75f; // 傍晚六点后开始归巢。
    [Range(0f, 1f)] public float SleepingSatietyDrainMultiplier = 0.5f; // 睡眠饱食消耗减半。

    private ColonyState state = new(); // 持久巢群状态。
    private readonly Dictionary<int, Item> residents = new(10); // 本轮装载成员。
    private float reconcileRemaining; // 下次成员维护的现实帧间隔。
    private bool alarmClockReady; // 本轮警戒时钟是否已校准。
    private double lastAlarmGameTime; // 上次警戒检查的世界绝对时间。
    public override string CanonicalModuleId => "Mod_HiveColony";
    public override ModuleData _Data { get => ModData; set => ModData = (Ex_ModData)value; }
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public Vector2 HomePosition => WorldTopologyRuntime.NormalizePosition(item.transform.position);
    public int Honey => state.Honey;
    public int HoneyCapacity => state.HoneyCapacity;
    public int SleepingResidents => CountSleepingResidents();
    public int TerritoryRadiusCells => TerritoryBaseRadiusCells + state.Residents.Count; // 边长恒为奇数，每多一蜂增加两格。
    public int TerritorySizeCells => TerritoryRadiusCells * 2 + 1;
    #endregion

    #region 调试显示
    private static GUIStyle hiveDebugStyle; // 所有蜂巢共用调试样式。

    /// <summary>独立显示蜂巢库存、成员、繁殖与领地警戒参数。</summary>
    private void OnGUI()
    {
        if (!HiveColonyDebugOverlay.Visible || !Application.isPlaying || item == null)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Vector3 screenPos = camera.WorldToScreenPoint(item.transform.position + new Vector3(0f, 1.25f, 0f));
        if (screenPos.z <= 0f)
            return;

        hiveDebugStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            normal = { textColor = Color.white }
        };

        string line1 =
            $"蜂巢 | 蜂蜜: {state.Honey}/{state.HoneyCapacity} | 成员: {state.Residents.Count}/{MaximumResidents} | 已装载: {residents.Count} | 睡眠: {SleepingResidents}";
        string line2 =
            $"繁殖: {GetBirthDebugText()} | 警戒: {GetAlarmDebugText()} | 领地: {TerritorySizeCells}×{TerritorySizeCells}格";
        float width = Mathf.Max(
            hiveDebugStyle.CalcSize(new GUIContent(line1)).x,
            hiveDebugStyle.CalcSize(new GUIContent(line2)).x) + 14f;
        const float height = 46f;
        Rect rect = new(
            screenPos.x - width * 0.5f,
            Screen.height - screenPos.y - height * 0.5f,
            width,
            height);
        GUI.Box(rect, $"{line1}\n{line2}", hiveDebugStyle);
    }

    /// <summary>把繁殖约束整理为当前最直接的调试状态。</summary>
    private string GetBirthDebugText()
    {
        if (state.Residents.Count >= MaximumResidents)
            return "已满员";
        if (state.Honey != state.HoneyCapacity)
            return "等待满蜜";
        if (!state.BirthClockInitialized)
            return "等待计时初始化";
        if (!TryGetWorldTime(out double now, out float dayLength) || dayLength <= 0f)
            return "时间不可用";

        double remainingSeconds = Math.Max(0d, dayLength * 3d - (now - state.LastBirthTime));
        return remainingSeconds <= 0d
            ? "条件满足"
            : $"剩余 {remainingSeconds / dayLength:F1} 天";
    }

    /// <summary>显示当前领地内玩家数量及共享警戒进度。</summary>
    private string GetAlarmDebugText()
    {
        float maxStay = 0f;
        foreach (float seconds in playerStay.Values)
            maxStay = Mathf.Max(maxStay, seconds);

        if (playerStay.Count == 0)
            return "安全";
        if (maxStay >= AlarmSeconds)
            return $"满警戒 ({playerStay.Count}人)";
        return $"观察 {playerStay.Count}人 / {maxStay:F1}/{AlarmSeconds:F0}s";
    }
    #endregion

    #region 生命周期
    public override void Load()
    {
        if (string.IsNullOrWhiteSpace(ActorId) || MinimumResidents < 1 || MaximumResidents < MinimumResidents ||
            SpawnRadius <= 0f || ReconcileInterval <= 0f || TerritoryBaseRadiusCells < 0 || AlarmSeconds <= 0f ||
            DayStartRatio < 0f || DayEndRatio > 1f || DayStartRatio >= DayEndRatio ||
            SleepingSatietyDrainMultiplier < 0f || SleepingSatietyDrainMultiplier > 1f)
            throw new InvalidOperationException("蜂巢生物群配置无效。");
        state = ModData.GetData<ColonyState>() ?? new ColonyState();
        if (state.Residents == null || state.HoneyCapacity < 10 || state.Honey < 0 ||
            state.Honey > state.HoneyCapacity || state.Residents.Count > MaximumResidents)
            throw new InvalidOperationException("蜂巢存档状态无效。");
        HashSet<int> unique = new();
        foreach (ResidentState member in state.Residents)
            if (member == null || member.Guid == 0 || member.Bee == null || !unique.Add(member.Guid))
                throw new InvalidOperationException("蜂巢存档包含无效或重复的成员。");
        residents.Clear();
        ClearTerritoryAlarm();
        reconcileRemaining = 0f;
        alarmClockReady = false;
        hiveDestroyed = false;
        BindHiveDamageEvents();
    }

    /// <summary>先同步成员的饱食度与愤怒值，再保存蜂巢唯一的权威快照。</summary>
    public override void Save()
    {
        PruneDeadResidents();
        CaptureResidents();
        ModData.WriteData(state);
    }

    /// <summary>卸载时先保存成员状态，再撤回独立 Actor。</summary>
    public override void Unload()
    {
        UnbindHiveDamageEvents();
        PruneDeadResidents();
        CaptureResidents();
        ModData.WriteData(state);
        if (!hiveDestroyed)
        {
            ItemMgr manager = ItemMgr.Instance;
            foreach (Item resident in residents.Values)
                if (resident != null && !resident.DestructionHandled)
                    manager.DespawnItem(resident, saveData: false);
        }
        residents.Clear();
        ClearTerritoryAlarm();
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || !ModData.isRunning)
            return;
        if (hiveDestroyed)
            return;
        float step = Mathf.Max(0f, deltaTime);
        if (TryAdvanceAlarmClock(out float alarmSeconds))
        {
            if (IsDayTime())
                TickTerritoryAlarm(alarmSeconds);
            else
            {
                ClearTerritoryAlarm();
                ApplyTerritoryAlert(0f);
            }
        }
        TickSleepCycle(step);
        reconcileRemaining -= step;
        if (reconcileRemaining > 0f)
            return;
        reconcileRemaining = ReconcileInterval;
        ReconcileResidents();
        TryBirth();
    }
    #endregion

    #region 成员维护
    /// <summary>恢复存档成员；只在新巢首次装载时创建初始成员。</summary>
    private void ReconcileResidents()
    {
        PruneDeadResidents();
        for (int index = 0; index < state.Residents.Count; index++)
        {
            ResidentState member = state.Residents[index];
            if (!member.SleepingInHive && !residents.ContainsKey(member.Guid))
                SpawnResident(index, member);
        }
        if (state.InitialResidentsCreated)
            return;
        state.Honey = UnityEngine.Random.Range(1, 11); // 新蜂巢初始携带 1~10 点蜂蜜，仅首次创建成员时随机一次。
        while (state.Residents.Count < MinimumResidents)
            SpawnResident(state.Residents.Count, null);
        state.InitialResidentsCreated = true;
    }

    /// <summary>死亡成员永久离巢；尚未重建的存档成员保持原 GUID。</summary>
    private void PruneDeadResidents()
    {
        for (int index = state.Residents.Count - 1; index >= 0; index--)
        {
            ResidentState member = state.Residents[index];
            if (!residents.TryGetValue(member.Guid, out Item resident))
                continue;
            AI_Bird bird = resident != null ? resident.GetComponentInChildren<AI_Bird>(true) : null;
            if (bird != null && bird.IsAlive && !resident.DestructionHandled)
                continue;
            residents.Remove(member.Guid);
            state.Residents.RemoveAt(index);
        }
    }

    /// <summary>复制每只已装载蜜蜂的运行态。</summary>
    private void CaptureResidents()
    {
        foreach (ResidentState member in state.Residents)
        {
            if (!residents.TryGetValue(member.Guid, out Item resident) || resident == null)
                continue;
            Mod_BeeBehavior bee = resident.itemMods?.GetMod_ByID<Mod_BeeBehavior>(Mod_BeeBehavior.ModuleId);
            if (bee != null)
                member.Bee = bee.CaptureState();
        }
    }

    /// <summary>按存档 GUID 或新身份创建成员，并绑定蜂巢与行为快照。</summary>
    private void SpawnResident(int slot, ResidentState saved)
    {
        ItemMgr manager = ItemMgr.Instance;
        if (!GameRes.Instance.TryGetItemDefinition(ActorId, out _))
            throw new InvalidOperationException($"蜂巢物种 {ActorId} 未注册。");
        Vector2 home = HomePosition;
        float angle = slot * (Mathf.PI * 2f / MaximumResidents);
        Vector2 offset = new(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector3 position = WorldTopologyRuntime.NormalizePosition((Vector3)(home + offset * SpawnRadius));
        ItemData data = GameRes.Instance.CreateItemData(ActorId);
        if (saved != null)
        {
            if (manager.GetItemByGuid(saved.Guid) != null)
                throw new InvalidOperationException($"蜂巢成员 GUID {saved.Guid} 已被其他实体占用。");
            data.Guid = saved.Guid;
        }
        Item resident = manager.InstantiateItem(data, position);
        try
        {
            resident.Load();
            AI_Bird bird = resident.itemMods.GetMod_ByID<AI_Bird>("AI_Bird");
            Mod_BeeBehavior bee = resident.itemMods.GetMod_ByID<Mod_BeeBehavior>(Mod_BeeBehavior.ModuleId);
            if (bird == null || !bird.permanentFlight || bee == null)
                throw new InvalidOperationException($"蜂巢物种 {ActorId} 必须组合常驻飞行与蜜蜂行为模块。");
            bird.SetColonyHome(item.itemData.Guid, home);
            bee.BindColony(this, saved?.Bee);
            int guid = resident.itemData.Guid;
            if (guid == 0 || (saved != null && guid != saved.Guid) || residents.ContainsKey(guid))
                throw new InvalidOperationException($"蜂巢生成了无效或重复的成员 GUID：{guid}。");
            residents.Add(guid, resident);
            if (saved == null)
                state.Residents.Add(new ResidentState { Guid = guid, Bee = bee.CaptureState() });
        }
        catch
        {
            if (!resident.DestructionHandled)
                manager.DespawnItem(resident, saveData: false);
            throw;
        }
    }
    #endregion

    #region 蜂蜜交易与繁殖
    /// <summary>成员返巢贡献一单位蜂蜜，库存不超过当前容量。</summary>
    public void ReceiveHoney() => state.Honey = Mathf.Min(state.HoneyCapacity, state.Honey + 1);

    /// <summary>无采蜜源时消耗巢内的一单位蜂蜜。</summary>
    public bool TryConsumeHoney()
    {
        if (state.Honey <= 0)
            return false;
        state.Honey--;
        return true;
    }

    /// <summary>满蜜且距上次诞蜂严格超过三个游戏日时繁殖。</summary>
    private void TryBirth()
    {
        if (!TryGetWorldTime(out double now, out float dayLength))
            return;
        if (!state.BirthClockInitialized)
        {
            state.LastBirthTime = now;
            state.BirthClockInitialized = true;
            return;
        }
        if (state.Residents.Count >= MaximumResidents || state.Honey != state.HoneyCapacity ||
            now - state.LastBirthTime <= dayLength * 3d)
            return;
        SpawnResident(state.Residents.Count, null);
        state.Honey -= 5;
        state.HoneyCapacity++;
        state.LastBirthTime = now;
    }

    /// <summary>读取世界绝对游戏时间，繁殖不依赖现实帧时间。</summary>
    private bool TryGetWorldTime(out double now, out float dayLength)
    {
        now = 0d;
        dayLength = 0f;
        DayTimeSystem clock = DayTimeSystem.Instance;
        if (clock == null || !clock.TryGetResolvedTimeData(item.gameObject.scene.name, out _, out TimeData time))
            return false;
        dayLength = time.DayLength;
        now = time.TotalDays * (double)dayLength + time.CurrentTime;
        return true;
    }

    /// <summary>蜂巢警戒与蜜蜂使用同一游戏时间单位。</summary>
    private bool TryAdvanceAlarmClock(out float elapsed)
    {
        elapsed = 0f;
        if (!TryGetWorldTime(out double now, out _))
            return false;
        if (alarmClockReady)
            elapsed = (float)Math.Max(0d, now - lastAlarmGameTime);
        lastAlarmGameTime = now;
        alarmClockReady = true;
        return true;
    }
    #endregion
}
