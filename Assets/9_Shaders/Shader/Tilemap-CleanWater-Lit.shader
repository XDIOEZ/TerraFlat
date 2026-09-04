Shader "FlatWorld/2D/Tilemap Clean Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}

        [Header(Clean Water Surface)]
        _DeepColor("深水颜色", Color) = (0.22, 0.055, 0.012, 1)
        _ShallowColor("浅水颜色", Color) = (1, 0.48, 0.08, 1)
        _SurfaceTint("水面染色强度", Range(0, 1)) = 0.68
        _DepthDarkening("深水压暗强度", Range(0, 1)) = 0.55
        _SwellScale("涌浪尺度", Range(0.05, 4)) = 0.42
        _DetailScale("细浪尺度", Range(0.5, 12)) = 4.4
        _WaveSpeed("水流速度", Range(-3, 3)) = 0.26
        _WaveDistortion("水流扭曲", Range(0, 4)) = 0.9
        _NormalStrength("表面起伏", Range(0, 0.8)) = 0.22
        _PixelDensity("表面采样密度", Range(1, 128)) = 64
        _FlowDirection("流动方向", Vector) = (0.7, 0.28, 0, 0)
        _TideCyclesPerDay("每日潮汐循环次数", Range(1, 4)) = 2.0
        _RippleColor("浪脊颜色", Color) = (1, 0.68, 0.3, 0.8)
        _RippleStrength("浪脊强度", Range(0, 1)) = 0.1
        _RippleScale("浪纹尺度", Range(0.25, 6)) = 2.3
        _RippleWidth("浪脊宽度", Range(0.04, 0.45)) = 0.08
        _RippleShadowStrength("浪背暗部", Range(0, 0.5)) = 0.035

        [Header(Reflection And Foam)]
        _ReflectionColor("镜面反射颜色", Color) = (1, 0.82, 0.58, 0.82)
        _ReflectionStrength("镜面反射强度", Range(0, 1)) = 0.38
        _ReflectionSmoothness("镜面反射平滑度", Range(0, 1)) = 0.86
        _ReflectionDirection("镜面环境方向", Vector) = (-0.25, 0.35, 0.9, 0)
        _SpecularColor("太阳高光", Color) = (1, 0.9, 0.7, 1)
        _SpecularStrength("太阳高光强度", Range(0, 1)) = 0.28
        _SpecularPower("太阳高光锐度", Range(4, 96)) = 56
        _SunDirection("太阳方向", Vector) = (0.28, 0.42, 0.86, 0)
        _CausticColor("焦散颜色", Color) = (1, 0.58, 0.2, 1)
        _CausticStrength("焦散强度", Range(0, 1)) = 0.16
        _FoamColor("泡沫颜色", Color) = (1, 0.82, 0.55, 0.65)
        _WhitecapStrength("浪峰泡沫", Range(0, 1)) = 0.025

        [Header(Moon Reflection)]
        _MoonReflectionColor("月光倒影颜色", Color) = (1, 0.9, 0.72, 1)
        _MoonReflectionStrength("月光倒影强度", Range(0, 8)) = 3.4
        _MoonReflectionPosition("月光倒影屏幕位置", Vector) = (0.68, 0.62, 0, 0)
        _MoonDiscRadius("月面倒影半径", Range(0.01, 0.2)) = 0.05
        _MoonTrailLength("月光带长度", Range(0.01, 0.7)) = 0.3
        _MoonTrailWidth("月光带宽度", Range(0.005, 0.2)) = 0.055

        [Header(Shore)]
        _EdgeColor("岸线暗部", Color) = (0.12, 0.03, 0.006, 1)
        _EdgeWidth("岸线宽度", Range(0.03, 0.45)) = 0.16
        _EdgeStrength("岸线暗部强度", Range(0, 1)) = 0.35
        _CornerStrength("转角叠加强度", Range(0, 1)) = 0.12
        _ShoreColor("岸线亮部", Color) = (1, 0.55, 0.18, 1)
        _ShoreStrength("岸线亮部强度", Range(0, 1)) = 0.14
        _ShoreFoamStrength("岸边泡沫强度", Range(0, 1)) = 0.18
        _FoamSpeed("岸边泡沫速度", Range(0, 3)) = 0.35

        [HideInInspector] _Color("Tint", Color) = (1,1,1,0.55)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        UsePass "FlatWorld/2D/Tilemap Water Lit/Universal2D"
        UsePass "FlatWorld/2D/Tilemap Water Lit/UniversalForward"
    }

    Fallback "Sprites/Default"
}
