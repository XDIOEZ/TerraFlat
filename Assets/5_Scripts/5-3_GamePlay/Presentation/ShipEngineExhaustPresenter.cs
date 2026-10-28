using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlatWorld.Spaceflight
{
    [DisallowMultipleComponent]
    public sealed class ShipEngineExhaustPresenter : MonoBehaviour
    {
        #region 喷焰配置与生命周期
        [Min(1f)] public float ParticlesPerSecond = 90f;
        [Min(.01f)] public float ParticleLifetime = .28f;
        [Min(.01f)] public float ExhaustSpeed = 4.5f;
        [Min(.01f)] public float NozzleWidth = .18f;

        private SpaceSession session;
        private ShipPieceState piece;
        private SpriteRenderer source;
        private ParticleSystem particles;
        private ParticleSystemRenderer particleRenderer;
        private Sprite syncedNozzleSprite;
        private bool emitting;
        private float appliedOutput = -1f;
        private AudioHandle engineAudio;
        private float appliedAudioVolume = -1f;
        private WorldPresentationSortingDomain sortingDomain;
        private int rawSortingLayer, rawSortingOrder, rawRenderQueue;

        public void Bind(SpaceSession owner, ShipPieceState engine, SpriteRenderer renderer)
        {
            if (!ReferenceEquals(source, renderer))
            {
                // 船件休眠或视图复用后重新绑定当前喷口，不继续使用旧视觉节点。
                if (particles != null && renderer != null && particles.transform.parent != renderer.transform)
                    particles.transform.SetParent(renderer.transform, false);
                syncedNozzleSprite = null;
            }
            session = owner;
            piece = engine;
            source = renderer;
        }

        public void SetSortingDomain(WorldPresentationSortingDomain domain)
        {
            if (ReferenceEquals(sortingDomain, domain)) return;
            RenderPipelineManager.beginContextRendering -= SubmitSortingKey;
            if (sortingDomain != null)
            {
                if (particleRenderer != null) sortingDomain.RemoveLocalSortingKey(particleRenderer);
                sortingDomain.Disposing -= OnSortingDomainDisposing;
            }
            sortingDomain = domain;
            if (domain != null)
            {
                domain.Disposing += OnSortingDomainDisposing;
                if (isActiveAndEnabled) RenderPipelineManager.beginContextRendering += SubmitSortingKey;
            }
        }

        private void OnSortingDomainDisposing() => SetSortingDomain(null);

        private void OnEnable()
        {
            RenderPipelineManager.beginContextRendering -= SubmitSortingKey;
            if (sortingDomain != null) RenderPipelineManager.beginContextRendering += SubmitSortingKey;
        }

        private void SubmitSortingKey(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (sortingDomain == null || particleRenderer == null) return;
            if (!particleRenderer.enabled || particles == null || particles.particleCount == 0)
            {
                sortingDomain.RemoveLocalSortingKey(particleRenderer);
                return;
            }
            sortingDomain.ApplyLocalRendererSortingKey(particleRenderer, rawSortingLayer, rawSortingOrder,
                rawRenderQueue, particleRenderer.bounds.center.y);
        }

        private void LateUpdate()
        {
            float output = session != null && piece?.IsAlive == true && source != null && source.enabled
                ? Mathf.Clamp01(session.GetEngineOutput(piece.PieceId)) : 0f;
            UpdateEngineAudio(output);
            if (output <= 0f)
            {
                if (particles != null && source != null) SynchronizeNozzle();
                if (emitting) particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                emitting = false;
                appliedOutput = -1f;
                return;
            }
            EnsureParticles();
            SynchronizeNozzle();
            if (!Mathf.Approximately(appliedOutput, output))
            {
                var emission = particles.emission;
                emission.rateOverTime = ParticlesPerSecond * output;
                var velocity = particles.velocityOverLifetime;
                float speed = ExhaustSpeed * Mathf.Lerp(.5f, 1f, output);
                velocity.y = new ParticleSystem.MinMaxCurve(-speed, -speed);
                appliedOutput = output;
            }
            if (!emitting)
            {
                particles.Play(false);
                particles.Emit(4);
                emitting = true;
            }
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= SubmitSortingKey;
            if (sortingDomain != null && particleRenderer != null) sortingDomain.RemoveLocalSortingKey(particleRenderer);
            engineAudio.Stop(.15f); engineAudio = AudioHandle.Invalid; appliedAudioVolume = -1f;
            if (particles != null) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            emitting = false;
            appliedOutput = -1f;
            syncedNozzleSprite = null;
        }
        private void OnDestroy()
        {
            RenderPipelineManager.beginContextRendering -= SubmitSortingKey;
            SetSortingDomain(null);
        }
        private void UpdateEngineAudio(float output)
        {
            if (output <= 0f || source == null || !AudioService.HasInstance)
            {
                engineAudio.Stop(.2f); engineAudio = AudioHandle.Invalid; appliedAudioVolume = -1f;
                return;
            }
            // 声音跟随实际耗油出力，真空仅保留地表十分之一的反馈音量。
            bool space = session.FindPiece(piece.PieceId, out ShipState ship, out _) &&
                session.GetFlight(ship.ShipId)?.Phase == ShipFlightPhase.Cruising;
            float volume = Mathf.Sqrt(output) * (space ? .1f : 1f);
            if (!engineAudio.IsPlaying)
            {
                var options = AudioPlayOptions.Attached(source.transform, volume);
                options.FadeIn = .15f;
                engineAudio = AudioService.Instance.Play("ship.engine.loop", options);
                appliedAudioVolume = volume;
            }
            else if (!Mathf.Approximately(appliedAudioVolume, volume))
            {
                AudioService.Instance.SetVolumeScale(engineAudio, volume);
                appliedAudioVolume = volume;
            }
        }
        #endregion

        #region 独立粒子表现
        private void EnsureParticles()
        {
            if (particles != null) return;
            Material material = Resources.Load<Material>("ShipEngineExhaust");
            if (material == null) throw new InvalidOperationException("飞船引擎喷焰缺少 ShipEngineExhaust 粒子材质。");

            GameObject nozzle = new("ShipEngineExhaust");
            nozzle.transform.SetParent(source.transform, false);
            particles = nozzle.AddComponent<ParticleSystem>();
            syncedNozzleSprite = null;
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            // 局部粒子跟着机身、安装朝向和升空偏移，浮动原点切换也不会拉出长尾。
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 48;
            main.startLifetime = new ParticleSystem.MinMaxCurve(ParticleLifetime * .7f, ParticleLifetime);
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(NozzleWidth * .7f, NozzleWidth * 1.3f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(NozzleWidth * 1.6f, NozzleWidth * 2.6f);
            main.startSizeZ = 1f;
            main.startColor = Color.white;
            main.gravityModifier = 0f;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(NozzleWidth * .5f, .01f, .01f);
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-.25f, .25f);
            velocity.y = new ParticleSystem.MinMaxCurve(-ExhaustSpeed, -ExhaustSpeed);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, .65f), new Keyframe(.15f, 1f), new Keyframe(1f, 0f)));
            Gradient gradient = new();
            gradient.SetKeys(new[]
                {
                    new GradientColorKey(new Color(.85f, .97f, 1f), 0f),
                    new GradientColorKey(new Color(.25f, .65f, 1f), .25f),
                    new GradientColorKey(new Color(1f, .55f, .12f), .65f),
                    new GradientColorKey(new Color(1f, .18f, .04f), 1f)
                }, new[] { new GradientAlphaKey(.95f, 0f), new GradientAlphaKey(.8f, .55f), new GradientAlphaKey(0f, 1f) });
            var color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = gradient;

            particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = material;
            rawRenderQueue = material.renderQueue;
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.alignment = ParticleSystemRenderSpace.Local;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            particleRenderer.lightProbeUsage = LightProbeUsage.Off;
            particleRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            SynchronizeNozzle();
        }

        private void SynchronizeNozzle()
        {
            Sprite sprite = source.sprite;
            if (syncedNozzleSprite != sprite)
            {
                syncedNozzleSprite = sprite;
                if (sprite != null)
                {
                    Bounds bounds = sprite.bounds;
                    Vector3 position = new(bounds.center.x, bounds.min.y + .025f, 0f);
                    if (particles.transform.localPosition != position)
                        particles.transform.localPosition = position;
                }
            }
            // 喷焰排在船地板、管道和电线后面，停推后的残留粒子也保持同一层级。
            WorldRenderingManager.Instance.GetSortingKey(WorldSortingManager.VehicleCategory,
                out int layerId, out int order);
            if (particleRenderer.sortingLayerID != layerId)
                particleRenderer.sortingLayerID = layerId;
            int exhaustOrder = checked(order - 5);
            rawSortingLayer = layerId;
            rawSortingOrder = exhaustOrder;
            if (particleRenderer.sortingOrder != exhaustOrder)
                particleRenderer.sortingOrder = exhaustOrder;
            int gameObjectLayer = source.gameObject.layer;
            if (particles.gameObject.layer != gameObjectLayer)
                particles.gameObject.layer = gameObjectLayer;
        }
        #endregion
    }
}
