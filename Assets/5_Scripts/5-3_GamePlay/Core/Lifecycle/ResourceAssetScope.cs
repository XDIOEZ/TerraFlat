using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 一次资源目录的 Addressables 所有者。请求发出时立即记录句柄，成功资源保留到目录卸载，
/// 失败、取消和退出都按相反顺序释放；同类型同地址只持有一次请求。
/// </summary>
internal sealed class ResourceAssetScope : IDisposable
{
    #region 资源所有权

    private readonly List<AsyncOperationHandle> handles = new();
    private readonly Dictionary<(Type, string), AsyncOperationHandle> assets = new();
    private bool disposed;
    public int HandleCount => handles.Count;

    /// <summary>接管当前请求，包括尚未完成或最终失败的句柄。</summary>
    public AsyncOperationHandle<T> Own<T>(AsyncOperationHandle<T> handle)
    {
        if (disposed)
        {
            if (handle.IsValid()) Addressables.Release(handle);
            throw new ObjectDisposedException(nameof(ResourceAssetScope));
        }
        handles.Add(handle);
        return handle;
    }

    /// <summary>按类型与规范地址去重请求，资源由本目录统一持有。</summary>
    public AsyncOperationHandle<T> Load<T>(string address) where T : UnityEngine.Object
    {
        if (disposed) throw new ObjectDisposedException(nameof(ResourceAssetScope));
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("资源地址不能为空。", nameof(address));
        var key = (typeof(T), address.Trim());
        if (assets.TryGetValue(key, out AsyncOperationHandle existing)) return existing.Convert<T>();
        AsyncOperationHandle<T> handle = Own(Addressables.LoadAssetAsync<T>(key.Item2));
        assets.Add(key, handle);
        return handle;
    }

    /// <summary>操作必须明确成功，禁止将空结果写入资源目录。</summary>
    public static T Require<T>(AsyncOperationHandle<T> handle, string source)
    {
        if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            throw new System.IO.InvalidDataException($"资源加载失败：{source}", handle.IsValid() ? handle.OperationException : null);
        return handle.Result;
    }

    /// <summary>逐个释放所有请求；一个异常不能阻止其余句柄清理。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var errors = new List<Exception>();
        for (int i = handles.Count - 1; i >= 0; i--)
        {
            try { if (handles[i].IsValid()) Addressables.Release(handles[i]); }
            catch (Exception exception) { errors.Add(exception); }
        }
        handles.Clear();
        assets.Clear();
        if (errors.Count > 0) throw new AggregateException("资源句柄释放失败。", errors);
    }

    #endregion
}
