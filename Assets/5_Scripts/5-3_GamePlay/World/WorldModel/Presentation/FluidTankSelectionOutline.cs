using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>组合储罐只描外周边界，内部切片和相邻罐格不单独描边。</summary>
public sealed class FluidTankSelectionOutline : IDisposable
{
    #region 组合罐外轮廓
    private readonly Transform parent;
    private readonly List<LineRenderer> edges = new();
    private Material material;
    private int used;
    private float edgeWidth;
    public FluidTankSelectionOutline(Transform parent) => this.parent = parent;
    public void Show(MachineEntity node, bool selected, float cellSize = 1f, Vector3 offset = default)
    {
        used = 0;
        if (parent == null) { Hide(); return; }
        if (selected && InteractionOutlineSettings.Enabled && MachineWorld.IsFluidTankBlock(node))
        {
            using var scope = MachineWorld.UseNodeScope(node);
            Camera camera = Camera.main;
            float pixel = camera != null && camera.orthographic ? camera.orthographicSize * 2f / Mathf.Max(1, camera.pixelHeight) : .02f;
            edgeWidth = pixel * InteractionOutlineSettings.ThicknessPixels;
            foreach (MachineEntity member in MachineWorld.GetFluidTankMembers(node))
            {
                int mask = MachineWorld.GetFluidTankConnectionMask(member);
                Vector2 delta = MachineWorld.IsShipScope ? (Vector2)(member.Cell - node.Cell) :
                    WorldTopologyRuntime.ShortestDelta((Vector2)node.Cell, (Vector2)member.Cell);
                Vector3 center = (Vector3)delta * cellSize + offset;
                float half = cellSize * .5f;
                if ((mask & 1) == 0) Edge(center + new Vector3(-half, half), center + new Vector3(half, half));
                if ((mask & 2) == 0) Edge(center + new Vector3(half, half), center + new Vector3(half, -half));
                if ((mask & 4) == 0) Edge(center + new Vector3(half, -half), center + new Vector3(-half, -half));
                if ((mask & 8) == 0) Edge(center + new Vector3(-half, -half), center + new Vector3(-half, half));
            }
        }
        for (int i = used; i < edges.Count; i++) if (edges[i] != null) edges[i].enabled = false;
    }
    private void Edge(Vector3 start, Vector3 end)
    {
        LineRenderer line;
        if (used == edges.Count)
        {
            if (material == null)
                material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default"))
                    { hideFlags = HideFlags.DontSave, name = "Tank Outer Outline" };
            GameObject child = new("Tank Outer Edge") { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(parent, false);
            line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false; line.positionCount = 2;
            line.startColor = line.endColor = Color.white;
            WorldRenderingManager.Instance.ApplyRendererSorting(line, WorldSortingManager.WorldEffectCategory, 100);
            edges.Add(line);
        }
        else line = edges[used];
        used++;
        if (line.startWidth != edgeWidth) line.startWidth = line.endWidth = edgeWidth;
        if (line.GetPosition(0) != start) line.SetPosition(0, start);
        if (line.GetPosition(1) != end) line.SetPosition(1, end);
        line.enabled = true;
    }
    public void Hide()
    {
        foreach (LineRenderer edge in edges) if (edge != null) edge.enabled = false;
    }
    public void Dispose()
    {
        foreach (LineRenderer edge in edges) if (edge != null)
        {
            edge.enabled = false;
            Release(edge.gameObject);
        }
        edges.Clear();
        if (material != null) { Release(material); material = null; }
    }
    private static void Release(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
    #endregion
}
