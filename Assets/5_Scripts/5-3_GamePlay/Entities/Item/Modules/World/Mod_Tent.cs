using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>帐篷交互模块；作为独立 Module Prefab 挂载到通用建筑本体 Shell。</summary>
public class Mod_Tent : Module, IInteractable
{
    #region 模块数据

    private const string TentModuleId = "帐篷模块";

    public Ex_ModData ModData = new Ex_ModData { ID = TentModuleId };
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? new Ex_ModData { ID = TentModuleId };
    }

    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    #endregion

    #region 配置

    public GameObject basePanel;
    public GameObject sleepPanel_Prefab;

    [Header("UI配置")]
    public string cancelButtonName = "取消";
    public float panelFadeDuration = 0.6f;
    public string zzzRootName = "ZZZs";

    [Header("ZZZ漂浮配置")]
    public float zzzMinSpeed = 40f;
    public float zzzMaxSpeed = 120f;
    public float zzzEdgePadding = 8f;

    [Header("睡觉配置")]
    public float sleepDuration = 8f;
    public float sleepTickInterval = 1f;
    [Range(0f, 1f)]
    [Tooltip("一次完整睡眠总共消耗的饥饿能量占比（基于 Max_Carbohydrates*1 + Max_Fat*0.5）")]
    public float sleepHungerConsumeRatioByMaxValue = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("一次完整睡眠总共消耗的水分占比（基于 Max_Water）")]
    public float sleepWaterConsumeRatioByMaxValue = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("一次完整睡眠总共恢复的血量占比（基于 MaxHp）")]
    public float sleepHealRatioByMaxHp = 0.5f;
    [Range(0f, 2f)]
    [Tooltip("维生素对总回血的加成系数，最终倍率 = 1 + 维生素比例 * 本系数")]
    public float vitaminHealBonusRatio = 0.5f;
    [Range(0f, 1f)]
    public float deficiencyThresholdRatio = 0.25f;
    [Range(0f, 1f)]
    public float earlyWakeBaseChancePerTick = 0.2f;
    [Tooltip("睡眠时昼夜系统的时间流逝倍率")]
    public float sleepTimeScale = 12f;

    #endregion

    #region 运行时状态

    private BasePanel sleepPanel;
    private CanvasGroup sleepCanvasGroup;
    private Item currentPlayer;
    private Coroutine sleepingRoutine;
    private Coroutine panelFadeRoutine;
    private Coroutine zzzFloatRoutine;
    private RectTransform sleepPanelRect;
    private readonly List<ZzzFloatNode> zzzFloatNodes = new();
    private bool movementInputLocked;
    private InputAction cachedMoveAction;
    private InputAction cachedShiftAction;
    private bool dayTimeScaleApplied;
    private float cachedDayTimeScale = 1f;
    private string cachedTimeScaleSceneName;

    #endregion

    #region 模块生命周期

    /// <summary>建立帐篷模块的稳定数据身份。</summary>
    public override void Awake()
    {
        EnsureModuleData();
        base.Awake();
    }

    /// <summary>帐篷没有持续运行态，加载时只校正模块数据。</summary>
    public override void Load()
    {
        EnsureModuleData();
    }

    /// <summary>帐篷当前没有额外持久化状态。</summary>
    public override void Save()
    {
        EnsureModuleData();
    }

    /// <summary>回收模块时释放输入锁、时间倍率与临时睡眠面板。</summary>
    public override void Unload()
    {
        if (sleepingRoutine != null)
            StopCoroutine(sleepingRoutine);
        if (panelFadeRoutine != null)
            StopCoroutine(panelFadeRoutine);
        StopZzzFloat();
        UnlockPlayerMovementInput();
        RestoreDayNightTimeScale();

        if (sleepPanel != null)
            UIManager.ExistingInstance?.DestroyPanel(sleepPanel);

        sleepPanel = null;
        sleepCanvasGroup = null;
        sleepPanelRect = null;
        basePanel = null;
        currentPlayer = null;
        sleepingRoutine = null;
        panelFadeRoutine = null;
    }

    /// <summary>帐篷不参与统一 Tick。</summary>
    public override void ModUpdate(float deltaTime)
    {
    }

    /// <summary>保证旧 Prefab 反序列化后也拥有可注册的模块数据。</summary>
    private void EnsureModuleData()
    {
        ModData ??= new Ex_ModData();
        ModData.ID = TentModuleId;
    }

    #endregion

    #region 帐篷交互

    public void OnInteractStart(Item playerItem)
    {
        currentPlayer = playerItem;

        if (!EnsureSleepPanel())
            return;

        BindSleepUI();
        FadePanel(1f, true);
        OnClickSleep();
    }

    public void OnInteractCancel(Item playerItem)
    {
        if (sleepingRoutine != null)
        {
            StopCoroutine(sleepingRoutine);
            sleepingRoutine = null;
            Debug.Log("[Tent] 交互被取消，睡眠提前结束");
        }

        StopZzzFloat();
        UnlockPlayerMovementInput();
        RestoreDayNightTimeScale();
        FadePanel(0f, false);
    }

    #endregion

    #region UI与绑定

    private bool EnsureSleepPanel()
    {
        if (sleepPanel != null)
            return true;

        if (sleepPanel_Prefab == null)
        {
            Debug.LogError("[Tent] sleepPanel_Prefab 未配置，无法打开睡觉界面");
            return false;
        }

        sleepPanel = UIManager.Instance.CreatePanelFromGameObject(sleepPanel_Prefab);
        basePanel = sleepPanel.gameObject;
        sleepCanvasGroup = sleepPanel.canvasGroup;
        sleepPanelRect = sleepPanel.GetComponent<RectTransform>();

        BuildZzzFloatNodes();
        sleepPanel.Close();
        return true;
    }

    private void BindSleepUI()
    {
        var cancelButton = sleepPanel.GetButton(cancelButtonName);
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(OnClickCancel);
            cancelButton.onClick.AddListener(OnClickCancel);
        }
    }

    private void FadePanel(float targetAlpha, bool openAfterFade)
    {
        if (sleepPanel == null)
            return;

        if (panelFadeRoutine != null)
            StopCoroutine(panelFadeRoutine);

        panelFadeRoutine = StartCoroutine(CoFadePanel(targetAlpha, openAfterFade));
    }

    private IEnumerator CoFadePanel(float targetAlpha, bool openAfterFade)
    {
        sleepPanel.Open();

        float from = sleepCanvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < panelFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / panelFadeDuration);
            sleepCanvasGroup.alpha = Mathf.Lerp(from, targetAlpha, t);
            yield return null;
        }

        sleepCanvasGroup.alpha = targetAlpha;
        sleepCanvasGroup.interactable = openAfterFade;
        sleepCanvasGroup.blocksRaycasts = openAfterFade;

        if (!openAfterFade)
            sleepPanel.Close();

        panelFadeRoutine = null;
    }

    #endregion

    #region 睡觉流程

    private void OnClickSleep()
    {
        if (currentPlayer == null)
        {
            Debug.LogError("[Tent] currentPlayer 为空，无法开始睡觉");
            return;
        }

        if (sleepingRoutine != null)
            return;

        sleepingRoutine = StartCoroutine(CoSleep(currentPlayer));
    }

    private void OnClickCancel()
    {
        OnInteractCancel(currentPlayer);
    }

    private IEnumerator CoSleep(Item playerItem)
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("[Tent] GameManager 未初始化，无法自动保存");
            sleepingRoutine = null;
            yield break;
        }

        var food = playerItem.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
        var hp = playerItem.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (food == null || hp == null)
        {
            Debug.LogError("[Tent] 玩家缺少 Mod_Food 或 DamageReceiver，无法执行睡眠结算");
            sleepingRoutine = null;
            yield break;
        }

        if (!LockPlayerMovementInput(playerItem))
        {
            sleepingRoutine = null;
            yield break;
        }

        ApplySleepDayNightTimeScale();

        // 记录睡觉点作为玩家濒死后的重生优先点
        var deathState = playerItem.itemMods.GetMod_ByID<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId);
        if (deathState != null)
        {
            deathState.SetSleepRespawnPoint(playerItem.transform.position);
        }

        GameManager.Instance.SaveGame();
        StartZzzFloat();

        int totalTicks = Mathf.Max(1, Mathf.CeilToInt(sleepDuration / Mathf.Max(0.01f, sleepTickInterval)));
        SleepPlan sleepPlan = BuildSleepPlan(food, hp, totalTicks);

        float elapsed = 0f;
        bool earlyWake = false;
        while (elapsed < sleepDuration)
        {
            elapsed += sleepTickInterval;

            ConsumeSleepNutrition(food, sleepPlan.hungerConsumePerTick, sleepPlan.waterConsumePerTick);
            HealBySleep(hp, playerItem, sleepPlan.healPerTick);

            if (ShouldWakeEarly(food))
            {
                earlyWake = true;
                break;
            }

            yield return new WaitForSeconds(sleepTickInterval);
        }

        sleepingRoutine = null;
        StopZzzFloat();
        UnlockPlayerMovementInput();
        RestoreDayNightTimeScale();
        Debug.Log(earlyWake ? "[Tent] 玩家在睡觉中途被饿醒/渴醒" : "[Tent] 睡眠结束");
        FadePanel(0f, false);
    }

    private bool ShouldWakeEarly(Mod_Food food)
    {
        var nutrition = food.Data.nutrition;
        float proteinRatio = nutrition.Max_Protein <= 0f
            ? 0f
            : nutrition.Protein / nutrition.Max_Protein;
        float waterRatio = nutrition.Max_Water <= 0f
            ? 0f
            : nutrition.Water / nutrition.Max_Water;

        if (proteinRatio >= deficiencyThresholdRatio && waterRatio >= deficiencyThresholdRatio)
            return false;

        float proteinDeficit = Mathf.Clamp01((deficiencyThresholdRatio - proteinRatio) / deficiencyThresholdRatio);
        float waterDeficit = Mathf.Clamp01((deficiencyThresholdRatio - waterRatio) / deficiencyThresholdRatio);
        float severity = Mathf.Max(proteinDeficit, waterDeficit);
        float chance = Mathf.Clamp01(earlyWakeBaseChancePerTick * (0.5f + severity));
        return Random.value < chance;
    }

    #endregion

    #region 食物消耗

    private void ConsumeSleepNutrition(Mod_Food food, float hungerConsumePerTick, float waterConsumePerTick)
    {
        var nutrition = food.Data.nutrition;

        // 饥饿能量消耗使用 Nutrition 基类统一逻辑：碳水 -> 脂肪 -> 蛋白质。
        nutrition.TryConsumeEnergy(hungerConsumePerTick);

        // 水分仍独立按配置扣减。
        nutrition.Water = Mathf.Max(0f, nutrition.Water - waterConsumePerTick);
    }

    #endregion

    #region 血量恢复

    private void HealBySleep(DamageReceiver hp, Item playerItem, float healPerTick)
    {
        hp.Heal(healPerTick, playerItem);
    }

    private SleepPlan BuildSleepPlan(Mod_Food food, DamageReceiver hp, int totalTicks)
    {
        var nutrition = food.Data.nutrition;
        float vitaminRatio = nutrition.Max_Vitamins <= 0f
            ? 0f
            : Mathf.Clamp01(nutrition.Vitamins / nutrition.Max_Vitamins);

        float maxHungerEnergy = nutrition.Max_Carbohydrates + nutrition.Max_Fat * 0.5f;
        float totalHungerConsume = maxHungerEnergy * sleepHungerConsumeRatioByMaxValue;
        float totalWaterConsume = nutrition.Max_Water * sleepWaterConsumeRatioByMaxValue;
        float totalHeal = hp.MaxHp * sleepHealRatioByMaxHp * (1f + vitaminRatio * vitaminHealBonusRatio);

        return new SleepPlan
        {
            hungerConsumePerTick = totalHungerConsume / totalTicks,
            waterConsumePerTick = totalWaterConsume / totalTicks,
            healPerTick = totalHeal / totalTicks
        };
    }

    private struct SleepPlan
    {
        public float hungerConsumePerTick;
        public float waterConsumePerTick;
        public float healPerTick;
    }

    #endregion

    #region 时间缩放

    private void ApplySleepDayNightTimeScale()
    {
        if (dayTimeScaleApplied)
            return;

        if (DayTimeSystem.Instance == null)
        {
            Debug.LogError("[Tent] DayTimeSystem 不存在，无法在睡眠中加速时间流逝");
            return;
        }

        string sceneName = SceneManager.GetActiveScene().name;
        cachedTimeScaleSceneName = sceneName;

        if (!DayTimeSystem.Instance.WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
        {
            DayTimeSystem.Instance.InitializeSceneTimeData(sceneName);
            if (!DayTimeSystem.Instance.WorldTimeDict.TryGetValue(sceneName, out timeData))
            {
                Debug.LogError($"[Tent] 场景 {sceneName} 的时间数据初始化失败，无法设置睡眠时间倍率");
                return;
            }
        }

        cachedDayTimeScale = timeData.TimeScaleModifier;
        DayTimeSystem.Instance.SetTimeScale(sceneName, sleepTimeScale);
        dayTimeScaleApplied = true;
    }

    private void RestoreDayNightTimeScale()
    {
        if (!dayTimeScaleApplied)
            return;

        if (DayTimeSystem.Instance == null)
        {
            Debug.LogError("[Tent] DayTimeSystem 不存在，无法恢复时间流逝倍率");
            dayTimeScaleApplied = false;
            return;
        }

        if (string.IsNullOrEmpty(cachedTimeScaleSceneName))
            cachedTimeScaleSceneName = SceneManager.GetActiveScene().name;

        DayTimeSystem.Instance.SetTimeScale(cachedTimeScaleSceneName, cachedDayTimeScale);
        dayTimeScaleApplied = false;
    }

    #endregion

    #region 输入锁定

    private bool LockPlayerMovementInput(Item playerItem)
    {
        if (movementInputLocked)
            return true;

    var controller = playerItem.itemMods.GetMod_ByID<GameController>(ModText.Controller);
    if (controller == null)
    {
        Debug.LogError("[Tent] 玩家缺少 GameController，无法禁用移动输入");
        return false;
    }

    var inputActions = controller._inputActions;
    if (inputActions == null)
    {
        Debug.LogError("[Tent] GameController._inputActions 为空，无法禁用移动输入");
        return false;
    }

    cachedMoveAction = inputActions.Win10.Move_Player;
    cachedShiftAction = inputActions.Win10.Shift;

    cachedMoveAction?.Disable();
    cachedShiftAction?.Disable();
    movementInputLocked = true;

    var mover = playerItem.itemMods.GetMod_ByID<Mover>(ModText.Mover);
    mover?.SetRunState(false);

        return true;
    }

    private void UnlockPlayerMovementInput()
    {
        if (!movementInputLocked)
            return;

        cachedMoveAction?.Enable();
        cachedShiftAction?.Enable();
        movementInputLocked = false;
    }

    #endregion

    #region ZZZ漂浮动画

    private sealed class ZzzFloatNode
    {
        public RectTransform rect;
        public Vector2 velocity;
    }

    private void BuildZzzFloatNodes()
    {
        zzzFloatNodes.Clear();

    if (sleepPanel == null)
    {
        Debug.LogError("[Tent] 睡眠面板为空，无法初始化 ZZZ 漂浮节点");
        return;
    }

    Transform root = sleepPanel.transform.Find(zzzRootName);
    if (root == null)
    {
        Debug.LogError($"[Tent] 未找到 ZZZ 根节点: {zzzRootName}");
        return;
    }

    for (int i = 0; i < root.childCount; i++)
    {
        RectTransform child = root.GetChild(i) as RectTransform;
        if (child == null)
        {
            Debug.LogError($"[Tent] ZZZ 子节点不是 RectTransform: {root.GetChild(i).name}");
            continue;
        }

        Vector2 dir = Random.insideUnitCircle;
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.right;
        dir.Normalize();

        zzzFloatNodes.Add(new ZzzFloatNode
        {
            rect = child,
            velocity = dir * Random.Range(zzzMinSpeed, zzzMaxSpeed)
        });
    }

    if (zzzFloatNodes.Count == 0)
        Debug.LogError("[Tent] ZZZ 根节点下没有可漂浮的子对象");
    }

    private void StartZzzFloat()
    {
        if (sleepPanelRect == null)
        {
            Debug.LogError("[Tent] sleepPanelRect 为空，无法启动 ZZZ 漂浮");
            return;
        }

    if (zzzFloatNodes.Count == 0)
        BuildZzzFloatNodes();

    if (zzzFloatNodes.Count == 0)
        return;

    if (zzzFloatRoutine != null)
        StopCoroutine(zzzFloatRoutine);

        zzzFloatRoutine = StartCoroutine(CoZzzFloat());
    }

    private void StopZzzFloat()
    {
        if (zzzFloatRoutine == null)
            return;

        StopCoroutine(zzzFloatRoutine);
        zzzFloatRoutine = null;
    }

    private IEnumerator CoZzzFloat()
    {
        while (true)
        {
            float panelWidth = sleepPanelRect.rect.width;
            float panelHeight = sleepPanelRect.rect.height;

        foreach (var node in zzzFloatNodes)
        {
            if (node.rect == null)
                continue;

            Vector2 pos = node.rect.anchoredPosition + node.velocity * Time.deltaTime;
            Vector2 halfSize = node.rect.rect.size * 0.5f;

            float minX = -panelWidth * 0.5f + halfSize.x + zzzEdgePadding;
            float maxX = panelWidth * 0.5f - halfSize.x - zzzEdgePadding;
            float minY = -panelHeight * 0.5f + halfSize.y + zzzEdgePadding;
            float maxY = panelHeight * 0.5f - halfSize.y - zzzEdgePadding;

            if (pos.x < minX)
            {
                pos.x = minX;
                node.velocity.x = Mathf.Abs(node.velocity.x);
            }
            else if (pos.x > maxX)
            {
                pos.x = maxX;
                node.velocity.x = -Mathf.Abs(node.velocity.x);
            }

            if (pos.y < minY)
            {
                pos.y = minY;
                node.velocity.y = Mathf.Abs(node.velocity.y);
            }
            else if (pos.y > maxY)
            {
                pos.y = maxY;
                node.velocity.y = -Mathf.Abs(node.velocity.y);
            }

            node.rect.anchoredPosition = pos;
        }

            yield return null;
        }
    }

    #endregion

}
