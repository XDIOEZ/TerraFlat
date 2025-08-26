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

        if (Init == false)
        {
            surface.BuildNavMeshAsync();
            Init = true;
        }
        else
        {
            surface.UpdateNavMesh(surface.navMeshData);
        }
    }
}