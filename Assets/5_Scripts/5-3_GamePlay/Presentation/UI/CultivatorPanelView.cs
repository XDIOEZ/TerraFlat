using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>培育器正式视图只持有控件，种植进度和收获均来自领域状态。</summary>
public sealed class CultivatorPanelView : MonoBehaviour
{
    #region 序列化控件
    public ItemSlot_UI[] SeedSlots;
    public ItemSlot_UI[] HarvestSlots;
    public ItemSlot_UI FertilizerSlot;
    public Button WaterButton;
    public Button[] ClearPlotButtons;
    public TMP_Text Status;
    #endregion
}
