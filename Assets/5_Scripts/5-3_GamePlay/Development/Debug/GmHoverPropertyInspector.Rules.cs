#if UNITY_EDITOR
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

internal sealed partial class GmHoverPropertyInspector
{
    #region 悬停观察器状态归属回归
    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterHoverInspectorOwnershipRules() => GameplayTestSystemProbe.Register(
        "ui_settings", "gm_inspector_ownership", CaptureHoverInspectorOwnership);

    // 两个独立观察器实际读取不同对象，核对文本缓存与清理不串状态。
    private static JObject CaptureHoverInspectorOwnership(JObject arguments)
    {
        var firstTarget = new GameObject("Inspector first");
        var secondTarget = new GameObject("Inspector second");
        try
        {
            firstTarget.transform.position = new Vector3(12.5f, -3f, 0f);
            secondTarget.transform.position = new Vector3(-6f, 9f, 0f);
            var first = new GmHoverPropertyInspector { hoverInspectorTarget = firstTarget };
            var second = new GmHoverPropertyInspector { hoverInspectorTarget = secondTarget };
            first.RefreshHoverInspectorText();
            second.RefreshHoverInspectorText();
            string secondText = second.hoverInspectorText;
            bool separate = first.hoverInspectorText.Contains("Inspector first") &&
                !first.hoverInspectorText.Contains("Inspector second") &&
                secondText.Contains("Inspector second") && !secondText.Contains("Inspector first");
            first.ClearTarget();
            return new JObject
            {
                ["separateText"] = separate,
                ["separateBuffers"] = !ReferenceEquals(first.hoverInspectorBuilder, second.hoverInspectorBuilder) &&
                    !ReferenceEquals(first.hoverInspectorHits, second.hoverInspectorHits) &&
                    !ReferenceEquals(first.hoverInspectorObjectPath, second.hoverInspectorObjectPath),
                ["clearOnlyOwner"] = first.hoverInspectorTarget == null && first.hoverInspectorItem == null &&
                    first.hoverInspectorText == string.Empty && first.hoverInspectorScroll == Vector2.zero &&
                    second.hoverInspectorTarget == secondTarget && second.hoverInspectorText == secondText,
                ["sharedFieldCache"] = ReferenceEquals(GetHoverInspectorFields(typeof(Vector3)), GetHoverInspectorFields(typeof(Vector3))),
                ["formatUnchanged"] = FormatInspectorValue(12.5f) == "12.5" &&
                    FormatInspectorValue("line\nnext") == "line ↵ next"
            };
        }
        finally
        {
            Object.DestroyImmediate(firstTarget);
            Object.DestroyImmediate(secondTarget);
        }
    }
    #endregion
}
#endif
