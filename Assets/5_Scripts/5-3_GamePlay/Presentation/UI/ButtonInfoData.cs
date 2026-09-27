using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>保存列表条目的稳定键与右键菜单入口；Name 对角色表示 ID。</summary>
public class ButtonInfoData : MonoBehaviour, IPointerClickHandler
{
    #region 条目数据与菜单

    public string Name;
    public string Path;

    public Image SelectImage;

    // 当物体被点击时调用
    public void OnPointerClick(PointerEventData eventData)
    {
        // 右键点击
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            OnContextMenu(eventData.position);
        }
    }

    // 右键菜单调用
    public void OnContextMenu(Vector2 point)
    {
        GameManager.Instance?.OpenContextMenu();
        if (SaveMenuRightMenuUI.Instance == null)
            return;
        SaveMenuRightMenuUI.Instance.SelectInfo = this;
        SaveMenuRightMenuUI.Instance.OpenUI(point);
    }

    #endregion
}
