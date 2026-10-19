// 轻量水体入口复用既有光照 Pass；同一低配关键字会传递给 Chunk Mesh 与 BRG。
Shader "FlatWorld/2D/Tilemap Water Low Spec Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        [PerRendererData] _LiquidDepthTexture("水深场", 2D) = "black" {}
        [HideInInspector] _LiquidDepthUvScaleOffset("水深纹理坐标", Vector) = (1,1,0,0)
        _DeepColor("深水颜色", Color) = (0.025, 0.16, 0.32, 1)
        _ShallowColor("浅水颜色", Color) = (0.18, 0.48, 0.58, 1)
        _SurfaceTint("水面染色强度", Range(0, 1)) = 1
        _RippleColor("亮纹颜色", Color) = (0.4, 0.7, 0.8, 1)
        _RippleStrength("亮纹强度", Range(0, 1)) = 0.08
        _WaveSpeed("波纹速度", Range(0, 3)) = 0.42
        _FlowDirection("默认流向", Vector) = (1, 0.35, 0, 0)
        _FlowSpeed("流动倍率", Range(0, 4)) = 1
        _EdgeWidth("岸线宽度", Range(0.03, 0.45)) = 0.16
        _CornerStrength("转角叠加强度", Range(0, 1)) = 0.18
        _ShoreColor("岸线颜色", Color) = (0.4, 0.65, 0.7, 1)
        _ShoreStrength("岸线亮度", Range(0, 1)) = 0.12
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        UsePass "FlatWorld/2D/Tilemap Water Lit/Universal2D"
        UsePass "FlatWorld/2D/Tilemap Water Lit/UniversalForward"
    }
}
