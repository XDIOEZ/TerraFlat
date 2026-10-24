using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 农业差量热查询注册

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterSoilPersistenceRules()
        => Register("agriculture", "soil_persistence_keys", ServiceSoilPersistenceKeys);

    private static JObject ServiceSoilPersistenceKeys(JObject args)
    {
        var root = new GameObject("probe:inactive-soil-persistence");
        root.SetActive(false);
        try { return root.AddComponent<SaveDataMgr>().CaptureTestSoilPersistenceKeys(); }
        finally { Object.DestroyImmediate(root); }
    }

    #endregion
}
