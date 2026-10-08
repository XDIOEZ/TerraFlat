using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只提交治疗选择、取消意图并显示会话进度，等待与完成由领域会话处理。</summary>
public sealed class BodyPartTreatmentPanel : MonoBehaviour
{
    #region 预置体绑定与状态

    public const string PrefabKey = "UI_BodyPartTreatment";
    public RectTransform PartRoot;
    public Button PartTemplate;
    public Button CancelButton;
    public TextMeshProUGUI Title;
    public TextMeshProUGUI Status;
    public Slider Progress;
    private static BodyPartTreatmentPanel current;
    private readonly List<Button> buttons = new();
    private readonly List<BodyPartType> parts = new();
    private BasePanel panel;
    private Mod_BodyPartTreatment treatment;
    private Item actor;
    private Mod_GameController controller;
    private Mod_DamageReceiver receiver;
    private BodyPartTreatmentSession session;
    private float nextRefresh;

    #endregion

    #region 生命周期

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        Mod_BodyPartTreatment.OpenRequested -= Show;
        Mod_BodyPartTreatment.OpenRequested += Show;
    }

    private static void Show(Mod_BodyPartTreatment target, Item owner)
    {
        if (target == null || !target.CanUse(owner)) return;
        if (current == null)
            current = UIManager.Instance.CreatePanelFromGameObject(GameRes.Instance.GetPrefab(PrefabKey))
                .GetComponent<BodyPartTreatmentPanel>();
        current.Bind(target, owner);
    }

    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        panel.SetGameplayInputBlocking(true);
        panel.Closed += ClearTarget;
        CancelButton.onClick.AddListener(panel.Close);
        PartTemplate.gameObject.SetActive(false);
        Progress.interactable = false;
    }

    private void Bind(Mod_BodyPartTreatment target, Item owner)
    {
        ClearTarget();
        treatment = target;
        actor = owner;
        receiver = owner.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        controller = owner.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        foreach (BodyPartHealth part in receiver.BodyParts)
        {
            if (part == null) continue;
            BodyPartType type = part.Part;
            Button button = Instantiate(PartTemplate, PartRoot, false);
            button.name = "部位_" + type;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Begin(type));
            button.gameObject.SetActive(true);
            buttons.Add(button);
            parts.Add(type);
        }
        Title.text = FlatWorldLocalizationService.GetUiText("选择治疗部位");
        Status.text = FlatWorldLocalizationService.GetUiText("选择受伤部位；关闭窗口或切换用品将取消引导，不消耗物品。");
        Progress.value = 0f;
        panel.Open();
        if (controller != null) controller.AcquireGameplayInputLock(this);
        RefreshButtons();
    }

    private void OnDisable() => ClearTarget();

    private void OnDestroy()
    {
        ClearTarget();
        if (panel != null) panel.Closed -= ClearTarget;
        if (current == this) current = null;
    }

    private void ClearTarget()
    {
        if (treatment != null && session != null) treatment.CancelTreatment(session);
        if (controller != null) controller.ReleaseGameplayInputLock(this);
        controller = null;
        actor = null;
        receiver = null;
        treatment = null;
        session = null;
        foreach (Button button in buttons)
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                Destroy(button.gameObject);
            }
        buttons.Clear();
        parts.Clear();
    }

    #endregion

    #region 部位与引导

    private void Begin(BodyPartType part)
    {
        if (session?.IsRunning == true || treatment == null) return;
        if (!treatment.TryBeginTreatment(actor, part, out string reason))
        {
            Status.text = FlatWorldLocalizationService.GetUiText(reason);
            return;
        }
        session = treatment.TreatmentSession;
        RefreshButtons();
    }

    private void Update()
    {
        if (panel == null || !panel.IsOpen()) return;
        if (session != null && !session.IsRunning)
        {
            if (session.State == BodyPartTreatmentSession.SessionState.Completed)
            {
                panel.Close();
                return;
            }
            Status.text = FlatWorldLocalizationService.GetUiText(session.Reason ?? "治疗已取消");
            Progress.value = 0f;
            session = null;
            RefreshButtons();
        }
        if (treatment == null || !treatment.CanUse(actor) || receiver == null)
        {
            panel.Close();
            return;
        }
        if (session?.IsRunning == true)
        {
            Progress.value = session.Progress;
            Status.text = FlatWorldLocalizationService.GetUiFormat("正在固定{0}　{1:0.0} / {2:0.0} 秒",
                GetPartName(session.TargetPart), session.ElapsedSeconds, session.DurationSeconds);
        }
        else if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.2f;
            RefreshButtons();
        }
    }

    private void RefreshButtons()
    {
        if (treatment == null || receiver == null) return;
        for (int i = 0; i < buttons.Count; i++)
        {
            BodyPartType part = parts[i];
            bool allowed = treatment.CanTreat(actor, part, out string reason);
            buttons[i].interactable = session?.IsRunning != true && allowed;
            if (!receiver.TryGetBodyPart(part, out BodyPartHealth state)) continue;
            string suffix = allowed ? "" : "  " + FlatWorldLocalizationService.GetUiText(reason);
            buttons[i].GetComponentInChildren<TextMeshProUGUI>(true).text =
                $"{GetPartName(part)}　{state.Hp:0.#} / {state.MaxHp:0.#}{suffix}";
        }
    }

    public static string GetPartName(BodyPartType part) => FlatWorldLocalizationService.GetUiText(part switch
    {
        BodyPartType.Head => "头部", BodyPartType.Chest => "胸部", BodyPartType.Abdomen => "腹部",
        BodyPartType.Pelvis => "骨盆", BodyPartType.LeftHand => "左手", BodyPartType.RightHand => "右手",
        BodyPartType.LeftLeg => "左腿", BodyPartType.RightLeg => "右腿", _ => part.ToString()
    });

    #endregion
}
