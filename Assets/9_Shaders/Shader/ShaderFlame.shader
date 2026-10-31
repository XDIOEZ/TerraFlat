Shader "Game/Effects/ShaderFlame"
{
    Properties
    {
        [HDR] _EdgeColor("Outer Flame", Color) = (1, 0.075, 0.008, 1)
        [HDR] _BodyColor("Orange Flame", Color) = (1, 0.32, 0.018, 1)
        [HDR] _InnerColor("Golden Flame", Color) = (1, 0.72, 0.08, 1)
        [HDR] _CoreColor("Hot Core", Color) = (1, 0.85, 0.26, 1)
        _Speed("Rise Speed", Range(0, 5)) = 1.4
        _Turbulence("Turbulence", Range(0, 2)) = 0.85
        _Wind("Sideways Bend", Range(-1, 1)) = 0
        _Brightness("Brightness", Range(0, 4)) = 1
        _Embers("GPU Embers", Range(0, 1)) = 0.65
        _PixelsPerUnit("Pixels Per World Unit", Range(8, 128)) = 32
        _AnimationFPS("Animation Frames Per Second", Range(1, 30)) = 12
        [PerRendererData] _FlameInstance("Width Height Intensity Phase", Vector) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Blend One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "ShaderFlameCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _EdgeColor, _BodyColor, _InnerColor, _CoreColor;
            float _Speed, _Turbulence, _Wind, _Brightness, _Embers, _PixelsPerUnit, _AnimationFPS;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(FlameInstances)
            UNITY_DEFINE_INSTANCED_PROP(float4, _FlameInstance)
        UNITY_INSTANCING_BUFFER_END(FlameInstances)

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float2 pixelGrid : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float4 instance = UNITY_ACCESS_INSTANCED_PROP(FlameInstances, _FlameInstance);
            input.positionOS.y += 0.5;
            input.positionOS.xy *= instance.xy;
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = input.uv;
            // 按世界尺寸生成方形像素格，宽高变化与父级缩放不会把火焰像素拉成长条。
            float2 worldSize = instance.xy * float2(
                length(TransformObjectToWorldDir(float3(1, 0, 0), false)),
                length(TransformObjectToWorldDir(float3(0, 1, 0), false)));
            output.pixelGrid = max(floor(worldSize * _PixelsPerUnit + 0.5), 1.0);
            return output;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float4 instance = UNITY_ACCESS_INSTANCED_PROP(FlameInstances, _FlameInstance);
            float frameRate = max(_AnimationFPS, 1.0);
            float t = floor(_Time.y * frameRate) / frameRate * _Speed + instance.w;
            return FlatWorldEvaluateProceduralFlame(input.uv, input.pixelGrid, t, instance.w, instance.z,
                _Wind, _Turbulence, _Embers, _Brightness,
                _EdgeColor.rgb, _BodyColor.rgb, _InnerColor.rgb, _CoreColor.rgb);
        }
        ENDHLSL

        Pass
        {
            Name "Flame2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "FlameForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback Off
}
