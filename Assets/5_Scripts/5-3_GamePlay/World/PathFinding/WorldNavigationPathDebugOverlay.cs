using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Build-safe runtime overlay for the routes currently followed by AI navigation agents.
/// It combines every visible route into one dynamic mesh and stays disabled by default.
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class WorldNavigationPathDebugOverlay : MonoBehaviour
{
    private const float RouteHalfWidth = 0.035f;
    private const float DestinationMarkerRadius = 0.13f;
    private const int DebugSortingOrder = 32760;

    private static readonly Color32[] RoutePalette =
    {
        new(53, 224, 255, 235),
        new(87, 255, 170, 235),
        new(190, 124, 255, 235),
        new(255, 111, 183, 235)
    };

    private static readonly Color32 DestinationColor = new(255, 194, 61, 245);
    private static WorldNavigationPathDebugOverlay instance;
    private static bool routesVisible;

    private readonly List<Vector3> vertices = new(1024);
    private readonly List<Color32> colors = new(1024);
    private readonly List<int> triangles = new(1536);
    private readonly List<Vector3> routePoints = new(32);

    private Mesh routeMesh;
    private MeshRenderer routeRenderer;
    private Material routeMaterial;

    public static bool RoutesVisible => routesVisible;
    public static WorldNavigationPathDebugOverlay ActiveInstance => instance;
    public int DrawnAgentCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        routesVisible = false;
    }

    public static bool ToggleRoutesVisible()
    {
        SetRoutesVisible(!routesVisible);
        return routesVisible;
    }

    public static void SetRoutesVisible(bool visible)
    {
        routesVisible = visible;
        if (visible)
            EnsureInstance();

        if (instance != null)
            instance.ApplyVisibility(routesVisible);
    }

    private static void EnsureInstance()
    {
        if (instance != null)
            return;

        instance = FindObjectOfType<WorldNavigationPathDebugOverlay>();
        if (instance != null)
            return;

        GameObject root = new("[World Navigation Path Debug]");
        root.hideFlags = HideFlags.DontSave;
        instance = root.AddComponent<WorldNavigationPathDebugOverlay>();
        DontDestroyOnLoad(root);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        CreateRenderResources();
        ApplyVisibility(routesVisible);
    }

    private void LateUpdate()
    {
        if (!routesVisible)
            return;

        RebuildRouteMesh();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        DestroyOwnedObject(routeMesh);
        DestroyOwnedObject(routeMaterial);
    }

    private void CreateRenderResources()
    {
        routeMesh = new Mesh
        {
            name = "World Navigation Debug Routes",
            hideFlags = HideFlags.DontSave,
            indexFormat = IndexFormat.UInt32
        };
        routeMesh.MarkDynamic();

        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = routeMesh;

        routeRenderer = gameObject.AddComponent<MeshRenderer>();
        routeRenderer.sortingOrder = DebugSortingOrder;
        routeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        routeRenderer.receiveShadows = false;
        routeRenderer.lightProbeUsage = LightProbeUsage.Off;
        routeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[WorldNavigation] 未找到运行时路线提示着色器，路线提示将保持关闭。", this);
            routesVisible = false;
            routeRenderer.enabled = false;
            return;
        }

        routeMaterial = new Material(shader)
        {
            name = "World Navigation Debug Route Material",
            hideFlags = HideFlags.DontSave,
            color = Color.white
        };
        routeRenderer.sharedMaterial = routeMaterial;
    }

    private void ApplyVisibility(bool visible)
    {
        enabled = visible;
        if (routeRenderer != null)
            routeRenderer.enabled = visible && routeMaterial != null;

        if (!visible)
            ClearRouteMesh();
    }

    private void RebuildRouteMesh()
    {
        vertices.Clear();
        colors.Clear();
        triangles.Clear();
        DrawnAgentCount = 0;

        IReadOnlyList<WorldNavigationAgent> agents = WorldNavigationAgent.ActiveAgents;
        for (int i = 0; i < agents.Count; i++)
        {
            WorldNavigationAgent agent = agents[i];
            if (agent == null)
                continue;

            bool drewAgent = false;
            Color32 routeColor = RoutePalette[(agent.GetInstanceID() & int.MaxValue) % RoutePalette.Length];
            if (agent.CopyRemainingDebugPath(routePoints))
            {
                for (int pointIndex = 1; pointIndex < routePoints.Count; pointIndex++)
                    AddSegment(routePoints[pointIndex - 1], routePoints[pointIndex], RouteHalfWidth, routeColor);
                drewAgent = true;
            }

            if (agent.TryGetDebugDestination(out Vector3 destination))
            {
                AddDestinationMarker(destination);
                drewAgent = true;
            }

            if (drewAgent)
                DrawnAgentCount++;
        }

        if (vertices.Count == 0)
        {
            ClearRouteMesh();
            return;
        }

        routeMesh.Clear(false);
        routeMesh.SetVertices(vertices);
        routeMesh.SetColors(colors);
        routeMesh.SetTriangles(triangles, 0, true);
    }

    private void AddDestinationMarker(Vector3 center)
    {
        Vector3 horizontal = new(DestinationMarkerRadius, 0f, 0f);
        Vector3 vertical = new(0f, DestinationMarkerRadius, 0f);
        AddSegment(center - horizontal, center + horizontal, RouteHalfWidth, DestinationColor);
        AddSegment(center - vertical, center + vertical, RouteHalfWidth, DestinationColor);
    }

    private void AddSegment(Vector3 start, Vector3 end, float halfWidth, Color32 color)
    {
        Vector2 delta = new(end.x - start.x, end.y - start.y);
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        Vector2 normal2D = new(-delta.y, delta.x);
        normal2D.Normalize();
        Vector3 normal = new Vector3(normal2D.x, normal2D.y, 0f) * halfWidth;
        int firstVertex = vertices.Count;

        vertices.Add(start + normal);
        vertices.Add(start - normal);
        vertices.Add(end + normal);
        vertices.Add(end - normal);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);

        triangles.Add(firstVertex);
        triangles.Add(firstVertex + 2);
        triangles.Add(firstVertex + 1);
        triangles.Add(firstVertex + 2);
        triangles.Add(firstVertex + 3);
        triangles.Add(firstVertex + 1);
    }

    private void ClearRouteMesh()
    {
        DrawnAgentCount = 0;
        if (routeMesh != null && routeMesh.vertexCount > 0)
            routeMesh.Clear(false);
    }

    private static void DestroyOwnedObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
