Shader "Game/Effects/ShaderSmoke"
{
    Properties
    {
        _DarkColor("Fresh Smoke", Color) = (0.15, 0.14, 0.13, 0.65)
        _LightColor("Dispersed Smoke", Color) = (0.42, 0.41, 0.38, 0.3)
        _Lifetime("Smoke Rise Seconds", Range(0.5, 10)) = 3.8
        _Turbulence("Smoke Turbulence", Range(0, 2)) = 0.8
        _Wind("Sideways Drift", Range(-1, 1)) = 0.15
        _PixelsPerUnit("Pixels Per World Unit", Range(8, 128)) = 32
        _AnimationFPS("Animation Frames Per Second", Range(1, 30)) = 12
        [PerRendererData] _SmokeInstance("Width Height Density Phase", Vector) = (0.65, 1.25, 1, 0)
        [PerRendererData] _SmokeStartTime("Emission Start Time", Float) = 0
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

        CBUFFER_START(UnityPerMaterial)
            half4 _DarkColor, _LightColor;
            float _Lifetime, _Turbulence, _Wind, _PixelsPerUnit, _AnimationFPS;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(SmokeInstances)
            UNITY_DEFINE_INSTANCED_PROP(float4, _SmokeInstance)
            UNITY_DEFINE_INSTANCED_PROP(float, _SmokeStartTime)
        UNITY_INSTANCING_BUFFER_END(SmokeInstances)

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
            float4 instance = UNITY_ACCESS_INSTANCED_PROP(SmokeInstances, _SmokeInstance);
            input.positionOS.y += 0.5;
            input.positionOS.xy *= instance.xy;
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = input.uv;
            float2 worldSize = instance.xy * float2(
                length(TransformObjectToWorldDir(float3(1, 0, 0), false)),
                length(TransformObjectToWorldDir(float3(0, 1, 0), false)));
            output.pixelGrid = max(floor(worldSize * _PixelsPerUnit + 0.5), 1.0);
            return output;
        }

        float Hash21(float2 p)
        {
            float3 h = frac(float3(p.xyx) * 0.1031);
            h += dot(h, h.yzx + 33.33);
            return frac((h.x + h.y) * h.z);
        }

        float Noise(float2 p)
        {
            float2 cell = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash21(cell), Hash21(cell + float2(1, 0)), f.x),
                lerp(Hash21(cell + float2(0, 1)), Hash21(cell + 1.0), f.x), f.y);
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float4 instance = UNITY_ACCESS_INSTANCED_PROP(SmokeInstances, _SmokeInstance);
            float startTime = UNITY_ACCESS_INSTANCED_PROP(SmokeInstances, _SmokeStartTime);
            float fps = max(_AnimationFPS, 1.0);
            float elapsed = floor(max(_Time.y - startTime, 0.0) * fps) / fps;
            float lifetime = max(_Lifetime, 0.1);
            float2 uv = (floor(input.uv * input.pixelGrid) + 0.5) / input.pixelGrid;
            float aspect = instance.x / max(instance.y, 0.01);
            float2 p = float2((uv.x - 0.5) * aspect, uv.y);
            float flow = Noise(float2(p.x * 19.0 + instance.w, p.y * 12.0 - elapsed * 0.65));
            half4 smoke = 0;
            // 八个错相烟团只在 GPU 内出生、上升、扩散和淡出，点燃前不预填整条烟柱。
            [unroll] for (int index = 0; index < 8; index++)
            {
                float clock = elapsed / lifetime + index * 0.125 + frac(instance.w);
                float age = frac(clock);
                float seed = Hash21(float2(index + instance.w, floor(clock)));
                float sway = sin(age * 5.5 + seed * 6.283) * age * 0.055 * _Turbulence;
                float2 center = float2(sway + _Wind * age * age * 0.13 + (seed - 0.5) * 0.04,
                    0.025 + age * 0.88);
                float radius = (0.045 + age * 0.12) * lerp(0.85, 1.15, seed);
                float edge = length((p - center) * float2(1.0, 0.85))
                    + (flow - 0.5) * 0.035 * _Turbulence;
                float alpha = (1.0 - smoothstep(radius * 0.45, radius, edge))
                    * smoothstep(0.0, 0.1, age) * (1.0 - smoothstep(0.55, 1.0, age))
                    * step(age * lifetime, elapsed);
                half4 tint = lerp(_DarkColor, _LightColor, age);
                alpha = saturate(alpha * tint.a * instance.z * 0.65);
                smoke.rgb += (1.0 - smoke.a) * tint.rgb * alpha;
                smoke.a += (1.0 - smoke.a) * alpha;
            }
            float border = smoothstep(0.0, 0.045, uv.y)
                * (1.0 - smoothstep(0.86, 1.0, uv.y))
                * smoothstep(0.0, 0.05, uv.x) * (1.0 - smoothstep(0.95, 1.0, uv.x));
            return smoke * border;
        }
        ENDHLSL

        Pass
        {
            Name "Smoke2D"
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
            Name "SmokeForward"
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
