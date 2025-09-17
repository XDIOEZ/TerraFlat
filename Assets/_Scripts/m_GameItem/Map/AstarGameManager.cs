// （完整类代码 — 基本保留你原来的所有方法，仅在 #region 后面新增了 UpdateAreaPenalty_Rectangle 与协程实现）
using NavMeshPlus.Components;
using Pathfinding;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

#if UNITY_EDITOR
using UnityEditor; // 仅Editor模式用Handles.Label，打包不报错
#endif

public class AstarGameManager : SingletonAutoMono<AstarGameManager>
{
    public AstarPath Pathfinder;
    public bool Init = false;
    // 权重修改区域的Gizmos可视化数据
    private List<DebugBounds> penaltyModifiedBounds = new List<DebugBounds>();

    [Header("按键调权重配置（策划可改）")]
    public bool enableKeyControl = true; // 游戏状态下启用按键控制
    public int penaltyStep = 100; // 每次按键增减的权重步长
    public int minPenalty = 0; // 权重最小值（避免负权重）
    public int maxPenalty = 10000; // 权重最大值（避免寻路异常）
    public Camera mainCamera; // 转换鼠标坐标（未指定则自动获取）

    public void Start()
    {
        Pathfinder = GetComponent<AstarPath>();
        // 自动获取MainCamera（避免策划忘记赋值）
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("❌ 未找到MainCamera！请给相机添加'Tag:MainCamera'或手动指定");
            }
        }
    }


    [Button("Update NavMesh")]
    public void UpdateMeshAsync(Vector2 center = default, int radius = 1)
    {
        if (!Init)
        {
            AstarPath.active.Scan();
            Init = true;
        }
        else
        {
            Vector2 chunkSize = ChunkMgr.GetChunkSize();
            Vector2 Newcenter = center + chunkSize * 0.5f;
            AstarPath.active.data.gridGraph.center = new Vector3(Newcenter.x, Newcenter.y, 0f);

            int width = Mathf.RoundToInt(chunkSize.x * (2 * radius - 1));
            int depth = Mathf.RoundToInt(chunkSize.y * (2 * radius - 1));
            float nodeSize = 1f;

            AstarPath.active.data.gridGraph.SetDimensions(width, depth, nodeSize);
            AstarPath.active.Scan();
            Vector2Int centerInt = Vector2Int.RoundToInt(center);
            Data_TileMap Data = ChunkMgr.Instance.Chunk_Dic[centerInt.ToString()].Map.Data;
            foreach (var kvp in Data.TileData.Values)
            {
                AstarGameManager.Instance.ModifyNodePenalty_Optimized(kvp[^1].position, kvp[^1].Penalty);
            }

            Debug.Log($"✅ NavMesh 更新完成，中心点: {center}，范围: {radius} 个 Chunk");
        }
    }


    #region 权重（Penalty）修改功能（适配 A* 3.5 及以下超旧版本）
    [Button("修改单个节点权重")]//TODO 这个是及高频调用的 1帧 4w+ 次，优化一下
    public void ModifyNodePenalty(Vector3 worldPos, uint newPenalty = 1000)
    {
        if (Pathfinder == null || AstarPath.active == null)
        {
            Debug.LogError("❌ AstarPath 组件未初始化！");
            return;
        }

        // 获取目标位置节点
        NNInfo nnInfo = AstarPath.active.GetNearest(worldPos);
        GraphNode targetNode = nnInfo.node;

        if (targetNode == null)
        {
            Debug.LogError($"❌ 节点获取失败！位置：{worldPos}（不在寻路图内）");
            return;
        }

        if (!targetNode.Walkable)
        {
            Debug.Log($"⚠️ 节点不可通行，已跳过。位置：{worldPos}");
            return;
        }

        // 修改权重
        targetNode.Penalty = newPenalty;

        // Gizmos可视化（标记为“按键调整”，黄色线框）
        Bounds nodeBounds = new Bounds(worldPos, Vector3.one * 0.8f);
        penaltyModifiedBounds.Add(new DebugBounds
        {
            bounds = nodeBounds,
            time = Time.time,
            isKeyAdjust = true // 标记为按键调整
        });
    }

    /// <summary>
    /// 高频调用优化版：修改单个节点权重
    /// - 无Log（避免控制台刷屏卡顿）
    /// - 避免重复赋值（Penalty相同则跳过）
    /// - Gizmos记录数量有限制
    /// </summary>
    public void ModifyNodePenalty_Optimized(Vector3 worldPos, uint newPenalty = 1000)
    {
        if (Pathfinder == null || AstarPath.active == null)
        {
            // 这里不报错，避免高频调用抛出大量日志
            return;
        }

        // 1. 获取节点
        NNInfo nnInfo = AstarPath.active.GetNearest(worldPos);
        GraphNode targetNode = nnInfo.node;
        if (targetNode == null || !targetNode.Walkable)
        {
            // 节点无效或不可通行，直接跳过
            return;
        }

        // 2. 避免重复赋值
        if (targetNode.Penalty == newPenalty) return;

        // 3. 修改权重
        targetNode.Penalty = newPenalty;
    }



    [Button("修改区域权重")]
    public void ModifyRegionPenalty(Vector2 center, int sizeX, int sizeY, int penaltyDelta = 500)
    {
        if (Pathfinder == null || AstarPath.active == null)
        {
            Debug.LogError("❌ AstarPath 组件未初始化！");
            return;
        }

        // 1. 构建区域边界（确保与节点对齐）
        Vector3 boundsCenter = new Vector3(center.x, center.y, 0f);
        Bounds targetRegion = new Bounds(boundsCenter, new Vector3(sizeX, sizeY, 1f));

        // 2. 超旧版本核心配置：无updatePenalty！仅需addPenalty+禁用modifyWalkability（避免删节点）
        GraphUpdateObject guo = new GraphUpdateObject(targetRegion);
        guo.modifyWalkability = false; // 绝对禁用“可通行性修改”，彻底防止误删节点
        guo.addPenalty = penaltyDelta; // 直接设置权重增量（正数加，负数减）

        // 3. 应用区域权重修改
        AstarPath.active.UpdateGraphs(guo);
        Debug.Log($"✅ 区域权重修改成功！\n区域：{targetRegion}\n权重增量：{penaltyDelta}\n提示：权重建议控制在 {minPenalty}-{maxPenalty} 内");

        // 4. Gizmos可视化（标记为“区域调整”，绿色线框）
        penaltyModifiedBounds.Add(new DebugBounds
        {
            bounds = targetRegion,
            time = Time.time,
            isKeyAdjust = false // 标记为区域调整
        });
    }
    #endregion


    #region 按键调整鼠标节点权重（策划友好功能）
    private void Update()
    {
        // 仅在“功能启用+相机有效+寻路组件就绪”时生效
        if (!enableKeyControl || mainCamera == null || Pathfinder == null || AstarPath.active == null)
        {
            return;
        }

        // 1. 按“+”（主键盘/小键盘）：增加鼠标位置节点权重
        if (Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            AdjustPenaltyAtMousePos(penaltyStep);
        }

        // 2. 按“-”（主键盘/小键盘）：减少鼠标位置节点权重
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            AdjustPenaltyAtMousePos(-penaltyStep);
        }
    }

    /// <summary>
    /// 调整鼠标位置下节点的权重（增量方式）
    /// </summary>
    private void AdjustPenaltyAtMousePos(int penaltyDelta)
    {
        // 1. 鼠标屏幕坐标 → 世界坐标（适配2D场景，z轴设为0）
        Vector3 mouseScreenPos = Input.mousePosition;
        mouseScreenPos.z = Mathf.Abs(mainCamera.transform.position.z); // 确保与节点在同一平面
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
        mouseWorldPos.z = 0; // 强制2D平面（匹配寻路节点z轴）

        // 2. 世界坐标 → 寻路节点
        NNInfo nnInfo = AstarPath.active.GetNearest(mouseWorldPos);
        GraphNode targetNode = nnInfo.node;

        // 3. 节点有效性校验
        if (targetNode == null || !targetNode.Walkable)
        {
            Debug.LogWarning($"⚠️ 鼠标位置无有效节点！\n世界坐标：{mouseWorldPos}\n是否可通行：{targetNode?.Walkable ?? false}");
            return;
        }

        // 4. 计算新权重（限制范围，避免异常）
        int currentPenalty = (int)targetNode.Penalty; // 超旧版本Penalty为int/uint，强制转换安全
        int newPenalty = Mathf.Clamp(currentPenalty + penaltyDelta, minPenalty, maxPenalty);

        // 5. 应用新权重 + 日志提示
        targetNode.Penalty = (uint)newPenalty;
        Debug.Log($"🔧 鼠标节点权重调整成功！\n位置：{mouseWorldPos}\n原权重：{currentPenalty} → 新权重：{newPenalty}\n步长：{penaltyDelta}");

        // 6. Gizmos可视化（标记为“按键调整”）
        Bounds nodeBounds = new Bounds(mouseWorldPos, Vector3.one * 0.8f);
        penaltyModifiedBounds.Add(new DebugBounds
        {
            bounds = nodeBounds,
            time = Time.time,
            isKeyAdjust = true
        });
    }
    #endregion


    #region Gizmos 可视化与辅助类
    // 修复核心：补充 isKeyAdjust 字段，用于区分权重修改类型
    private class DebugBounds
    {
        public Bounds bounds; // 区域边界
        public float time; // 绘制起始时间
        public bool isKeyAdjust = false; // true=按键调整，false=区域调整（新增字段）
    }

    private List<DebugBounds> updatedBounds = new List<DebugBounds>();
    //TODO 能参考下面的更新特定区块的烘焙 然后新建一个更新特定区块的权重的方法吗

    // —— 新增方法：按矩形区域更新权重（支持增量 addPenalty 与 逐节点绝对设置两种模式）
    [Button("更新特定区块权重")]
    public void UpdateAreaPenalty_Rectangle(Vector2 center, int length, int width, int penaltyValue = 500, bool setAbsolute = false)
    {
        if (Pathfinder == null || AstarPath.active == null)
        {
            Debug.LogError("❌ AstarPath 组件未初始化！");
            return;
        }

        Vector3 boundsCenter = new Vector3(center.x, center.y, 0f);
        Bounds targetRegion = new Bounds(boundsCenter, new Vector3(length, width, 1f));

        if (!setAbsolute)
        {
            // 快速批量：使用 GraphUpdateObject.addPenalty（增量模式，性能最好）
            GraphUpdateObject guo = new GraphUpdateObject(targetRegion);
            guo.modifyWalkability = false;
            guo.addPenalty = penaltyValue;
            AstarPath.active.UpdateGraphs(guo);

            Debug.Log($"✅ 区域（增量）权重修改成功！ 区域：{targetRegion} 权重增量：{penaltyValue}");
        }
        else
        {
            // 逐节点绝对赋值（比较耗时，采用协程分帧）
            int clamped = Mathf.Clamp(penaltyValue, minPenalty, maxPenalty);
            uint targetPenalty = (uint)clamped;

            // 取 grid 的 nodeSize（如果可用），用于精确采样
            float nodeSize = 1f;
            var gg = AstarPath.active.data.gridGraph as GridGraph;
            if (gg != null) nodeSize = gg.nodeSize;

            // 计算采样的左下角与范围
            float halfLen = length * 0.5f;
            float halfWid = width * 0.5f;
            float left = center.x - halfLen;
            float bottom = center.y - halfWid;

            // 启动协程做分帧设置（避免卡顿）
            StartCoroutine(IterateSetPenalty_CoRod(left, bottom, length, width, nodeSize, targetPenalty));
            Debug.Log($"🔧 已开始异步（分帧）区域绝对权重设置：区域中心 {center} 大小 {length}x{width} 目标权重 {targetPenalty}");
        }

        // 可视化（区域调整为绿色）
        penaltyModifiedBounds.Add(new DebugBounds
        {
            bounds = targetRegion,
            time = Time.time,
            isKeyAdjust = false
        });
    }

    // 协程：按 nodeSize 逐点采样并设置权重（带分帧）
    private IEnumerator IterateSetPenalty_CoRod(float left, float bottom, int length, int width, float nodeSize, uint targetPenalty)
    {
        if (AstarPath.active == null) yield break;

        float right = left + length;
        float top = bottom + width;

        int rows = 0;
        for (float x = left + nodeSize * 0.5f; x < right; x += nodeSize)
        {
            for (float y = bottom + nodeSize * 0.5f; y < top; y += nodeSize)
            {
                Vector3 samplePos = new Vector3(x, y, 0f);
                NNInfo nn = AstarPath.active.GetNearest(samplePos);
                GraphNode node = nn.node;
                if (node != null && node.Walkable)
                {
                    if (node.Penalty != targetPenalty)
                    {
                        node.Penalty = targetPenalty;
                    }
                }
            }

            rows++;
            // 每处理若干列分帧一次，避免卡顿（数值可调）
            if (rows % 8 == 0)
            {
                yield return null;
            }
        }
    }

    public void UpdateArea_Rectangle(Vector2 center, int length, int width)
    {
        Vector3 boundsCenter = new Vector3(center.x, center.y, 0f);
        Vector3 boundsSize = new Vector3(length, width, 1);
        Bounds bounds = new Bounds(boundsCenter, boundsSize);

        StartCoroutine(DelayedUpdate(bounds));
    }

    private IEnumerator DelayedUpdate(Bounds bounds)
    {
        yield return null;
        var guo = new GraphUpdateObject(bounds);
        AstarPath.active.UpdateGraphs(guo);
        updatedBounds.Add(new DebugBounds { bounds = bounds, time = Time.time });
    }

    // 优化Gizmos：区分“原有更新区（红）”“按键调整（黄）”“区域调整（绿）”
    private void OnDrawGizmos()
    {
        // 1. 绘制原有NavMesh更新区域（红色线框，保留10秒）
        if (updatedBounds != null && updatedBounds.Count > 0)
        {
            Gizmos.color = Color.red;
            for (int i = updatedBounds.Count - 1; i >= 0; i--)
            {
                DebugBounds db = updatedBounds[i];
                if (Time.time - db.time < 10f)
                {
                    Gizmos.DrawWireCube(db.bounds.center, db.bounds.size);
                }
                else
                {
                    updatedBounds.RemoveAt(i); // 超时移除，避免内存泄漏
                }
            }
        }

        // 2. 绘制权重修改区域（黄色=按键调整，绿色=区域调整）
        if (penaltyModifiedBounds != null && penaltyModifiedBounds.Count > 0)
        {
            for (int i = penaltyModifiedBounds.Count - 1; i >= 0; i--)
            {
                DebugBounds db = penaltyModifiedBounds[i];
                // 按键调整保留5秒，区域调整保留10秒
                if (Time.time - db.time < (db.isKeyAdjust ? 5f : 10f))
                {
                    // 颜色区分：黄色=按键，绿色=区域
                    Gizmos.color = db.isKeyAdjust ? Color.yellow : Color.green;
                    Gizmos.DrawWireCube(db.bounds.center, db.bounds.size);

                    // Editor模式下添加文字标签（更直观）
#if UNITY_EDITOR
                    string labelText = db.isKeyAdjust ? "Key Adjust" : "Area Adjust";
                    Handles.Label(db.bounds.center + Vector3.up * 0.5f, labelText);
#endif
                }
                else
                {
                    penaltyModifiedBounds.RemoveAt(i); // 超时移除
                }
            }
        }
    }
    #endregion
}
