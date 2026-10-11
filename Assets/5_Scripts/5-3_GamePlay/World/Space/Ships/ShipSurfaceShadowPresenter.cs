using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    internal sealed class ShipSurfaceShadowPresenter : IDisposable
    {
        #region 整船地面投影资源
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int UvTransformId = Shader.PropertyToID("_ShadowUvTransform");
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly MaterialPropertyBlock properties = new();
        private readonly HashSet<Vector2Int> cells = new();
        private readonly List<Vector3> vertices = new();
        private readonly List<Color32> colors = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<int> triangles = new();
        private double centerX, centerY, cellSize;
        private float footprintSpan, lastBlur = -1f, lastOpacity = -1f;
        private bool geometryReady;

        public ShipSurfaceShadowPresenter(Transform owner, string shipId)
        {
            Material material = Resources.Load<Material>("AIECS/ActorBlobShadow");
            if (material == null) throw new InvalidOperationException("飞船地面投影材质未加载。");
            root = new GameObject("飞船地面投影_" + shipId);
            root.transform.SetParent(owner, false);
            mesh = new Mesh { name = "飞船占地投影", hideFlags = HideFlags.DontSave };
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            WorldSortingManager.GetInstance().ApplyRenderer(renderer, WorldSortingManager.GroundShadowCategory);
            properties.SetVector(UvTransformId, new Vector4(1f, 1f, 0f, 0f));
        }
        #endregion

        #region 高度与地面姿态
        public void Synchronize(SpaceSession session, ShipState ship, double height, bool structureChanged)
        {
            float blur = SunShadowSettings.EffectiveBlurStrength;
            if (!geometryReady || structureChanged || cellSize != ship.CellSizeMeters || lastBlur != blur)
                RebuildFootprint(ship, blur);
            if (cells.Count == 0) { renderer.enabled = false; return; }

            // 影子只读取地面坐标，真实高度用于缩小和淡出，不继承船体贴图的升高偏移。
            float heightRatio = (float)(ShipLandingGuidance.SurfaceVisualRise(height) / footprintSpan);
            float heightFade = 1f / (1f + heightRatio);
            float scale = Mathf.Lerp(.45f, 1f, heightFade);
            root.transform.SetPositionAndRotation(session.DisplayPoint(ship, centerX, centerY),
                Quaternion.Euler(0f, 0f, (float)(ship.AngleRadians * Mathf.Rad2Deg)));
            root.transform.localScale = new Vector3(scale, scale, 1f);
            float opacity = ActorShadowManager.GetInstance().GetShadowOpacity(SceneManager.GetActiveScene())
                * heightFade * heightFade;
            renderer.enabled = opacity > .001f;
            if (lastOpacity != opacity)
            {
                properties.SetColor(ColorId, new Color(1f, 1f, 1f, opacity));
                renderer.SetPropertyBlock(properties);
                lastOpacity = opacity;
            }
        }
        #endregion

        #region 去重占地与外缘羽化
        private void RebuildFootprint(ShipState ship, float blur)
        {
            cells.Clear(); vertices.Clear(); colors.Clear(); uvs.Clear(); triangles.Clear();
            cellSize = ship.CellSizeMeters;
            lastBlur = blur;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive || piece.Kind is ShipPieceKind.Wire or ShipPieceKind.Pipe or ShipPieceKind.Fiber) continue;
                foreach (ShipCell cell in ShipGeometry.Footprint(piece))
                    if (cells.Add(new Vector2Int(cell.X, cell.Y)))
                    {
                        minX = Math.Min(minX, cell.X); minY = Math.Min(minY, cell.Y);
                        maxX = Math.Max(maxX, cell.X); maxY = Math.Max(maxY, cell.Y);
                    }
            }
            mesh.Clear();
            geometryReady = true;
            if (cells.Count == 0) return;
            centerX = ((double)minX + maxX + 1d) * .5d * cellSize;
            centerY = ((double)minY + maxY + 1d) * .5d * cellSize;
            footprintSpan = (float)Math.Max(cellSize, Math.Max((double)maxX - minX + 1d, (double)maxY - minY + 1d) * cellSize);
            float feather = (float)cellSize * blur;
            // 同格的地板和设备只画一次，羽化只加在外露边，避免整船内部出现格子缝或叠黑。
            foreach (Vector2Int cell in cells)
            {
                float x = (float)(cell.x * cellSize - centerX), y = (float)(cell.y * cellSize - centerY);
                float size = (float)cellSize;
                Vector3 bottomLeft = new(x, y), topLeft = new(x, y + size);
                Vector3 topRight = new(x + size, y + size), bottomRight = new(x + size, y);
                AddQuad(bottomLeft, topLeft, topRight, bottomRight, 255, 255, 255, 255);
                if (feather <= 0f) continue;
                bool left = !cells.Contains(cell + Vector2Int.left), right = !cells.Contains(cell + Vector2Int.right);
                bool bottom = !cells.Contains(cell + Vector2Int.down), top = !cells.Contains(cell + Vector2Int.up);
                if (left) AddFeather(bottomLeft, topLeft, Vector3.left * feather);
                if (right) AddFeather(topRight, bottomRight, Vector3.right * feather);
                if (bottom) AddFeather(bottomRight, bottomLeft, Vector3.down * feather);
                if (top) AddFeather(topLeft, topRight, Vector3.up * feather);
                if (left && bottom) AddCorner(bottomLeft, Vector3.left * feather, Vector3.down * feather);
                if (left && top) AddCorner(topLeft, Vector3.up * feather, Vector3.left * feather);
                if (right && top) AddCorner(topRight, Vector3.right * feather, Vector3.up * feather);
                if (right && bottom) AddCorner(bottomRight, Vector3.down * feather, Vector3.right * feather);
            }
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void AddFeather(Vector3 first, Vector3 second, Vector3 offset)
            => AddQuad(first, second, second + offset, first + offset, 255, 255, 0, 0);

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, byte alphaA, byte alphaB, byte alphaC, byte alphaD)
        {
            int start = vertices.Count;
            AddVertex(a, alphaA); AddVertex(b, alphaB); AddVertex(c, alphaC); AddVertex(d, alphaD);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        private void AddCorner(Vector3 corner, Vector3 first, Vector3 second)
        {
            int start = vertices.Count;
            AddVertex(corner, 255); AddVertex(corner + first, 0); AddVertex(corner + second, 0);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        private void AddVertex(Vector3 position, byte alpha)
        {
            vertices.Add(position); colors.Add(new Color32(255, 255, 255, alpha));
            uvs.Add(Vector2.zero);
        }
        #endregion

        #region 资源释放
        public void Dispose()
        {
            if (renderer != null) renderer.enabled = false;
            if (root != null) UnityEngine.Object.Destroy(root);
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
        }
        #endregion
    }

    public sealed partial class SpaceSession
    {
        #region 地表飞船投影生命周期
        private readonly Dictionary<string, ShipSurfaceShadowPresenter> surfaceShadows = new(StringComparer.Ordinal);
        private readonly HashSet<string> visibleSurfaceShadows = new(StringComparer.Ordinal);
        private readonly List<string> expiredSurfaceShadows = new();

        private void SynchronizeSurfaceShadow(ShipState ship, bool visible, double height, bool structureChanged)
        {
            if (!visible || IsSpaceView) return;
            if (!surfaceShadows.TryGetValue(ship.ShipId, out ShipSurfaceShadowPresenter shadow))
                surfaceShadows.Add(ship.ShipId, shadow = new ShipSurfaceShadowPresenter(transform, ship.ShipId));
            shadow.Synchronize(this, ship, height, structureChanged);
            visibleSurfaceShadows.Add(ship.ShipId);
        }

        private void RemoveExpiredSurfaceShadows()
        {
            expiredSurfaceShadows.Clear();
            foreach (string id in surfaceShadows.Keys)
                if (!visibleSurfaceShadows.Contains(id)) expiredSurfaceShadows.Add(id);
            foreach (string id in expiredSurfaceShadows)
            { surfaceShadows[id].Dispose(); surfaceShadows.Remove(id); }
        }

        private void ReleaseSurfaceShadows()
        {
            foreach (ShipSurfaceShadowPresenter shadow in surfaceShadows.Values) shadow.Dispose();
            surfaceShadows.Clear(); visibleSurfaceShadows.Clear(); expiredSurfaceShadows.Clear();
        }
        #endregion
    }
}
