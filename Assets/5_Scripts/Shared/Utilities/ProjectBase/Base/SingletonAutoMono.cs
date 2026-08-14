using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 泛型自动创建的单例基类
/// 继承这个类后，子类可以全局访问，自动跨场景，且不会重复创建
/// </summary>
public class SingletonAutoMono<T> : MonoBehaviour where T : MonoBehaviour
{
    protected static T instance;

    /// <summary>
    /// 单例全局访问
    /// </summary>
    public static T Instance
    {
        get
        {
            // 如果应用正在退出，则不再尝试创建新的单例实例
            if (SingletonAutoMonoLifecycle.IsShuttingDown)
            {
                return instance;
            }

            if (instance == null)
            {
                // 优先寻找场景里是否已经存在
                instance = FindObjectOfType<T>();

                if (instance == null)
                {
                    // 场景不存在就自动创建
                    GameObject obj = new GameObject(typeof(T).Name);
                    DontDestroyOnLoad(obj);
                    instance = obj.AddComponent<T>();
                }
            }
            return instance;
        }
    }

    /// <summary>
    /// Awake 防呆，防止场景中手动拖拽多个对象导致冲突
    /// </summary>
    protected virtual void Awake()
    {
        if (instance == null)
        {
            instance = this as T;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 应用退出时标记，防止在销毁过程中再次访问单例导致创建新 GameObject
    /// </summary>
    protected virtual void OnApplicationQuit()
    {
        SingletonAutoMonoLifecycle.MarkShuttingDown();
    }

    /// <summary>
    /// 当单例自身被销毁时，清理静态引用
    /// </summary>
    protected virtual void OnDestroy()
    {
        if (instance == this as T)
        {
            instance = null;
        }
    }

}

/// <summary>
/// 在关闭 Domain Reload 的快速进入播放模式下，静态字段不会自动清空。
/// 统一在每次运行初始化时复位退出标记，避免下一轮运行的自动单例永久返回 null。
/// </summary>
internal static class SingletonAutoMonoLifecycle
{
    public static bool IsShuttingDown { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        IsShuttingDown = false;
    }

    public static void MarkShuttingDown()
    {
        IsShuttingDown = true;
    }
}
