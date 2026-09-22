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
        current.actor = owner;
        target.Changed += current.Refresh;

        BuildingPanelActions buildingActions = current.GetComponent<BuildingPanelActions>();
        if (buildingActions == null)
            throw new InvalidOperationException("燃料交互面板缺少 BuildingPanelActions。");
        buildingActions.Bind(target.item);

        current.panel.Open();
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
            "拖入燃料补充燃料；熄灭后拖入火种可重新点燃。");

        fuelBar.SetValueWithoutNotify(interaction.FuelRatio);
    }

    private void Close() => panel?.Close();

    private void ClearTarget()
    {
        if (interaction != null)
            interaction.Changed -= Refresh;
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
