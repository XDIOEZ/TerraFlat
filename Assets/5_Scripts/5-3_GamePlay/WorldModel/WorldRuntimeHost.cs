using UnityEngine;

/// <summary>
/// Unity lifecycle boundary for the engine-free world. It forwards frame time and main-thread
/// commits only; all gameplay decisions live in FlatWorld.WorldModel systems.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldRuntimeHost : MonoBehaviour
{
    private ChunkMgr owner;

    public void Bind(ChunkMgr chunkManager)
    {
        owner = chunkManager;
    }

    private void Update()
    {
        owner?.AdvanceWorldRuntime(Time.deltaTime);
    }

    private void OnDestroy()
    {
        owner = null;
    }
}
