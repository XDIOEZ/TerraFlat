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

    private readonly List<Camera> replicas = new();
    private readonly List<Vector2Int> requiredImages = new();
    private Camera sourceCamera;
    private UniversalAdditionalCameraData sourceCameraData;
    private CinemachineVirtualCamera virtualCamera;
    private Transform followTarget;
    private bool warnedAboutReplicaLimit;

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
    }

    private void LateUpdate()
    {
        if (sourceCamera == null || !sourceCamera.enabled ||
            !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            DisableAllReplicas();
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

        if (requiredImages.Count > MaximumReplicaCameras && !warnedAboutReplicaLimit)
        {
            warnedAboutReplicaLimit = true;
            Debug.LogWarning(
                $"[WrappedWorldCamera] View requires {requiredImages.Count} world images; " +
                $"only the nearest {MaximumReplicaCameras} are rendered. Reduce administrator zoom.",
                this);
        }
    }

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
