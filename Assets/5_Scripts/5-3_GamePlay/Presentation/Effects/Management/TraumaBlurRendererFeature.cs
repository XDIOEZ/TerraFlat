using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>脑震荡独立模糊通道；仅最终游戏相机且强度非零时复制屏幕做九点采样，不改变其它来源的红边。</summary>
public sealed class TraumaBlurRendererFeature : ScriptableRendererFeature
{
    #region 视觉通道
    private static float strength;
    private Material material;
    private BlurPass pass;
    public static void SetStrength(float value) => strength = Mathf.Clamp(value, 0f, 4f);

    public override void Create()
    {
        CoreUtils.Destroy(material);
        material = CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("Shaders/BodyTraumaBlur"));
        pass = new BlurPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null || strength <= 0f || renderingData.cameraData.cameraType != CameraType.Game ||
            !renderingData.cameraData.resolveFinalTarget) return;
        pass.Setup(material, strength);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) { CoreUtils.Destroy(material); pass?.Dispose(); }

    private sealed class BlurPass : ScriptableRenderPass
    {
        private static readonly int SourceTexelSizeId = Shader.PropertyToID("_SourceTexelSize");
        private RTHandle temporary;
        private Material material;
        private float radius;
        public BlurPass() { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing; ConfigureInput(ScriptableRenderPassInput.Color); }
        public void Setup(Material value, float strength) { material = value; radius = strength; }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref temporary, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "BodyTraumaBlur");
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var cmd = CommandBufferPool.Get("Body trauma blur");
            material.SetFloat("_Radius", radius);
            // 显式传入源纹理像素尺寸，避免依赖当前 URP Blit 头文件未提供的变量。
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            int sourceWidth = Mathf.Max(1, source.rt != null ? source.rt.width : descriptor.width);
            int sourceHeight = Mathf.Max(1, source.rt != null ? source.rt.height : descriptor.height);
            material.SetVector(SourceTexelSizeId, new Vector4(1f / sourceWidth, 1f / sourceHeight, 0f, 0f));
            Blitter.BlitCameraTexture(cmd, source, temporary, material, 0);
            Blitter.BlitCameraTexture(cmd, temporary, source);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        public void Dispose() { temporary?.Release(); temporary = null; }
    }
    #endregion
}
