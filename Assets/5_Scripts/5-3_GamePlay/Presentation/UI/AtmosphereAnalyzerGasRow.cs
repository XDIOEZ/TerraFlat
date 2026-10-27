using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正式成分行模板同时显示气体名称、百分比和同尺度条形读数。</summary>
public sealed class AtmosphereAnalyzerGasRow : MonoBehaviour
{
    #region 成分行控件与读数
    [SerializeField] private TMP_Text gasName;
    [SerializeField] private TMP_Text percentage;
    [SerializeField] private Image proportionFill;
    public string FluidId { get; private set; }
    public decimal Fraction { get; private set; }
    public void RenderComponent(AtmosphereMeasurementComponent component, string displayName)
    {
        FluidId = component.FluidId;
        Fraction = component.Fraction;
        AtmosphereAnalyzerPanel.SetDynamicText(gasName, displayName);
        decimal percent = component.Fraction * 100m;
        string value = percent > 0m && percent < .001m ? "<0.001%" :
            percent.ToString("0.###", CultureInfo.CurrentCulture) + "%";
        AtmosphereAnalyzerPanel.SetDynamicText(percentage, value);
        RectTransform bar = proportionFill.rectTransform;
        bar.anchorMax = new Vector2(Mathf.Clamp01((float)component.Fraction), 1f);
    }
    #endregion
}
