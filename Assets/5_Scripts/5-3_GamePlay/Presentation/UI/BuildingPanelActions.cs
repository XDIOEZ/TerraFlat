using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 便携设施面板的通用建筑操作：手持时进入放置模式，落地后拆回带状态的物品。
/// 仅绑定正式 Prefab 中的按钮，不创建 UI，不保管容器数据，也不绕过建筑事务。
/// </summary>
public sealed class BuildingPanelActions : MonoBehaviour
{
    public Button PlaceButton; // 手持设施的放置入口。
    public Button DismantleButton; // 世界建筑的拆回入口。
    private BasePanel panel;
    private Mod_Building building;

    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        PlaceButton.onClick.AddListener(Place);
        DismantleButton.onClick.AddListener(Dismantle);
        panel.Closed += ClearTarget;
    }

    /// <summary>根据真实物品角色切换操作，普通地面掉落物不能临时冒充手持召唤器。</summary>
    public void Bind(Item target)
    {
        building = target?.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
        PlaceButton.gameObject.SetActive(building != null && building.IsSummoner);
        PlaceButton.interactable = building != null && building.IsItemInInventory;
        DismantleButton.gameObject.SetActive(building != null && building.CanCommitDismantle);
        DismantleButton.interactable = building != null && !building.IsDismantlePending;
    }

    /// <summary>先进入放置模式，再关闭模态界面释放输入，由下一次使用提交建筑。</summary>
    private void Place()
    {
        if (building != null && building.BeginPlacement())
            panel.Close();
    }

    /// <summary>关闭面板后走既有拆除事务，生成返还物成功后才删除建筑。</summary>
    private void Dismantle()
    {
        Mod_Building target = building;
        if (target == null || !target.CanCommitDismantle || target.IsDismantlePending)
            return;
        panel.Close();
        target.UnInstall();
    }

    private void ClearTarget() => building = null;

    private void OnDestroy()
    {
        if (panel != null) panel.Closed -= ClearTarget;
        if (PlaceButton != null) PlaceButton.onClick.RemoveListener(Place);
        if (DismantleButton != null) DismantleButton.onClick.RemoveListener(Dismantle);
    }
}
