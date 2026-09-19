using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>医疗部位选择与引导表现。仅克隆正式 Prefab 的按钮模板，不假设玩家拥有固定数量的部位。</summary>
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
    private GameController controller;
    private DamageReceiver receiver;
    private BodyPartType selectedPart;
    private bool channeling;
    private float elapsed;
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
        receiver = owner.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        controller = owner.itemMods.GetMod_ByID<GameController>(ModText.Controller);
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
        controller?.AcquireGameplayInputLock(this);
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
        controller?.ReleaseGameplayInputLock(this);
        controller = null;
        actor = null;
        receiver = null;
        treatment = null;
        channeling = false;
        elapsed = 0f;
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
        if (channeling || treatment == null || !treatment.CanTreat(actor, part, out _)) return;
        selectedPart = part;
        elapsed = 0f;
        channeling = true;
        RefreshButtons();
    }

    private void Update()
    {
        if (panel == null || !panel.IsOpen()) return;
        if (treatment == null || !treatment.CanUse(actor) || receiver == null)
        {
            panel.Close();
            return;
        }
        if (channeling)
        {
            if (!treatment.CanTreat(actor, selectedPart, out string reason))
            {
                channeling = false;
                Status.text = FlatWorldLocalizationService.GetUiText(reason);
                Progress.value = 0f;
                RefreshButtons();
                return;
            }
            elapsed += Time.deltaTime;
            float duration = treatment.channelDurationSeconds;
            Progress.value = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            Status.text = FlatWorldLocalizationService.GetUiFormat("正在固定{0}　{1:0.0} / {2:0.0} 秒",
                GetPartName(selectedPart), Mathf.Min(elapsed, duration), duration);
            if (elapsed >= duration)
            {
                channeling = false;
                bool completed = treatment.TryCompleteTreatment(actor, selectedPart);
                if (completed) panel.Close();
                else
                {
                    Status.text = FlatWorldLocalizationService.GetUiText("治疗未完成，物品未消耗。请重新选择部位。");
                    RefreshButtons();
                }
            }
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
            buttons[i].interactable = !channeling && allowed;
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
