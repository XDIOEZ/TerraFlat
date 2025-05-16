using UnityEngine;
using NavMeshPlus;
using NavMeshPlus.Components;
using zFramework.Internal;
using System.Threading.Tasks;

using NaughtyAttributes;

public class NavMeshRuntimeBake : MonoBehaviour
{
    public NavMeshSurface surface;
    public bool OnStartBuild;

    public void Start()
    {
        if (OnStartBuild)
        {
            UpdateNavMeshAsync();
        }
    }
    [Button("Update NavMesh")]
    public  void UpdateNavMeshAsync()
    {

        surface.BuildNavMesh();  // ����ʱ�決}

       // Debug.Log("�決���");
    }
}
