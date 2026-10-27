using UnityEngine;

internal static class PlayerPrefsFloatValueCommitter
{
    #region 已校验浮点偏好的变化提交

    // 只共用缓存比较和持久化，领域仍负责校验、初始化与保存后的通知。
    internal static float PersistChangedCachedFloatPreference(
        ref float cachedValue, string key, float value, out bool changed)
    {
        changed = !Mathf.Approximately(cachedValue, value);
        if (!changed) return cachedValue;
        cachedValue = value;
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
        return value;
    }

    #endregion
}
