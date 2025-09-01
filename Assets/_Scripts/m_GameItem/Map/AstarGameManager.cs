using NavMeshPlus.Components;
using Pathfinding;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AstarGameManager : SingletonAutoMono<AstarGameManager>
{
    public AstarPath Pathfinder;
    public bool Init = false;

    public void Start()
    {
        // GameChunkManager.Instance.OnChunkLoadFinish += UpdateNavMeshAsync; // 监听地图块加载完成事件，更新 NavMesh
        Pathfinder = GetComponent<AstarPath>();
    }


    [Button("Update NavMesh")]
    public void UpdateMeshAsync(Vector2 center = default, int radius = 2)
    {
        if (!Init)
        {
            // 第一次调用，完整扫描
            AstarPath.active.Scan();
            Init = true;
        }
        else
        {
            // 当前区块大小
            Vector2 chunkSize = GameChunkManager.GetChunkSize();

            // 如果 center 是 Chunk 的左下角，就 +0.5f * size 对齐到中心
            center += chunkSize * 0.5f;

            // 设置 GridGraph 的中心
            AstarPath.active.data.gridGraph.center = new Vector3(center.x, center.y, 0f);

            // 设置 GridGraph 的尺寸
            int width = Mathf.RoundToInt(chunkSize.x * (2 * radius - 1));//
            int depth = Mathf.RoundToInt(chunkSize.y * (2 * radius - 1));
            float nodeSize = 1f; // 每个格子大小（根据你的 Tile 单位调整）

            AstarPath.active.data.gridGraph.SetDimensions(width, depth, nodeSize);

            // 全量重新生成（同步，可能会卡顿）
            AstarPath.active.Scan();

            Debug.Log($"✅ NavMesh 更新完成，中心点: {center}，范围: {radius} 个 Chunk");
        }
    }




    IEnumerator Starts()
    {
        foreach (Progress progress in AstarPath.active.ScanAsync())
        {
            Debug.Log("Scanning... " + progress.ToString());
            yield return null;
        }
    }
    private class DebugBounds
    {
        public Bounds bounds;
        public float time; // 记录绘制的起始时间
    }

    private List<DebugBounds> updatedBounds = new List<DebugBounds>();
    /// <summary>
    /// 更新矩形区域并在 Scene 窗口显示 Gizmos（延迟一帧调用）
    /// </summary>
    public void UpdateArea_Rectangle(Vector2 center, int length, int width)
    {
        Vector3 boundsCenter = new Vector3(center.x, center.y, 0f);
        Vector3 boundsSize = new Vector3(length, width, 1);
        Bounds bounds = new Bounds(boundsCenter, boundsSize);

        // 启动协程延迟一帧再更新
        StartCoroutine(DelayedUpdate(bounds));
    }

    private IEnumerator DelayedUpdate(Bounds bounds)
    {
        yield return null; // 等待一帧，避免递归调用异常

        var guo = new GraphUpdateObject(bounds);
        AstarPath.active.UpdateGraphs(guo);

        // Gizmos 可视化
        updatedBounds.Add(new DebugBounds { bounds = bounds, time = Time.time });
    }

    private void OnDrawGizmos()
    {
        if (updatedBounds == null) return;

        Gizmos.color = Color.red;

        // 遍历绘制存活时间未超过10秒的矩形
        for (int i = updatedBounds.Count - 1; i >= 0; i--)
        {
            DebugBounds db = updatedBounds[i];
            if (Time.time - db.time < 10f)
            {
                Gizmos.DrawWireCube(db.bounds.center, db.bounds.size);
            }
            else
            {
                // 超过10秒，移除
                updatedBounds.RemoveAt(i);
            }
        }
    }
}
