using TMPro;
using UnityEngine;

/// <summary>版本号直接读取构建版本，避免每次发布手动修改界面文字。</summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class ApplicationVersionLabel : MonoBehaviour, IUiRootStaticContent
{
    #region 版本展示
    private void OnEnable()
    {
        GetComponent<TextMeshProUGUI>().text = $"v{Application.version}";
        // 场景根复用同一Prefab，版本只由UIManager拥有的常驻根显示。
        if (Application.isPlaying && GetComponentInParent<UIManager>() == null)
            gameObject.SetActive(false);
    }
    #endregion
}

/// <summary>UI根自身持有的静态内容不随业务资源重载销毁。</summary>
public interface IUiRootStaticContent { }
