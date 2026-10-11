using UnityEngine;

namespace FlatWorld.Spaceflight
{
    [DefaultExecutionOrder(10001)]
    public sealed class SpaceStarfield : MonoBehaviour
    {
        #region 固定星空样本与资源
        private const int StarCount = 420;
        private readonly Vector2[] positions = new Vector2[StarCount];
        private readonly float[] diameters = new float[StarCount];
        private readonly Color[] colors = new Color[StarCount];
        private Texture2D texture;
        private Material material;
        private Mesh mesh;
        private Transform starPlane;
        private Camera activeCamera;
        private Color previousColor;
        private CameraClearFlags previousFlags;
        private int geometryWidth;
        private int geometryHeight;

        private void Awake()
        {
            texture = new Texture2D(9, 9, TextureFormat.RGBA32, false)
                { name = "星点亮芯与柔光", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[81];
            for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
            {
                float radiusSquared = (x - 4) * (x - 4) + (y - 4) * (y - 4);
                float alpha = radiusSquared >= 16f ? 0f : Mathf.Exp(-radiusSquared * .45f);
                pixels[y * 9 + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(pixels); texture.Apply();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new System.InvalidOperationException("太空星空缺少 Sprites/Default Shader。");
            material = new Material(shader) { name = "太空星空", mainTexture = texture };
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
                Color color = tint < .12f ? new Color(.76f, .86f, 1f) :
                    tint > .88f ? new Color(1f, .87f, .7f) : Color.white;
                colors[i] = new Color(color.r * brightness, color.g * brightness, color.b * brightness, 1f);
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
            }
            int width = Mathf.Max(1, camera.pixelWidth), height = Mathf.Max(1, camera.pixelHeight);
            if (geometryWidth != width || geometryHeight != height) BuildGeometry(width, height);
            Vector3 bottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, 10f));
            Vector3 bottomRight = camera.ViewportToWorldPoint(new Vector3(1f, 0f, 10f));
            Vector3 topLeft = camera.ViewportToWorldPoint(new Vector3(0f, 1f, 10f));
            Vector3 center = camera.ViewportToWorldPoint(new Vector3(.5f, .5f, 10f));
            center.z = 1f;
            starPlane.SetPositionAndRotation(center, camera.transform.rotation);
            starPlane.localScale = new Vector3(Vector3.Distance(bottomLeft, bottomRight),
                Vector3.Distance(bottomLeft, topLeft), 1f);
        }

        private void BuildGeometry(int width, int height)
        {
            // 星点位置与像素大小固定，缩放只调整整个背景平面，不重抽星点或增加绘制对象。
            var vertices = new Vector3[StarCount * 4];
            var uvs = new Vector2[StarCount * 4];
            var vertexColors = new Color[StarCount * 4];
            var triangles = new int[StarCount * 6];
            for (int i = 0; i < StarCount; i++)
            {
                int v = i * 4, t = i * 6;
                Vector2 center = positions[i] - Vector2.one * .5f;
                float halfWidth = diameters[i] / width * .5f, halfHeight = diameters[i] / height * .5f;
                vertices[v] = new Vector3(center.x - halfWidth, center.y - halfHeight);
                vertices[v + 1] = new Vector3(center.x + halfWidth, center.y - halfHeight);
                vertices[v + 2] = new Vector3(center.x + halfWidth, center.y + halfHeight);
                vertices[v + 3] = new Vector3(center.x - halfWidth, center.y + halfHeight);
                uvs[v] = Vector2.zero; uvs[v + 1] = Vector2.right;
                uvs[v + 2] = Vector2.one; uvs[v + 3] = Vector2.up;
                for (int corner = 0; corner < 4; corner++) vertexColors[v + corner] = colors[i];
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = uvs; mesh.colors = vertexColors;
            mesh.triangles = triangles; mesh.RecalculateBounds();
            geometryWidth = width; geometryHeight = height;
        }
        #endregion

        #region 相机背景所有权与释放
        private void OnDisable() => RestoreCamera();
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
            if (texture != null) Destroy(texture);
            if (material != null) Destroy(material);
        }
        #endregion
    }
}
