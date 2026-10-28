#if UNITY_EDITOR
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 目录共用布局回归
    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterCatalogLayoutRules() => GameplayTestSystemProbe.Register(
        "ui_settings", "gm_catalog_layout", CaptureCatalogLayoutRules);

    // 用正式构建入口检查两个目录的共用骨架和独立输入控件。
    private static JObject CaptureCatalogLayoutRules(JObject arguments)
    {
        var controller = new GameObject("GM catalog rules controller");
        controller.SetActive(false);
        var canvas = new GameObject("GM catalog rules canvas", typeof(RectTransform), typeof(Canvas));
        try
        {
            var console = controller.AddComponent<GMReflectionConsole>();
            console.BuildAirdropBrowser(canvas.transform);
            console.BuildAiCreatureBrowser(canvas.transform);
            var airdrop = console.airdropBrowserRoot;
            var creatures = console.aiCreatureBrowserRoot;
            bool Frame(GameObject browser)
            {
                var rect = browser.GetComponent<RectTransform>();
                var layout = browser.GetComponent<VerticalLayoutGroup>();
                return rect.sizeDelta == new Vector2(1120f, 760f) && rect.pivot == new Vector2(.5f, .5f) &&
                    layout.padding.left == 20 && layout.padding.top == 18 && layout.spacing == 10f &&
                    browser.transform.Find("Header").GetComponent<LayoutElement>().preferredHeight == CatalogBrowserHeaderHeight &&
                    browser.transform.Find("Toolbar").GetComponent<LayoutElement>().preferredHeight == CatalogBrowserToolbarHeight;
            }
            return new JObject
            {
                ["sameFrame"] = Frame(airdrop) && Frame(creatures),
                ["startClosed"] = !airdrop.activeSelf && !creatures.activeSelf,
                ["independentInputs"] = console.airdropSearchInput != console.aiCreatureSearchInput &&
                    console.amountInput != console.aiCreatureAmountInput,
                ["amountDefaults"] = console.amountInput.text == "1" && console.aiCreatureAmountInput.text == "1",
                ["searchSize"] = console.airdropSearchInput.GetComponent<LayoutElement>().preferredWidth == 560f &&
                    console.aiCreatureSearchInput.GetComponent<LayoutElement>().preferredWidth == 560f,
                ["sameGridAlignment"] = console.airdropItemGridLayout.childAlignment == TextAnchor.UpperCenter &&
                    console.aiCreatureGridLayout.childAlignment == TextAnchor.UpperCenter,
                ["separateContents"] = console.airdropItemGrid != console.aiCreatureGrid &&
                    console.airdropItemGrid.IsChildOf(airdrop.transform) && console.aiCreatureGrid.IsChildOf(creatures.transform)
            };
        }
        finally
        {
            DestroyImmediate(canvas);
            DestroyImmediate(controller);
        }
    }
    #endregion
}
#endif
