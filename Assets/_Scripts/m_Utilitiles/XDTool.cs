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
    // ��̬�����洢��Ʒ ID����ʹ�� PlayerPrefs �������ô洢
    private static int itemId;

    public static int ItemId
    {
        get 
        {
            PlayerPrefs.SetInt("ItemId",(PlayerPrefs.GetInt("ItemId", 0)+1)); // ���浽 PlayerPrefs

            PlayerPrefs.Save(); // ȷ������д�����

            return PlayerPrefs.GetInt("ItemId", 0) ;
        } // ��ȡ�洢�� ItemId��Ĭ��ֵΪ 0
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
            PlayerPrefs.SetInt("Guid", i); // ���浽 PlayerPrefs
            return i; 
        } // ��ȡ�洢�� ItemId��Ĭ��ֵΪ 0
    }

    //��������ֵ������е�key��value
    public static void PrintDic<TKey, TValue>(Dictionary<TKey, TValue> dictionary) 
    {
        if (dictionary == null || dictionary.Count == 0)
        {
            Debug.Log("�ֵ�Ϊ�ջ�δ��ʼ����");
            return;
        }

        // ƴ�Ӽ�ֵ�Ե�һ���ַ���
        string debugText = "�ֵ�����:\n";
        foreach (KeyValuePair<TKey, TValue> kvp in dictionary)
        {
            debugText += $"Key: {kvp.Key}, Value: {kvp.Value.ToString()}\n";
        }

        // һ�������
        Debug.Log(debugText);
    }

    public static void PrintList<T>(List<T> list)
    {
        if (list == null || list.Count == 0)
        {
            Debug.Log("�б�Ϊ�ջ�δ��ʼ����");
            return;
        }

        string debugText = "�б�����:\n";
        foreach (T element in list)
        {
            debugText += $"{element}\n";
        }

        Debug.Log(debugText);
    }
    /// <summary>
    /// ����С�������
    /// </summary>
    /// <typeparam name="T">Ҫ��ȡ���������</typeparam>
    /// <param name="obj">��˭���Ͽ�ʼ��ȡ</param>
    /// <returns></returns>
    public static T GetComponentInChildrenAndParent<T>(GameObject obj) where T : class
    {
        //��Ϸ��������״̬ʱ���Ż���GetComponentInChildren��GetComponentInParent����
        if (Application.isPlaying)
        {
            T component = obj.GetComponentInChildren<T>();
            
            if (component == null)
            {
                component = obj.GetComponentInParent<T>();
            }
            if (component == null)
            {
                //Debug.LogWarning($"GameObject {obj.name} û���ҵ���� {typeof(T).Name}��");
            }else
            {
                //Debug.Log($"GameObject {obj.name} �ҵ���� {typeof(T).Name}��");
            }
            return component;
        }
        else
        {
            Debug.LogWarning("��Ϸ���ڱ༭״̬���޷�ʹ��GetComponentInChildren��GetComponentInParent������");
            return null;
        }
    }


    /// <summary>
    /// ʹ�� Addressables ͬ��������Դ������¼����ʱ��
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="address"></param>
    /// <returns></returns>
    public static T LoadABByAddressSync<T>(string address) where T : UnityEngine.Object
    {
        // ������������ʱ��
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        // ��ʼ�첽���ز���
        AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(address);
        // �ȴ��첽������ɣ�ʵ��ͬ������Ч��
        T result = handle.WaitForCompletion();

        // ֹͣ��ʱ��
        stopwatch.Stop();

        // �������ʱ��
        UnityEngine.Debug.Log($"������Դ '{address}' ��ʱ: {stopwatch.ElapsedMilliseconds} ����");

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            return result;
        }
        else
        {
            UnityEngine.Debug.LogError($"ͬ������ Addressable ��Դʧ��: {address}");
            return null;
        }
    }

    /// <summary>
    /// �첽���� Addressable ��Դ
    /// </summary>
    /// <typeparam name="T">��Դ����</typeparam>
    /// <param name="address">��Դ��ַ</param>
    /// <param name="callback">������ɺ�Ļص�</param>
    public  static void LoadResourceAsync<T>(string address, System.Action<T> callback) where T : UnityEngine.Object
    {
        MonoMgr.GetInstance().StartCoroutine(LoadResourceCoroutine(address, callback));
    }

    private  static IEnumerator LoadResourceCoroutine<T>(string address, System.Action<T> callback) where T : UnityEngine.Object
    {
        // ��ʼ�첽���ز���
        AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(address);
        yield return handle;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            // ���سɹ���ִ�лص�
            callback?.Invoke(handle.Result);
        }
        else
        {
            // ����ʧ�ܣ����������Ϣ
            Debug.LogError($"���� Addressable ��Դʧ��: {address}");
            callback?.Invoke(null);
        }
    }

    private static bool IsGzipCompressed(byte[] data)
    {
        return data.Length > 2 && data[0] == 0x1F && data[1] == 0x8B;
    }

    /// <summary>
    /// �첽������Դ��
    /// </summary>
    /// <typeparam name="T">��Դ����</typeparam>
    /// <param name="address">��Դ��ַ</param>
    /// <param name="onSuccess">���سɹ��ص�</param>
    /// <param name="onFail">����ʧ�ܻص�</param>
    public static void LoadAssetAsync<T>(string address, Action<T> onSuccess, Action<Exception> onFail = null) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(address))
        {

            Debug.LogError("��Դ��ַ����Ϊ�գ�");
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
                Debug.LogError($"��Դ����ʧ�ܣ�{address}\n������Ϣ��{handle.OperationException}");
            }
        };
    }
    /// <summary>
    /// �첽���ز�ʵ���� Addressables ��Դ
    /// </summary>
    /// <param name="address">Addressables ��Դ��ַ</param>
    /// <param name="position">ʵ����λ��</param>
    /// <param name="rotation">ʵ������ת</param>
    /// <param name="onSuccess">�ɹ��ص������� GameObject</param>
    /// <param name="onFail">ʧ�ܻص��������쳣��Ϣ</param>
    public static void InstantiateAddressableAsync(string address, Vector3 position, Quaternion rotation, Action<GameObject> onSuccess, Action<Exception> onFail = null)
    {
        Debug.Log($"ʵ���� Addressable ��Դ��{address}");
        if (string.IsNullOrEmpty(address))
        {
            Debug.LogError("��Դ��ַ����Ϊ�գ�");
            onFail?.Invoke(new ArgumentException("��Դ��ַ����Ϊ�գ�"));
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
                Debug.LogError($"ʵ����ʧ�ܣ�{address}\n������Ϣ��{handle.OperationException}");
            }
        };
    }

}
