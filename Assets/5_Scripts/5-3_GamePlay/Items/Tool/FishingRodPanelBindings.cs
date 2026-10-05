using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正式钓具 UI 的序列化引用；布局由编辑器构建，不在运行时拼接。</summary>
public sealed class FishingRodPanelBindings : MonoBehaviour
{
    public ItemSlot_UI HookSlot;
    public ItemSlot_UI BaitSlot;
    public TMP_Text Status;
    public Button CloseButton;
}
