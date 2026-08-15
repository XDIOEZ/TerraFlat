using UnityEngine;

/// <summary>
/// MonoBehaviour 单例基类；Unity 销毁对象后，静态引用仍可能保留托管壳对象，访问时需要按 Unity null 语义恢复有效实例。
/// </summary>
public class SingletonMono<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;

    /// <summary>仅由第一个有效实例接管静态引用，避免场景副本覆盖跨场景实例。</summary>
    protected virtual void Awake()
    {
        if (instance == null || instance == this as T)
        {
            instance = this as T;
            return;
        }

        Destroy(gameObject);
    }

    /// <summary>返回有效实例；旧实例已销毁时从当前场景重新查找。</summary>
    public static T GetInstance()
    {
        if (instance == null)
            instance = Object.FindFirstObjectByType<T>();

        return instance;
    }

    /// <summary>单例销毁时清理静态引用，避免下一次场景进入继续持有旧对象。</summary>
    protected virtual void OnDestroy()
    {
        if (instance == this as T)
            instance = null;
    }

    /// <summary>返回有效实例，找不到时保持原有错误提示行为。</summary>
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Object.FindFirstObjectByType<T>();
                if (instance == null)
                {
                    Debug.LogError("无法在场景中找到:" + typeof(T));
                    return null;
                }
            }
            return instance;
        }
    }

}
