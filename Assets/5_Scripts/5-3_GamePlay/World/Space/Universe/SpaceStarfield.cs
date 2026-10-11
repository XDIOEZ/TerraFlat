using UnityEngine;

namespace FlatWorld.Spaceflight
{
    [DefaultExecutionOrder(10001)]
    public sealed class SpaceStarfield : MonoBehaviour
    {
        #region 固定星空样本与资源
        private const int StarCount = 420;
        private const double ParallaxStrength = .035d;
        private static readonly int ParallaxId = Shader.PropertyToID("_ParallaxOffset");
        private readonly Vector2[] positions = new Vector2[StarCount];
        private readonly float[] diameters = new float[StarCount];
        private readonly Color[] colors = new Color[StarCount];
        private readonly Vector4[] animations = new Vector4[StarCount];
        private Material material;
        private Mesh mesh;
        private Transform starPlane;
        private Camera activeCamera;
        private Color previousColor;
        private CameraClearFlags previousFlags;
        private int geometryWidth;
        private int geometryHeight;
        private SpaceVector2 previousPosition;
        private bool hasPreviousPosition;
        private double parallaxX;
        private double parallaxY;

        private void Awake()
        {
            Material template = Resources.Load<Material>("Space/Starfield");
            if (template == null) throw new System.InvalidOperationException("太空星空缺少 Space/Starfield 材质。");
            material = new Material(template) { name = "太空星空" };
            mesh = new Mesh { name = "太空星空合批网格" };
            var plane = new GameObject("背景星点", typeof(MeshFilter), typeof(MeshRenderer));
            starPlane = plane.transform; starPlane.SetParent(transform, false);
            plane.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.sortingOrder = short.MinValue;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            var random = new System.Random(17413);
            var clusters = new Vector2[4];
            for (int i = 0; i < clusters.Length; i++)
                clusters[i] = new Vector2(.1f + Next01(random) * .8f, .1f + Next01(random) * .8f);
            for (int i = 0; i < StarCount; i++)
            {
                Vector2 point = new Vector2(Next01(random), Next01(random));
                if (Next01(random) < .4f)
                {
                    float radius = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(.0001f, Next01(random)))) * .065f;
                    float angle = Next01(random) * Mathf.PI * 2f;
                    point = clusters[random.Next(clusters.Length)] +
                        new Vector2(Mathf.Cos(angle) * radius * 1.5f, Mathf.Sin(angle) * radius);
                    point = new Vector2(Mathf.Repeat(point.x, 1f), Mathf.Repeat(point.y, 1f));
                }
                positions[i] = point;
                float category = Next01(random);
                float brightness;
                if (category < .7f)
                { diameters[i] = Mathf.Lerp(3f, 5f, Next01(random)); brightness = Mathf.Lerp(.25f, .58f, Next01(random)); }
                else if (category < .95f)
                { diameters[i] = Mathf.Lerp(5f, 8f, Next01(random)); brightness = Mathf.Lerp(.55f, .8f, Next01(random)); }
                else
                { diameters[i] = Mathf.Lerp(9f, 15f, Next01(random)); brightness = Mathf.Lerp(.8f, 1f, Next01(random)); }
                float tint = Next01(random);
                Color color = tint < .18f ? new Color(.55f, .72f, 1f) :
                    tint < .3f ? new Color(1f, .48f, .35f) :
                    tint < .5f ? new Color(1f, .87f, .67f) :
                    tint < .72f ? new Color(.84f, .9f, 1f) : Color.white;
                colors[i] = new Color(color.r * brightness, color.g * brightness, color.b * brightness, 1f);
                float depth = i % 3 == 0 ? .25f : i % 3 == 1 ? .5f : 1f;
                animations[i] = new Vector4(depth, Next01(random) * Mathf.PI * 2f,
                    Mathf.Lerp(.65f, 1.8f, Next01(random)), Mathf.Lerp(.55f, 1f, Next01(random)));
            }
        }
        private static float Next01(System.Random random) => (float)random.NextDouble();
        #endregion

        #region 屏幕尺度与单网格合批
        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            if (activeCamera != camera)
            {
                RestoreCamera(); activeCamera = camera;
                previousColor = camera.backgroundColor; previousFlags = camera.clearFlags;
                camera.backgroundColor = Color.black; camera.clearFlags = CameraClearFlags.SolidColor;
                hasPreviousPosition = false;
            }
            int width = Mathf.Max(1, camera.pixelWidth), height = Mathf.Max(1, camera.pixelHeight);
            if (geometryWidth != width || geometryHeight != height) BuildGeometry(width, height);
            Vector3 bottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, 10f));
            Vector3 bottomRight = camera.ViewportToWorldPoint(new Vector3(1f, 0f, 10f));
            Vector3 topLeft = camera.ViewportToWorldPoint(new Vector3(0f, 1f, 10f));
            Vector3 center = camera.ViewportToWorldPoint(new Vector3(.5f, .5f, 10f));
            center.z = 1f;
            starPlane.SetPositionAndRotation(center, camera.transform.rotation);
            float worldWidth = Vector3.Distance(bottomLeft, bottomRight);
            float worldHeight = Vector3.Distance(bottomLeft, topLeft);
            starPlane.localScale = new Vector3(worldWidth, worldHeight, 1f);
            UpdateParallax(camera, worldWidth, worldHeight);
        }

        private void UpdateParallax(Camera camera, float worldWidth, float worldHeight)
        {
            // 只传双精度镜头位移，星点分层移动和闪烁全部交给显卡，不受浮动原点影响。
            SpaceSession session = SpaceSession.Current;
            SpaceVector2 position = session != null
                ? session.UnprojectPosition(camera.transform.position)
                : SpaceVector2.FromVector2(camera.transform.position);
            if (hasPreviousPosition && position.IsFinite)
            {
                SpaceVector2 delta = position - previousPosition;
                Vector3 right = camera.transform.right, up = camera.transform.up;
                parallaxX = (parallaxX + (delta.X * right.x + delta.Y * right.y) /
                    System.Math.Max(.001d, worldWidth) * ParallaxStrength) % 4d;
                parallaxY = (parallaxY + (delta.X * up.x + delta.Y * up.y) /
                    System.Math.Max(.001d, worldHeight) * ParallaxStrength) % 4d;
            }
            previousPosition = position;
            hasPreviousPosition = position.IsFinite;
            material.SetVector(ParallaxId, new Vector4((float)parallaxX, (float)parallaxY, 0f, 0f));
        }

        private void BuildGeometry(int width, int height)
        {
            // 九宫格副本补齐屏幕边缘，仍然只有一个网格，分辨率变化才重建几何。
            const int tileCount = 9;
            var vertices = new Vector3[StarCount * tileCount * 4];
            var uvs = new Vector2[vertices.Length];
            var centers = new Vector2[vertices.Length];
            var animationData = new System.Collections.Generic.List<Vector4>(vertices.Length);
            var vertexColors = new Color[vertices.Length];
            var triangles = new int[StarCount * tileCount * 6];
            for (int tileY = -1; tileY <= 1; tileY++)
            for (int tileX = -1; tileX <= 1; tileX++)
            for (int i = 0; i < StarCount; i++)
            {
                int sample = ((tileY + 1) * 3 + tileX + 1) * StarCount + i;
                int v = sample * 4, t = sample * 6;
                Vector2 center = positions[i] - Vector2.one * .5f;
                float halfWidth = diameters[i] / width * .5f, halfHeight = diameters[i] / height * .5f;
                vertices[v] = new Vector3(tileX - halfWidth, tileY - halfHeight);
                vertices[v + 1] = new Vector3(tileX + halfWidth, tileY - halfHeight);
                vertices[v + 2] = new Vector3(tileX + halfWidth, tileY + halfHeight);
                vertices[v + 3] = new Vector3(tileX - halfWidth, tileY + halfHeight);
                uvs[v] = Vector2.zero; uvs[v + 1] = Vector2.right;
                uvs[v + 2] = Vector2.one; uvs[v + 3] = Vector2.up;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertexColors[v + corner] = colors[i];
                    centers[v + corner] = center;
                    animationData.Add(animations[i]);
                }
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = uvs; mesh.colors = vertexColors;
            mesh.uv2 = centers; mesh.SetUVs(2, animationData); mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(3.1f, 3.1f, 1f));
            geometryWidth = width; geometryHeight = height;
        }
        #endregion

        #region 相机背景所有权与释放
        private void OnDisable() { RestoreCamera(); hasPreviousPosition = false; }
        private void RestoreCamera()
        {
            if (activeCamera == null) return;
            activeCamera.backgroundColor = previousColor; activeCamera.clearFlags = previousFlags;
            activeCamera = null;
        }
        private void OnDestroy()
        {
            RestoreCamera();
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
        #endregion
    }
}
