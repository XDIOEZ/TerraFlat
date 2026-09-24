using FlatWorld.Audio;
using UnityEngine;

/// <summary>
/// 锄地和铲子采挖共用的本地泥土反馈：复用一个随工具存在的世界空间粒子系统，
/// 每次有效操作喷出土屑并播放泥土声。
/// 不参与耕地进度、伤害或网络权威判定。
/// </summary>
public sealed class HoeTillingFeedback : MonoBehaviour
{
    private const string ParticleObjectName = "Hoe Dirt Particles";
    private const string ParticleMaterialResourcePath = "Weather/Materials/RainParticle";
    private const string TillCueId = "item.hoe.till";
    private const int ParticleCount = 10;

    private static readonly Color SoilDark = new(0.24f, 0.12f, 0.055f, 0.92f);
    private static readonly Color SoilLight = new(0.54f, 0.31f, 0.13f, 0.9f);
    private static readonly Color PeatDark = new(0.14f, 0.09f, 0.065f, 0.94f);
    private static readonly Color PeatLight = new(0.39f, 0.26f, 0.16f, 0.92f);

    private Item hoe;
    private ParticleSystem particles;
    private ParticleSystemRenderer particleRenderer;

    public static void Play(Item sourceHoe, Vector2Int worldCell)
    {
        if (sourceHoe == null)
            return;

        HoeTillingFeedback feedback = sourceHoe.GetComponent<HoeTillingFeedback>();
        if (feedback == null)
            feedback = sourceHoe.gameObject.AddComponent<HoeTillingFeedback>();

        feedback.hoe = sourceHoe;
        feedback.Emit(worldCell, false);
    }

    /// <summary>铲子采挖泥炭时喷出深色土屑。</summary>
    public static void PlayDigging(Item sourceShovel, Vector2Int worldCell)
    {
        if (sourceShovel == null) return;
        HoeTillingFeedback feedback = sourceShovel.GetComponent<HoeTillingFeedback>();
        if (feedback == null)
            feedback = sourceShovel.gameObject.AddComponent<HoeTillingFeedback>();
        feedback.hoe = sourceShovel;
        feedback.Emit(worldCell, true);
    }

    private void Emit(Vector2Int worldCell, bool peat)
    {
        EnsureParticles();
        if (particles == null)
            return;

        Vector3 center = new(worldCell.x + 0.5f, worldCell.y + 0.32f, 0f);
        SyncSorting();

        for (int i = 0; i < ParticleCount; i++)
        {
            float horizontal = Random.Range(-0.48f, 0.48f);
            ParticleSystem.EmitParams emit = new()
            {
                position = center + new Vector3(Random.Range(-0.24f, 0.24f), Random.Range(-0.06f, 0.1f), 0f),
                velocity = new Vector3(horizontal, Random.Range(0.38f, 0.92f), 0f),
                startColor = Color.Lerp(peat ? PeatDark : SoilDark,
                    peat ? PeatLight : SoilLight, Random.value),
                startLifetime = Random.Range(0.28f, 0.48f),
                startSize = Random.Range(0.055f, 0.115f)
            };
            particles.Emit(emit, 1);
        }

        AudioService.Instance.PlayAt(TillCueId, center);
    }

    private void EnsureParticles()
    {
        if (particles != null)
            return;

        Transform existing = transform.Find(ParticleObjectName);
        GameObject particleObject = existing != null
            ? existing.gameObject
            : new GameObject(ParticleObjectName, typeof(ParticleSystem));

        if (existing == null)
        {
            particleObject.layer = gameObject.layer;
            particleObject.transform.SetParent(transform, false);
        }

        if (particleObject.GetComponent<ActorRenderEffectExclude>() == null)
            particleObject.AddComponent<ActorRenderEffectExclude>();

        particles = particleObject.GetComponent<ParticleSystem>();
        particleRenderer = particles.GetComponent<ParticleSystemRenderer>();

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = 40;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.48f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.115f);
        main.startColor = new ParticleSystem.MinMaxGradient(SoilDark, SoilLight);
        main.gravityModifier = 0.18f;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.55f),
            new Keyframe(0.18f, 1f),
            new Keyframe(1f, 0.2f)));

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.78f, 0.55f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        if (particleRenderer != null)
        {
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sharedMaterial = Resources.Load<Material>(ParticleMaterialResourcePath);
            particleRenderer.enableGPUInstancing = true;
        }

        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void SyncSorting()
    {
        if (particleRenderer == null)
            return;

        SpriteRenderer source = hoe != null ? hoe.Sprite : null;
        particleRenderer.sortingLayerID = source != null ? source.sortingLayerID : 0;
        particleRenderer.sortingOrder = source != null ? source.sortingOrder + 6 : 6;
    }
}
