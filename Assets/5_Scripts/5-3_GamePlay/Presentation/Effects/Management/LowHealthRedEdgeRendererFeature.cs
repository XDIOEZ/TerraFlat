using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 低血量 GPU 红边后处理。只有警示强度大于零时才向最终 Game Camera 入队，
/// 使用一个全屏三角形和硬件 Alpha Blend 直接把红色混到相机颜色缓冲；不复制屏幕纹理、
/// 不创建额外 Camera，也不需要 UI Canvas，因此手机端常态几乎没有额外渲染成本。
/// </summary>
public sealed class LowHealthRedEdgeRendererFeature : ScriptableRendererFeature
{
    #region 静态状态

    private const float MinimumVisibleStrength = 0.0005f;

    private static Color edgeColor = Color.red;
    private static float edgeStrength;
    private static float edgeSmoothness = 0.82f;

    /// <summary>由 ScreenPostProcessManager 提交本帧最终参数。</summary>
    public static void SetState(Color color, float strength, float smoothness)
    {
        edgeColor = color;
        edgeStrength = Mathf.Clamp01(strength);
        edgeSmoothness = Mathf.Clamp01(smoothness);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        edgeColor = Color.red;
        edgeStrength = 0f;
        edgeSmoothness = 0.82f;
    }

    #endregion

    #region Renderer Feature

    [SerializeField]
    private Shader edgeShader; // 渲染器资源显式绑定的红边 Shader。

    private Material material;
    private RedEdgePass pass;

    public override void Create()
    {
        CoreUtils.Destroy(material);
        material = null;

        if (edgeShader == null)
        {
            Debug.LogError("[LowHealthRedEdge] Renderer Feature 未配置 edgeShader。", this);
            pass = null;
            return;
        }

        material = CoreUtils.CreateEngineMaterial(edgeShader);
        pass = new RedEdgePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null || material == null || edgeStrength <= MinimumVisibleStrength)
            return;

        ref CameraData cameraData = ref renderingData.cameraData;
        if (cameraData.cameraType != CameraType.Game || !cameraData.resolveFinalTarget)
            return;

        pass.Setup(material, edgeColor, edgeStrength, edgeSmoothness);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
    }

    #endregion


    /// <summary>在 URP 后处理之后直接混合红边，不读取源纹理。</summary>
    private sealed class RedEdgePass : ScriptableRenderPass
    {
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int EdgeStrengthId = Shader.PropertyToID("_EdgeStrength");
        private static readonly int EdgeSmoothnessId = Shader.PropertyToID("_EdgeSmoothness");

        private readonly ProfilingSampler redEdgeProfilingSampler =
            new ProfilingSampler("FlatWorld Low Health Red Edge");

        private Material passMaterial;
        private Color passColor;
        private float passStrength;
        private float passSmoothness;

        public RedEdgePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public void Setup(Material targetMaterial, Color color, float strength, float smoothness)
        {
            passMaterial = targetMaterial;
            passColor = color;
            passStrength = strength;
            passSmoothness = smoothness;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (passMaterial == null || passStrength <= MinimumVisibleStrength)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, redEdgeProfilingSampler))
            {
                passMaterial.SetColor(EdgeColorId, passColor);
                passMaterial.SetFloat(EdgeStrengthId, passStrength);
                passMaterial.SetFloat(EdgeSmoothnessId, passSmoothness);

                CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);
                cmd.DrawProcedural(
                    Matrix4x4.identity,
                    passMaterial,
                    0,
                    MeshTopology.Triangles,
                    3,
                    1);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
