using UnityEngine;

/// <summary>两种世界掉落后端共用的材料燃烧粒子工厂。</summary>
public static class WorldItemCombustionVisual
{
    #region 燃烧粒子

    public static ParticleSystem Create(Transform parent)
    {
        var root = new GameObject("世界掉落物_燃烧", typeof(ParticleSystem));
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        ParticleSystem particles = root.GetComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.82f, 0.18f, 1f),
            new Color(1f, 0.22f, 0.03f, 0.95f));
        main.maxParticles = 16;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 10f;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.06f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.9f, 0.25f), 0f),
                new GradientColorKey(new Color(1f, 0.18f, 0.02f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;
        return particles;
    }

    #endregion
}
