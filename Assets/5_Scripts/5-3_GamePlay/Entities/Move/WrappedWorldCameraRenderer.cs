using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders translated images of the canonical world whenever the main camera
/// crosses a wrapped boundary. The copies are cameras only; no gameplay object
/// is cloned and the main camera remains the sole audio/UI/post-process owner.
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class WrappedWorldCameraRenderer : MonoBehaviour
{
    private const int MaximumReplicaCameras = 24;
    private const int UiLayer = 5;
    private const float LightSourceRefreshInterval = 0.25f;

    private readonly List<Camera> replicas = new();
    private readonly List<Vector2Int> requiredImages = new();
    private readonly List<Light2D> pointLightSources = new();
    private readonly List<ReplicaLightRecord> replicaLights = new();
    private Camera sourceCamera;
    private UniversalAdditionalCameraData sourceCameraData;
    private CinemachineVirtualCamera virtualCamera;
    private Transform followTarget;
    private bool warnedAboutReplicaLimit;
    private float nextLightSourceRefreshTime;

    public int ActiveReplicaCount { get; private set; }
    public IReadOnlyList<Camera> Replicas => replicas;

    internal void Configure(
        Camera camera,
        CinemachineVirtualCamera cinemachineCamera,
        Transform target)
    {
        RemoveReplicasFromSourceStack();
        sourceCamera = camera;
        sourceCameraData = sourceCamera != null
            ? sourceCamera.GetUniversalAdditionalCameraData()
            : null;
        virtualCamera = cinemachineCamera;
        followTarget = target;
        enabled = sourceCamera != null;
    }

    public void Configure(Camera camera)
    {
        Configure(camera, null, null);
    }

    private void OnEnable()
    {
        WorldTopologyRuntime.LocalPlayerPositionWrapped -= HandlePositionWrapped;
        WorldTopologyRuntime.LocalPlayerPositionWrapped += HandlePositionWrapped;
    }

    private void OnDisable()
    {
        WorldTopologyRuntime.LocalPlayerPositionWrapped -= HandlePositionWrapped;
        RemoveReplicasFromSourceStack();
        DisableAllReplicas();
        DisableAllReplicaLights();
    }

    private void OnDestroy()
    {
        WorldTopologyRuntime.LocalPlayerPositionWrapped -= HandlePositionWrapped;
        RemoveReplicasFromSourceStack();
        for (int i = 0; i < replicas.Count; i++)
        {
            if (replicas[i] != null)
                Destroy(replicas[i].gameObject);
        }
        replicas.Clear();

        for (int i = 0; i < replicaLights.Count; i++)
        {
            if (replicaLights[i].Proxy != null)
                Destroy(replicaLights[i].Proxy.gameObject);
        }
        replicaLights.Clear();
    }

    private void LateUpdate()
    {
        if (sourceCamera == null || !sourceCamera.enabled ||
            !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            DisableAllReplicas();
            DisableAllReplicaLights();
            return;
        }

        BuildRequiredImages(bounds);
        int count = Mathf.Min(requiredImages.Count, MaximumReplicaCameras);
        EnsurePool(count);
        ActiveReplicaCount = count;
        for (int i = 0; i < replicas.Count; i++)
        {
            Camera replica = replicas[i];
            bool active = i < count;
            replica.enabled = active;
            if (!active)
                continue;

            CopySourceSettings(replica, i);
            Vector2Int image = requiredImages[i];
            Vector3 sourcePosition = sourceCamera.transform.position;
            replica.transform.SetPositionAndRotation(
                new Vector3(
                    sourcePosition.x - image.x * bounds.Span.x,
                    sourcePosition.y - image.y * bounds.Span.y,
                    sourcePosition.z),
                sourceCamera.transform.rotation);
        }

        RefreshReplicaLights(bounds, count);

        if (requiredImages.Count > MaximumReplicaCameras && !warnedAboutReplicaLimit)
        {
            warnedAboutReplicaLimit = true;
            Debug.LogWarning(
                $"[WrappedWorldCamera] View requires {requiredImages.Count} world images; " +
                $"only the nearest {MaximumReplicaCameras} are rendered. Reduce administrator zoom.",
                this);
        }
    }

    #region 环绕光照代理

    /// <summary>
    /// 为每个实际绘制的环绕世界副本同步 Point Light2D。
    /// 相机副本只会平移地形画面，URP 不理解环绕拓扑，因此动态灯也必须平移到同一世界镜像。
    /// </summary>
    private void RefreshReplicaLights(WorldTopologyBounds bounds, int imageCount)
    {
        RefreshPointLightSourceCache();

        int refreshVersion = Time.frameCount;
        for (int sourceIndex = 0; sourceIndex < pointLightSources.Count; sourceIndex++)
        {
            Light2D source = pointLightSources[sourceIndex];
            if (!ShouldReplicateLight(source))
                continue;

            // 主相机也可能需要看见位于世界另一端的灯，因此先同步它最近的拓扑镜像。
            SyncNearestLightImage(source, sourceCamera.transform.position, bounds, refreshVersion);
            for (int imageIndex = 0; imageIndex < imageCount; imageIndex++)
            {
                Camera replicaCamera = replicas[imageIndex];
                SyncNearestLightImage(source, replicaCamera.transform.position,
                    bounds, refreshVersion);
            }
        }

        for (int i = 0; i < replicaLights.Count; i++)
        {
            ReplicaLightRecord record = replicaLights[i];
            if (record.Proxy != null)
            {
                record.Proxy.enabled = record.LastRefreshVersion == refreshVersion &&
                                       ShouldReplicateLight(record.Source);
            }
        }
    }

    /// <summary>让指定相机附近始终存在该真实灯的最近环绕镜像。</summary>
    private void SyncNearestLightImage(
        Light2D source,
        Vector3 cameraPosition,
        WorldTopologyBounds bounds,
        int refreshVersion)
    {
        Vector2 sourcePosition = source.transform.position;
        Vector2 nearestPosition = bounds.NearestImagePosition(cameraPosition, sourcePosition);
        Vector2 offset = nearestPosition - sourcePosition;
        if (offset == Vector2.zero || !IsLightPotentiallyVisible(source, offset, cameraPosition))
            return;

        ReplicaLightRecord record = GetOrCreateReplicaLight(source, offset);
        SyncReplicaLight(record, source, offset);
        record.LastRefreshVersion = refreshVersion;
    }

    /// <summary>低频刷新真实点光源列表，逐帧只同步已经缓存的少量灯光。</summary>
    private void RefreshPointLightSourceCache()
    {
        float now = Time.unscaledTime;
        if (now < nextLightSourceRefreshTime)
            return;

        nextLightSourceRefreshTime = now + LightSourceRefreshInterval;
        pointLightSources.Clear();
        RemoveDestroyedLightRecords();
        Light2D[] lights = FindObjectsOfType<Light2D>(false);
        for (int i = 0; i < lights.Length; i++)
        {
            Light2D light = lights[i];
            if (light != null && light.lightType == Light2D.LightType.Point &&
                light.GetComponent<WorldTopologyLightProxy>() == null)
            {
                pointLightSources.Add(light);
            }
        }
    }

    /// <summary>只为可能进入当前视野的灯创建代理，避免远处已加载灯光放大代理数量。</summary>
    private bool IsLightPotentiallyVisible(
        Light2D source,
        Vector2 offset,
        Vector3 cameraPosition)
    {
        if (sourceCamera == null || !sourceCamera.orthographic)
            return true;

        float radius = source.pointLightOuterRadius * Mathf.Max(
            Mathf.Abs(source.transform.lossyScale.x),
            Mathf.Abs(source.transform.lossyScale.y));
        float halfHeight = sourceCamera.orthographicSize;
        float halfWidth = halfHeight * Mathf.Max(0.01f, sourceCamera.aspect);
        Vector3 lightPosition = source.transform.position + (Vector3)offset;
        return lightPosition.x + radius >= cameraPosition.x - halfWidth &&
               lightPosition.x - radius <= cameraPosition.x + halfWidth &&
               lightPosition.y + radius >= cameraPosition.y - halfHeight &&
               lightPosition.y - radius <= cameraPosition.y + halfHeight;
    }

    /// <summary>真实灯销毁后同步释放代理，避免跨维度或长时间游玩残留对象。</summary>
    private void RemoveDestroyedLightRecords()
    {
        for (int i = replicaLights.Count - 1; i >= 0; i--)
        {
            ReplicaLightRecord record = replicaLights[i];
            if (record.Source != null)
                continue;

            replicaLights.RemoveAt(i);
            if (record.Proxy != null)
                Destroy(record.Proxy.gameObject);
        }
    }

    private static bool ShouldReplicateLight(Light2D source)
    {
        return source != null && source.enabled && source.gameObject.activeInHierarchy &&
               source.lightType == Light2D.LightType.Point && source.intensity > 0f;
    }

    /// <summary>按真实灯光和世界镜像偏移复用代理，避免玩家移动时反复创建对象。</summary>
    private ReplicaLightRecord GetOrCreateReplicaLight(Light2D source, Vector2 offset)
    {
        for (int i = 0; i < replicaLights.Count; i++)
        {
            ReplicaLightRecord existing = replicaLights[i];
            if (existing.Source == source && existing.Offset == offset)
                return existing;
        }

        var proxyObject = new GameObject($"Wrapped Light - {source.name}", typeof(Light2D));
        proxyObject.hideFlags = HideFlags.DontSave;
        proxyObject.AddComponent<WorldTopologyLightProxy>();
        Light2D proxy = proxyObject.GetComponent<Light2D>();

        // 首次建立时复制私有序列化配置（尤其是受影响的 Sorting Layer）。
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), proxy);
        var record = new ReplicaLightRecord(source, proxy, offset);
        replicaLights.Add(record);
        return record;
    }

    /// <summary>同步运行时会变化的点光参数以及世界变换。</summary>
    private static void SyncReplicaLight(
        ReplicaLightRecord record,
        Light2D source,
        Vector2 offset)
    {
        Light2D proxy = record.Proxy;
        Transform sourceTransform = source.transform;
        Transform proxyTransform = proxy.transform;
        proxyTransform.SetPositionAndRotation(sourceTransform.position + (Vector3)offset,
            sourceTransform.rotation);
        proxyTransform.localScale = sourceTransform.lossyScale;
        proxy.gameObject.layer = source.gameObject.layer;

        proxy.lightType = Light2D.LightType.Point;
        proxy.blendStyleIndex = source.blendStyleIndex;
        proxy.color = source.color;
        proxy.intensity = source.intensity;
        proxy.falloffIntensity = source.falloffIntensity;
        proxy.overlapOperation = source.overlapOperation;
        proxy.lightOrder = source.lightOrder;
        proxy.pointLightInnerAngle = source.pointLightInnerAngle;
        proxy.pointLightOuterAngle = source.pointLightOuterAngle;
        proxy.pointLightInnerRadius = source.pointLightInnerRadius;
        proxy.pointLightOuterRadius = source.pointLightOuterRadius;
        proxy.shadowsEnabled = source.shadowsEnabled;
        proxy.shadowIntensity = source.shadowIntensity;
        proxy.volumeIntensityEnabled = source.volumeIntensityEnabled;
        proxy.volumeIntensity = source.volumeIntensity;
        proxy.volumetricShadowsEnabled = source.volumetricShadowsEnabled;
        proxy.shadowVolumeIntensity = source.shadowVolumeIntensity;
    }

    private void DisableAllReplicaLights()
    {
        for (int i = 0; i < replicaLights.Count; i++)
        {
            if (replicaLights[i].Proxy != null)
                replicaLights[i].Proxy.enabled = false;
        }
    }

    private sealed class ReplicaLightRecord
    {
        public ReplicaLightRecord(Light2D source, Light2D proxy, Vector2 offset)
        {
            Source = source;
            Proxy = proxy;
            Offset = offset;
        }

        public Light2D Source { get; }
        public Light2D Proxy { get; }
        public Vector2 Offset { get; }
        public int LastRefreshVersion { get; set; }
    }

    #endregion

    private void BuildRequiredImages(WorldTopologyBounds bounds)
    {
        requiredImages.Clear();
        float halfHeight = sourceCamera.orthographic
            ? sourceCamera.orthographicSize
            : Mathf.Max(bounds.Span.x, bounds.Span.y);
        float halfWidth = sourceCamera.orthographic
            ? halfHeight * Mathf.Max(0.01f, sourceCamera.aspect)
            : halfHeight;
        Vector3 cameraPosition = sourceCamera.transform.position;
        float viewMinX = cameraPosition.x - halfWidth;
        float viewMaxX = cameraPosition.x + halfWidth;
        float viewMinY = cameraPosition.y - halfHeight;
        float viewMaxY = cameraPosition.y + halfHeight;

        int minImageX = Mathf.FloorToInt((viewMinX - bounds.MaxExclusive.x) / bounds.Span.x) - 1;
        int maxImageX = Mathf.CeilToInt((viewMaxX - bounds.Min.x) / bounds.Span.x) + 1;
        int minImageY = Mathf.FloorToInt((viewMinY - bounds.MaxExclusive.y) / bounds.Span.y) - 1;
        int maxImageY = Mathf.CeilToInt((viewMaxY - bounds.Min.y) / bounds.Span.y) + 1;

        for (int imageX = minImageX; imageX <= maxImageX; imageX++)
        {
            float imageMinX = bounds.Min.x + imageX * bounds.Span.x;
            float imageMaxX = bounds.MaxExclusive.x + imageX * bounds.Span.x;
            if (imageMaxX <= viewMinX || imageMinX >= viewMaxX)
                continue;

            for (int imageY = minImageY; imageY <= maxImageY; imageY++)
            {
                if (imageX == 0 && imageY == 0)
                    continue;
                float imageMinY = bounds.Min.y + imageY * bounds.Span.y;
                float imageMaxY = bounds.MaxExclusive.y + imageY * bounds.Span.y;
                if (imageMaxY <= viewMinY || imageMinY >= viewMaxY)
                    continue;
                requiredImages.Add(new Vector2Int(imageX, imageY));
            }
        }

        requiredImages.Sort((left, right) =>
            (left.sqrMagnitude).CompareTo(right.sqrMagnitude));
    }

    private void EnsurePool(int count)
    {
        while (replicas.Count < count)
        {
            var replicaObject = new GameObject(
                $"Wrapped World Camera {replicas.Count + 1}",
                typeof(Camera));
            replicaObject.tag = "Untagged";
            replicaObject.hideFlags = HideFlags.DontSave;
            Camera replica = replicaObject.GetComponent<Camera>();
            UniversalAdditionalCameraData cameraData =
                replica.GetUniversalAdditionalCameraData();
            cameraData.renderType = CameraRenderType.Overlay;
            replicas.Add(replica);
        }

        if (sourceCameraData == null || sourceCameraData.renderType != CameraRenderType.Base)
            return;

        for (int i = 0; i < count; i++)
        {
            Camera replica = replicas[i];
            if (replica != null && !sourceCameraData.cameraStack.Contains(replica))
                sourceCameraData.cameraStack.Add(replica);
        }
    }

    private void CopySourceSettings(Camera replica, int index)
    {
        replica.CopyFrom(sourceCamera);
        replica.clearFlags = CameraClearFlags.Depth;
        replica.backgroundColor = Color.clear;
        replica.depth = sourceCamera.depth + index + 1f;
        replica.cullingMask = sourceCamera.cullingMask & ~(1 << UiLayer);
        replica.targetTexture = sourceCamera.targetTexture;
        replica.useOcclusionCulling = sourceCamera.useOcclusionCulling;
        replica.stereoTargetEye = StereoTargetEyeMask.None;
    }

    private void HandlePositionWrapped(WorldWrapEvent wrapEvent)
    {
        if (virtualCamera == null || followTarget == null)
            return;
        virtualCamera.OnTargetObjectWarped(followTarget, wrapEvent.WorldShift);
    }

    private void DisableAllReplicas()
    {
        ActiveReplicaCount = 0;
        for (int i = 0; i < replicas.Count; i++)
        {
            if (replicas[i] != null)
                replicas[i].enabled = false;
        }
    }

    private void RemoveReplicasFromSourceStack()
    {
        if (sourceCameraData == null || sourceCameraData.renderType != CameraRenderType.Base)
            return;

        for (int i = 0; i < replicas.Count; i++)
        {
            Camera replica = replicas[i];
            if (replica != null)
                sourceCameraData.cameraStack.Remove(replica);
        }
    }
}

/// <summary>标记纯渲染用环绕灯光，逻辑光照采样必须忽略这些副本。</summary>
[DisallowMultipleComponent]
public sealed class WorldTopologyLightProxy : MonoBehaviour
{
}
