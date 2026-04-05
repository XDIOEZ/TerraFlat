using UnityEngine;

public class GameDebugManager : MonoBehaviour
{
#region 显示设置

    [Header("调试快捷键")]
    [SerializeField] private KeyCode toggleEnvironmentInfoKey = KeyCode.F3;

    [Header("实例化策略")]
    [SerializeField] private bool createOnFirstToggle = true;
    [SerializeField] private bool cleanupDuplicateDisplaysOnToggle = true;

#endregion

#region 运行时字段

    private EnvironmentInfoDisplay _environmentInfoDisplay;

#endregion

#region 生命周期

    private void Update()
    {
        if (Input.GetKeyDown(toggleEnvironmentInfoKey))
        {
            ToggleEnvironmentInfo();
        }
    }

#endregion

#region 公共方法

    public void ToggleEnvironmentInfo()
    {
        EnvironmentInfoDisplay display = GetOrCreateDisplay();
        if (display == null)
        {
            Debug.LogError("[GameDebugManager] 无法获取 EnvironmentInfoDisplay 实例");
            return;
        }

        if (cleanupDuplicateDisplaysOnToggle)
        {
            CleanupDuplicateDisplays(display);
        }

        display.Toggle();
    }

#endregion

#region 私有方法

    private EnvironmentInfoDisplay GetOrCreateDisplay()
    {
        if (_environmentInfoDisplay != null)
            return _environmentInfoDisplay;

        _environmentInfoDisplay = EnvironmentInfoDisplay.Instance;
        if (_environmentInfoDisplay != null)
            return _environmentInfoDisplay;

        if (!createOnFirstToggle)
            return null;

        _environmentInfoDisplay = EnvironmentInfoDisplay.EnsureInstance();
        return _environmentInfoDisplay;
    }

    private void CleanupDuplicateDisplays(EnvironmentInfoDisplay keeper)
    {
        EnvironmentInfoDisplay[] allDisplays = FindObjectsOfType<EnvironmentInfoDisplay>(true);
        for (int i = 0; i < allDisplays.Length; i++)
        {
            EnvironmentInfoDisplay display = allDisplays[i];
            if (display == null || display == keeper)
                continue;

            Destroy(display.gameObject);
        }
    }

#endregion
}
