using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 泛型自动创建的单例基类
/// 继承这个类后，子类可以全局访问，自动跨场景，且不会重复创建
/// </summary>
public class SingletonAutoMono<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;

    // 标记应用是否正在退出，防止在销毁阶段重新创建单例对象
    private static bool isShuttingDown = false;

    /// <summary>
    /// 单例全局访问
    /// </summary>
    public static T Instance
    {
        get
        {
            // 如果应用正在退出，则不再尝试创建新的单例实例
            if (isShuttingDown)
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
        isShuttingDown = true;
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
