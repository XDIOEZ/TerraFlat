using UnityEngine;
using NavMeshPlus;
using NavMeshPlus.Components;
using Sirenix.OdinInspector;
using System.Diagnostics;

public class NavMeshRuntimeBake : MonoBehaviour
{
    public NavMeshSurface surface;
    public bool Init = false;

    // ========= 单例部分 =========
    public static NavMeshRuntimeBake Instance { get; private set; }

    private void Awake()
    {

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // 确保只有一个实例
            return;
        }

        Instance = this;

        // 如果需要跨场景保留单例，就加上这句
        // DontDestroyOnLoad(gameObject);
    }
    public void Start()
    {
        GameChunkManager.Instance.OnChunkLoadFinish += UpdateNavMeshAsync; // 监听地图块加载完成事件，更新 NavMesh
        GameManager.Instance.Event_ExitGame_End += ClearMeshData;
    }

    public void ClearMeshData()
    {
        surface.navMeshData = null;
        Init = false;
    }

    [Button("Update NavMesh")]
    public void UpdateNavMeshAsync(Chunk chunk)
    {
        // 开始计时
        Stopwatch stopwatch = Stopwatch.StartNew();

        //   UnityEngine.Debug.Log("开始烘焙 NavMesh...");

        if (Init == false)
        {
            surface.BuildNavMeshAsync();
            Init = true;
        }
       
        surface.UpdateNavMesh(surface.navMeshData);
        
        // 停止计时
        stopwatch.Stop();
        }

    // 可选：更详细的时间统计版本
    [Button("Update NavMesh (Detailed)")]
    public void UpdateNavMeshWithDetailedTiming()
    {
        var totalStopwatch = Stopwatch.StartNew();

        UnityEngine.Debug.Log("=== NavMesh 烘焙开始 ===");
        UnityEngine.Debug.Log($"烘焙区域大小: {surface.size}");
        UnityEngine.Debug.Log($"收集对象模式: {surface.collectObjects}");

        // 记录烘焙前的时间戳
        var startTime = System.DateTime.Now;

        // 烘焙
        surface.BuildNavMesh();

        totalStopwatch.Stop();

        // 详细输出
        UnityEngine.Debug.Log("=== NavMesh 烘焙完成 ===");
        UnityEngine.Debug.Log($"开始时间: {startTime:HH:mm:ss.fff}");
        UnityEngine.Debug.Log($"结束时间: {System.DateTime.Now:HH:mm:ss.fff}");
        UnityEngine.Debug.Log($"总耗时: {totalStopwatch.ElapsedMilliseconds} ms");
        UnityEngine.Debug.Log($"总耗时: {totalStopwatch.Elapsed.TotalSeconds:F3} 秒");

        // 性能等级提示
        if (totalStopwatch.ElapsedMilliseconds < 100)
            UnityEngine.Debug.Log("<color=green>性能: 优秀</color>");
        else if (totalStopwatch.ElapsedMilliseconds < 500)
            UnityEngine.Debug.Log("<color=yellow>性能: 良好</color>");
        else
            UnityEngine.Debug.Log("<color=red>性能: 需要优化</color>");
    }
}