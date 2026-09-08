using System.Globalization;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正式设置 Prefab 的四季输入页；输入先留在草稿中，应用后统一写入世界，取消不改变日历。</summary>
public sealed class SeasonSettingsPanel : MonoBehaviour, ISettingsPageLifecycle
{
    public const string PageName = "设置分页_季节";
    private readonly TMP_InputField[] inputs = new TMP_InputField[4]; // 春夏秋冬输入
    private readonly float[] draft = new float[4]; // 当前输入草稿
    private TextMeshProUGUI status; // 状态与错误提示
    private Button apply; // 原子应用按钮
    private Button cancel; // 丢弃草稿按钮

    /// <summary>绑定 Prefab 中已经存在的控件，不在运行时创建视觉节点。</summary>
    private void Awake()
    {
        TMP_InputField[] fields = GetComponentsInChildren<TMP_InputField>(true);
        foreach (TMP_InputField field in fields)
            for (int index = 0; index < inputs.Length; index++)
                if (field.name == "季节天数_" + index)
                    inputs[index] = field;
        foreach (TextMeshProUGUI text in GetComponentsInChildren<TextMeshProUGUI>(true))
            if (text.name == "状态文本") status = text;
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (button.name == "应用按钮") apply = button;
            if (button.name == "取消按钮") cancel = button;
        }
        apply.onClick.AddListener(Apply);
        cancel.onClick.AddListener(Refresh);
    }

    /// <summary>释放本页添加的按钮监听。</summary>
    private void OnDestroy()
    {
        if (apply != null) apply.onClick.RemoveListener(Apply);
        if (cancel != null) cancel.onClick.RemoveListener(Refresh);
    }

    /// <summary>按稳定 Provider 身份取得季节业务入口。</summary>
    private static SeasonSettingsProvider GetProvider() =>
        SettingsProviderRegistry.TryGet(SeasonSettingsProvider.Id, out ISettingsProvider provider)
            ? provider as SeasonSettingsProvider : null;

    /// <summary>页面出现时从已生效的世界设置重新填入草稿。</summary>
    public void OnSettingsPageShown() => Refresh();

    /// <summary>草稿只存在于输入框中，离开页面不会提交。</summary>
    public void OnSettingsPageHidden() { }

    /// <summary>读取四季长度，未知世界只提示状态而不建立默认世界数据。</summary>
    private void Refresh()
    {
        SeasonSettingsProvider provider = GetProvider();
        if (provider == null || !provider.TryRead(out SeasonCycleSettings settings))
        {
            status.text = FlatWorldLocalizationService.GetUiText("请先进入世界。");
            apply.interactable = false;
            return;
        }
        apply.interactable = true;
        for (int index = 0; index < inputs.Length; index++)
            inputs[index].SetTextWithoutNotify(settings.GetDays((WorldSeason)index).ToString("0.###", CultureInfo.InvariantCulture));
        status.text = FlatWorldLocalizationService.GetUiFormat("一年共 {0} 天；调整会保留当前季节进度。", settings.YearDays);
    }

    /// <summary>校验数字格式后交由 Provider 一次提交，任何错误均保留原世界设置。</summary>
    private void Apply()
    {
        for (int index = 0; index < inputs.Length; index++)
        {
            if (!float.TryParse(inputs[index].text, NumberStyles.Float, CultureInfo.InvariantCulture, out draft[index]))
            {
                status.text = FlatWorldLocalizationService.GetUiText("每个季节的天数必须是大于 0 的有限数值。");
                return;
            }
        }
        SeasonSettingsProvider provider = GetProvider();
        if (provider == null)
        {
            Refresh();
            return;
        }
        if (!provider.TryApply(draft[0], draft[1], draft[2], draft[3], out string error))
        {
            status.text = FlatWorldLocalizationService.GetUiText(error);
            return;
        }
        Refresh();
    }
}
