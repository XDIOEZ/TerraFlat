using UnityEngine;

/// <summary>只安排显示回调先于镜头，不改变物品管理器的 Awake 和初始化顺序。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-2000)]
public sealed class ItemPresentationFrameDriver : MonoBehaviour
{
    #region 独立显示帧顺序
    private ItemMgr owner;
    private void OnEnable() => owner = GetComponent<ItemMgr>();
    private void LateUpdate()
    {
        if (owner != null) owner.UpdateItemPresentationFrame();
    }
    #endregion
}
