using System.Globalization;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正式设置 Prefab 的四季只读页；季节长度由轨道物理自动派生。</summary>
public sealed class SeasonSettingsPanel : MonoBehaviour, ISettingsPageLifecycle
{
    public const string PageName = "设置分页_季节";
    private readonly TMP_InputField[] inputs = new TMP_InputField[4]; // 春夏秋冬输入
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
        cancel.onClick.AddListener(Refresh);
    }

    /// <summary>释放本页添加的按钮监听。</summary>
    private void OnDestroy()
    {
        if (cancel != null) cancel.onClick.RemoveListener(Refresh);
    }

    /// <summary>按稳定 Provider 身份取得季节业务入口。</summary>
    private static SeasonSettingsProvider GetProvider() =>
        SettingsProviderRegistry.TryGet(SeasonSettingsProvider.Id, out ISettingsProvider provider)
            ? provider as SeasonSettingsProvider : null;

    /// <summary>页面出现时从已生效的世界设置刷新派生季节长度。</summary>
    public void OnSettingsPageShown() => Refresh();

    /// <summary>只读页离开时无需提交。</summary>
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
        apply.interactable = false;
        for (int index = 0; index < inputs.Length; index++)
        {
            inputs[index].interactable = false;
            inputs[index].SetTextWithoutNotify(settings.GetDays((WorldSeason)index).ToString("0.###", CultureInfo.InvariantCulture));
        }
        status.text = FlatWorldLocalizationService.GetUiFormat(
            "一年共 {0:0.###} 天；季节长度由公转周期与轨道离心率自动计算。", settings.YearDays);
    }
}
