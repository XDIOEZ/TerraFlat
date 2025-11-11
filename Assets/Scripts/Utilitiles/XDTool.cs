using Force.DeepCloner;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Debug = UnityEngine.Debug;

public static class XDTool
{
    // 静态变量存储物品 ID，并使用 PlayerPrefs 进行永久存储
    private static int itemId;

    public static int ItemId
    {
        get 
        {
            PlayerPrefs.SetInt("ItemId",(PlayerPrefs.GetInt("ItemId", 0)+1)); // 保存到 PlayerPrefs

            PlayerPrefs.Save(); // 确保数据写入磁盘

            return PlayerPrefs.GetInt("ItemId", 0) ;
        } // 读取存储的 ItemId，默认值为 0
        set
        {
           
        }
    }
    public static int NextGuid
    {
        get
        {
            int i;
            i = PlayerPrefs.GetInt("Guid", 0);
            i++;
            PlayerPrefs.SetInt("Guid", i); // 保存到 PlayerPrefs
            return i; 
        } // 读取存储的 ItemId，默认值为 0
    }

    //遍历输出字典中所有的key和value
    public static void PrintDic<TKey, TValue>(Dictionary<TKey, TValue> dictionary) 
    {
        if (dictionary == null || dictionary.Count == 0)
        {
            Debug.Log("字典为空或未初始化！");
            return;
        }

        // 拼接键值对到一个字符串
        string debugText = "字典内容:\n";
        foreach (KeyValuePair<TKey, TValue> kvp in dictionary)
        {
            debugText += $"Key: {kvp.Key}, Value: {kvp.Value.ToString()}\n";
        }

        // 一次性输出
        Debug.Log(debugText);
    }

    public static void PrintList<T>(List<T> list)
    {
        if (list == null || list.Count == 0)
        {
            Debug.Log("列表为空或未初始化！");
            return;
        }

        string debugText = "列表内容:\n";
        foreach (T element in list)
        {
            debugText += $"{element}\n";
        }

        Debug.Log(debugText);
    }
    /// <summary>
    /// 老人小孩先起飞
    /// </summary>
    /// <typeparam name="T">要获取的组件类型</typeparam>
    /// <param name="obj">从谁身上开始获取</param>
    /// <returns></returns>
    public static T GetComponentInChildrenAndParent<T>(GameObject obj) where T : class
    {
        //游戏处于运行状态时，才会有GetComponentInChildren和GetComponentInParent方法
        if (Application.isPlaying)
        {
            T component = obj.GetComponentInChildren<T>();
            
            if (component == null)
            {
                component = obj.GetComponentInParent<T>();
            }
            if (component == null)
            {
                //Debug.LogWarning($"GameObject {obj.name} 没有找到组件 {typeof(T).Name}！");
            }else
            {
                //Debug.Log($"GameObject {obj.name} 找到组件 {typeof(T).Name}！");
            }
            return component;
        }
        else
        {
            Debug.LogWarning("游戏处于编辑状态，无法使用GetComponentInChildren和GetComponentInParent方法！");
            return null;
        }
    }


    /// <summary>
    /// 使用 Addressables 同步加载资源，并记录加载时间
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="address"></param>
    /// <returns></returns>
    public static T LoadABByAddressSync<T>(string address) where T : UnityEngine.Object
    {
        // 创建并启动计时器
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        // 开始异步加载操作
        AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(address);
        // 等待异步操作完成，实现同步加载效果
        T result = handle.WaitForCompletion();

        // 停止计时器
        stopwatch.Stop();

        // 输出加载时间
        UnityEngine.Debug.Log($"加载资源 '{address}' 耗时: {stopwatch.ElapsedMilliseconds} 毫秒");

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            return result;
        }
        else
        {
            UnityEngine.Debug.LogError($"同步加载 Addressable 资源失败: {address}");
            return null;
        }
    }

    /// <summary>
    /// 异步加载 Addressable 资源
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="address">资源地址</param>
    /// <param name="callback">加载完成后的回调</param>
    public  static void LoadResourceAsync<T>(string address, System.Action<T> callback) where T : UnityEngine.Object
    {
        MonoMgr.GetInstance().StartCoroutine(LoadResourceCoroutine(address, callback));
    }

    private  static IEnumerator LoadResourceCoroutine<T>(string address, System.Action<T> callback) where T : UnityEngine.Object
    {
        // 开始异步加载操作
        AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(address);
        yield return handle;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            // 加载成功，执行回调
            callback?.Invoke(handle.Result);
        }
        else
        {
            // 加载失败，输出错误信息
            Debug.LogError($"加载 Addressable 资源失败: {address}");
            callback?.Invoke(null);
        }
    }

    private static bool IsGzipCompressed(byte[] data)
    {
        return data.Length > 2 && data[0] == 0x1F && data[1] == 0x8B;
    }

    /// <summary>
    /// 异步加载资源。
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="address">资源地址</param>
    /// <param name="onSuccess">加载成功回调</param>
    /// <param name="onFail">加载失败回调</param>
    public static void LoadAssetAsync<T>(string address, Action<T> onSuccess, Action<Exception> onFail = null) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(address))
        {

            Debug.LogError("资源地址不能为空！");
            return;
        }
        Addressables.LoadAssetAsync<T>(address).Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                onSuccess?.Invoke(handle.Result);
            }
            else
            {
                onFail?.Invoke(handle.OperationException);
                Debug.LogError($"资源加载失败：{address}\n错误信息：{handle.OperationException}");
            }
        };
    }
    /// <summary>
    /// 异步加载并实例化 Addressables 资源
    /// </summary>
    /// <param name="address">Addressables 资源地址</param>
    /// <param name="position">实例化位置</param>
    /// <param name="rotation">实例化旋转</param>
    /// <param name="onSuccess">成功回调，返回 GameObject</param>
    /// <param name="onFail">失败回调，返回异常信息</param>
    public static void InstantiateAddressableAsync(string address, Vector3 position, Quaternion rotation, Action<GameObject> onSuccess, Action<Exception> onFail = null)
    {
        Debug.Log($"实例化 Addressable 资源：{address}");
        if (string.IsNullOrEmpty(address))
        {
            Debug.LogError("资源地址不能为空！");
            onFail?.Invoke(new ArgumentException("资源地址不能为空！"));
            return;
        }

        Addressables.InstantiateAsync(address, position, rotation).Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                onSuccess?.Invoke(handle.Result);
            }
            else
            {
                onFail?.Invoke(handle.OperationException);
                Debug.LogError($"实例化失败：{address}\n错误信息：{handle.OperationException}");
            }
        };
    }

}
