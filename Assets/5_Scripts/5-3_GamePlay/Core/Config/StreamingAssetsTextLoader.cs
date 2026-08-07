using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Reads text files from StreamingAssets on both file-system platforms and platforms where
/// StreamingAssets is stored inside a package (for example Android's APK/JAR).
/// </summary>
public static class StreamingAssetsTextLoader
{
    public static string CombinePath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("StreamingAssets 根路径不能为空", nameof(root));

        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        return root.TrimEnd('/', '\\') + "/" + normalizedRelativePath;
    }

    public static string ReadAllText(string path)
    {
        if (RequiresWebRequest(path))
        {
            throw new NotSupportedException(
                $"当前平台的 StreamingAssets 不能同步读取，请使用 ReadAllTextAsync：{path}");
        }

        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到 StreamingAssets 文件：{path}", path);

        return File.ReadAllText(path);
    }

    public static IEnumerator ReadAllTextAsync(
        string path,
        Action<string> onCompleted,
        Action<Exception> onFailed)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            onFailed?.Invoke(new ArgumentException("StreamingAssets 文件路径不能为空", nameof(path)));
            yield break;
        }

        if (!RequiresWebRequest(path))
        {
            try
            {
                onCompleted?.Invoke(ReadAllText(path));
            }
            catch (Exception exception)
            {
                onFailed?.Invoke(exception);
            }

            yield break;
        }

        using UnityWebRequest request = UnityWebRequest.Get(path);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            onFailed?.Invoke(new IOException(
                $"读取 StreamingAssets 文件失败：{path}；{request.error}；HTTP {request.responseCode}"));
            yield break;
        }

        onCompleted?.Invoke(request.downloadHandler.text);
    }

    public static bool RequiresWebRequest(string path)
    {
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.WebGLPlayer)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(path) && path.Contains("://");
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"StreamingAssets 相对路径无效：{relativePath}");

        string normalized = relativePath.Trim().Replace('\\', '/').TrimStart('/');
        string[] segments = normalized.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".." || segment.Contains(':'))
                throw new InvalidDataException($"StreamingAssets 相对路径无效：{relativePath}");
        }

        return string.Join("/", segments);
    }
}
