using System;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 通用燃料交互面板：显示燃烧/燃料状态并接收库存投料。
/// 具体物品名称来自当前定义，面板本身不认识任何具体物品类型。
/// </summary>
public sealed class FuelInteractionPanel : MonoBehaviour, IInventoryDragDropTarget
{
    #region 运行时绑定

    public const string PrefabKey = "UI_FuelInteraction";
    private static FuelInteractionPanel current;

    private BasePanel panel;
    private TMP_Text title;
    private TMP_Text status;
    private TMP_Text hint;
    private Slider fuelBar;
    private RectTransform inputDropZone;
    private ItemSlot_UI ignitionSlot;
    private Button igniteButton;
    private Button extinguishButton;
    private Mod_FuelInteraction interaction;
    private Item actor;
    private float nextRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        Mod_FuelInteraction.OpenRequested -= Show;
        Mod_FuelInteraction.OpenRequested += Show;
    }

    private static void Show(Mod_FuelInteraction target, Item owner)
    {
        if (target == null || owner == null)
            return;

        if (current == null)
        {
            GameObject prefab = GameRes.Instance?.GetPrefab(PrefabKey, false);
            if (prefab == null)
                throw new InvalidOperationException($"缺少通用燃料交互 UI Prefab：{PrefabKey}");
            current = UIManager.Instance.CreatePanelFromGameObject(prefab).GetComponent<FuelInteractionPanel>();
        }

        current.ClearTarget();
        current.interaction = target;
        current.panel.BindPhysicalDeviceArtwork(target.item.itemData.IDName, () =>
            new PhysicalDeviceArtworkView.OperatingState { Burning = target.IsBurning, Heat = target.IsBurning ? 1f : 0f, Fuel = target.FuelRatio });
        current.actor = owner;
        target.Changed += current.Refresh;
        Inventory tinder = target.IgnitionInventory;
        tinder.RuntimeAccessGuard = () => current != null && current.panel.IsOpen() &&
            current.interaction == target && target.CanOperate(owner);
        tinder.DefaultTarget_Inventory = owner.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        tinder.BindSlotUI(current.ignitionSlot, 0);
        tinder.SyncData();

        BuildingPanelActions buildingActions = current.GetComponent<BuildingPanelActions>();
        if (buildingActions == null)
            throw new InvalidOperationException("燃料交互面板缺少 BuildingPanelActions。");
        buildingActions.Bind(target.item);

        // 交给 UIManager 按当前面板层级处理返回，不单独维护 ESC 状态。
        current.panel.PrepareForGameplayWindow();
        current.panel.Open();
        tinder.SyncQuickTransferTarget(current.panel);
        current.Refresh();
    }

    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        title = panel.GetText("标题");
        status = panel.GetText("燃料状态");
        hint = panel.GetText("说明文本");
        fuelBar = FindNamedComponent<Slider>("燃料进度");
        inputDropZone = FindNamedComponent<RectTransform>("燃料输入槽");
        ignitionSlot = FindNamedComponent<ItemSlot_UI>("火种插槽");
        igniteButton = panel.GetButton("点燃按钮");
        extinguishButton = panel.GetButton("熄灭按钮");
        igniteButton.onClick.AddListener(() => interaction?.TryIgniteFromStoredItem(actor));
        extinguishButton.onClick.AddListener(() => interaction?.TryExtinguish(actor));

        panel.GetButton("关闭按钮").onClick.AddListener(Close);
        panel.Closed += ClearTarget;
    }

    private T FindNamedComponent<T>(string objectName) where T : Component
    {
        T[] components = GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i].name == objectName)
                return components[i];
        }

        throw new InvalidOperationException($"燃料交互面板缺少节点：{objectName}");
    }

    #endregion

    #region 显示与生命周期

    private void OnEnable()
    {
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
    }

    private void OnDisable()
    {
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
    }

    private void HandleLanguageChanged(string _)
    {
        if (panel != null && panel.IsOpen() && interaction != null)
            Refresh();
    }

    private void Update()
    {
        if (panel == null || !panel.IsOpen())
            return;
        if (interaction == null || !interaction.CanOperate(actor))
        {
            Close();
            return;
        }

        if (Time.unscaledTime < nextRefresh)
            return;
        nextRefresh = Time.unscaledTime + 0.2f;
        Refresh();
    }

    private void Refresh()
    {
        if (interaction == null)
            return;

        title.text = GameRes.Instance != null &&
                     GameRes.Instance.TryGetItemDefinition(
                         interaction.item.itemData.IDName,
                         out RuntimeItemDefinition definition)
            ? definition.DisplayName
            : FlatWorldLocalizationService.GetUiText("燃料");

        string state = interaction.IsBurning
            ? FlatWorldLocalizationService.GetUiText("燃烧中")
            : FlatWorldLocalizationService.GetUiText("已熄灭");
        status.text = FlatWorldLocalizationService.GetUiFormat(
            "{0}　燃料 {1:0} / {2:0} 秒",
            state,
            interaction.CurrentFuel,
            interaction.MaxFuel);
        hint.text = FlatWorldLocalizationService.GetUiText(
            "放入火种或温度达到500℃的物品后点击点燃；拖入燃料可补充燃料。");

        fuelBar.SetValueWithoutNotify(interaction.FuelRatio);
        MechanicalPanelView.SetButtonCaption(igniteButton, "点燃");
        MechanicalPanelView.SetButtonCaption(extinguishButton, "熄灭");
        igniteButton.gameObject.SetActive(!interaction.IsBurning);
        extinguishButton.gameObject.SetActive(interaction.IsBurning);
    }

    private void Close() => panel?.Close();

    private void ClearTarget()
    {
        if (interaction != null)
        {
            interaction.Changed -= Refresh;
            Inventory tinder = interaction.IgnitionInventory;
            ignitionSlot.ClearInventoryBinding(tinder);
            tinder.itemSlot_UI.Clear();
            tinder.DefaultTarget_Inventory = null;
            tinder.SyncQuickTransferTarget(panel);
            tinder.RuntimeAccessGuard = () => false;
        }
        interaction = null;
        actor = null;
    }

    private void OnDestroy()
    {
        ClearTarget();
        if (panel != null)
            panel.Closed -= ClearTarget;
    }

    #endregion

    #region 库存拖放

    public bool ContainsInventoryDropPoint(Vector2 screenPosition, Camera eventCamera)
    {
        return panel != null && panel.IsOpen() && inputDropZone != null &&
               RectTransformUtility.RectangleContainsScreenPoint(inputDropZone, screenPosition, eventCamera);
    }

    public bool TryAcceptInventoryDrag(
        InventoryDragTransaction transaction,
        Vector2 screenPosition,
        Camera eventCamera)
    {
        if (transaction == null || interaction == null || !interaction.CanOperate(actor) ||
            !ContainsInventoryDropPoint(screenPosition, eventCamera))
        {
            return false;
        }

        bool accepted = transaction.TryConsumeSourceItem(
            source => interaction.TryFeedFromInventoryItem(source, actor, transaction.DraggedAmount));
        if (accepted)
            Refresh();
        return accepted;
    }

    #endregion
}
