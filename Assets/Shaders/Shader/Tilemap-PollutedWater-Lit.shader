Shader "FlatWorld/2D/Tilemap Polluted Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}

        [Header(Polluted Water Surface)]
        _DeepColor("深水颜色", Color) = (0.005, 0.025, 0.012, 1)
        _ShallowColor("浅水颜色", Color) = (0.065, 0.16, 0.055, 1)
        _SurfaceTint("水面染色强度", Range(0, 1)) = 0.78
        _SwellScale("涌浪尺度", Range(0.05, 4)) = 0.5
        _DetailScale("细浪尺度", Range(0.5, 12)) = 2.7
        _WaveSpeed("水流速度", Range(-3, 3)) = 0.14
        _WaveDistortion("水流扭曲", Range(0, 4)) = 1.9
        _NormalStrength("表面起伏", Range(0, 0.8)) = 0.18
        _PixelDensity("表面采样密度", Range(1, 128)) = 48
        _FlowDirection("流动方向", Vector) = (0.55, 0.18, 0, 0)
        _RippleColor("浪脊颜色", Color) = (0.18, 0.32, 0.12, 0.75)
        _RippleStrength("浪脊强度", Range(0, 1)) = 0.09
        _RippleScale("浪纹尺度", Range(0.25, 6)) = 1.3
        _RippleWidth("浪脊宽度", Range(0.04, 0.45)) = 0.18
        _RippleShadowStrength("浪背暗部", Range(0, 0.5)) = 0.2

        [Header(Reflection And Foam)]
        _ReflectionColor("镜面反射颜色", Color) = (0.1, 0.22, 0.07, 0.82)
        _ReflectionStrength("镜面反射强度", Range(0, 1)) = 0.14
        _ReflectionSmoothness("镜面反射平滑度", Range(0, 1)) = 0.32
        _ReflectionDirection("镜面环境方向", Vector) = (-0.3, 0.12, 0.88, 0)
        _SpecularColor("太阳高光", Color) = (0.36, 0.45, 0.22, 1)
        _SpecularStrength("太阳高光强度", Range(0, 1)) = 0.12
        _SpecularPower("太阳高光锐度", Range(4, 96)) = 20
        _SunDirection("太阳方向", Vector) = (0.28, 0.42, 0.86, 0)
        _CausticColor("焦散颜色", Color) = (0.1, 0.25, 0.06, 1)
        _CausticStrength("焦散强度", Range(0, 1)) = 0.015
        _FoamColor("浮沫颜色", Color) = (0.2, 0.32, 0.1, 1)
        _WhitecapStrength("表面浮沫", Range(0, 1)) = 0.1

        [Header(Moon Reflection)]
        _MoonReflectionColor("月光倒影颜色", Color) = (0.48, 0.62, 0.4, 1)
        _MoonReflectionStrength("月光倒影强度", Range(0, 8)) = 2.2
        _MoonReflectionPosition("月光倒影屏幕位置", Vector) = (0.68, 0.62, 0, 0)
        _MoonDiscRadius("月面倒影半径", Range(0.01, 0.2)) = 0.045
        _MoonTrailLength("月光带长度", Range(0.01, 0.7)) = 0.26
        _MoonTrailWidth("月光带宽度", Range(0.005, 0.2)) = 0.05

        [Header(Shore)]
        _EdgeColor("岸线暗部", Color) = (0.003, 0.007, 0.002, 1)
        _EdgeWidth("岸线宽度", Range(0.03, 0.45)) = 0.24
        _EdgeStrength("岸线暗部强度", Range(0, 1)) = 0.85
        _CornerStrength("转角叠加强度", Range(0, 1)) = 0.2
        _ShoreColor("岸线亮部", Color) = (0.16, 0.28, 0.08, 1)
        _ShoreStrength("岸线亮部强度", Range(0, 1)) = 0.08
        _ShoreFoamStrength("岸边浮沫强度", Range(0, 1)) = 0.3
        _FoamSpeed("岸边浮沫速度", Range(0, 3)) = 0.16

        [HideInInspector] _Color("Tint", Color) = (1,1,1,0.92)
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
