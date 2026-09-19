using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 载具水面尾波：只消费实际水面位移，每 0.22 格发射一对扩散圆环，单船最多 32 颗粒子。
/// 复用已有雨滴水波材质，无碰撞、不参与速度计算；世界空间尾波不会随船体或乘员层级移动。
/// </summary>
[DisallowMultipleComponent]
public sealed class CarrierWaterWake : MonoBehaviour
{
    #region 状态和生命周期
    private Mod_Carrier source;
    private ParticleSystem particles;
    private Vector2 lastPosition;
    private float distanceRemainder;
    private const float EmitDistance = 0.22f;

    public void Bind(Mod_Carrier carrier)
    {
        source = carrier;
        lastPosition = transform.position;
        Clear();
    }

    /// <summary>停止/离水时不增加尾波，保留已发射粒子的自然扩散。</summary>
    private void LateUpdate()
    {
        Vector2 position = transform.position;
        float distance = WorldTopologyRuntime.Distance(lastPosition, position);
        lastPosition = position;
        if (source == null || !source.IsAvailable || distance > 4f || source.CurrentVelocity.sqrMagnitude < 0.0025f ||
            ChunkMgr.ExistingInstance == null ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample tile) ||
            (tile.Cell.Flags & FlatWorld.WorldModel.TerrainCellFlags.Water) == 0)
        {
            distanceRemainder = 0f;
            return;
        }
        distanceRemainder = Mathf.Min(distanceRemainder + distance, EmitDistance * 4f);
        if (distanceRemainder < EmitDistance) return;
        EnsureEmitter();
        Vector2 backward = -source.CurrentVelocity.normalized;
        Vector2 side = new(-backward.y, backward.x);
        while (distanceRemainder >= EmitDistance)
        {
            distanceRemainder -= EmitDistance;
            Emit(position + backward * 0.35f + side * 0.4f);
            Emit(position + backward * 0.35f - side * 0.4f);
        }
    }

    public void Clear()
    {
        distanceRemainder = 0f;
        if (particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    private void OnDisable() => Clear();
    private void OnDestroy() { if (particles != null) Destroy(particles.gameObject); }
    #endregion

    #region 可复用环形粒子
    /// <summary>独立场景根节点，仅首次产生尾波时创建，不给每一圈水纹创建对象。</summary>
    private void EnsureEmitter()
    {
        if (particles != null && particles.gameObject.scene == gameObject.scene) return;
        if (particles != null) Destroy(particles.gameObject);
        Material material = Resources.Load<Material>("Weather/Materials/RainGroundSplash");
        if (material == null) throw new System.InvalidOperationException("载具尾波缺少 RainGroundSplash 环形材质。");
        GameObject emitter = new("Carrier Water Wake");
        SceneManager.MoveGameObjectToScene(emitter, gameObject.scene);
        particles = emitter.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 32;
        main.startSpeed = 0f;
        main.startLifetime = 0.9f;
        main.startSize = 1.3f;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.enabled = false;
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.25f, 1f, 1f));
        var color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = 40;
    }

    /// <summary>后缘可能越过岸边，因此每个尾波落点也必须仍在有效水面。</summary>
    private void Emit(Vector2 position)
    {
        if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample tile) ||
            (tile.Cell.Flags & FlatWorld.WorldModel.TerrainCellFlags.Water) == 0) return;
        particles.Emit(new ParticleSystem.EmitParams
        {
            position = new Vector3(position.x, position.y, transform.position.z),
            startColor = new Color(0.72f, 0.94f, 1f, 0.6f),
            startLifetime = 0.9f,
            startSize = 1.3f
        }, 1);
    }
    #endregion
}
