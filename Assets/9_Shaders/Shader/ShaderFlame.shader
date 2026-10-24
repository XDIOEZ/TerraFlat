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

        // 三支不同高度的火舌相连，边缘扰动沿纵向上升而非整团缩放。
        float Tongue(float2 p, float offset, float height, float width, float bend, float t)
        {
            float y = saturate(p.y / max(height, 0.01));
            float center = offset + (bend + _Wind * 0.13) * y * y
                + sin(y * 8.0 - t * 2.2) * 0.038 * y;
            float radius = width * pow(saturate(1.0 - y), 0.72) * smoothstep(-0.04, 0.13, p.y);
            return min(radius - abs(p.x - center), (height - p.y) * 0.5);
        }

        float Ember(float2 cell, float2 pixelGrid, float t, float seed)
        {
            float life = frac(t * 0.24 + seed);
            float2 center = float2(sin(seed * 31.0 + life * 5.0) * (0.06 + life * 0.12),
                0.24 + life * 0.72);
            center.x += _Wind * life * 0.12;
            float2 emberCell = floor(float2(center.x + 0.5, center.y) * pixelGrid);
            float2 delta = abs(cell - emberCell);
            return (1.0 - step(0.5, max(delta.x, delta.y)))
                * step(0.05, life) * (1.0 - step(0.25 + _Embers * 0.55, life));
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float4 instance = UNITY_ACCESS_INSTANCED_PROP(FlameInstances, _FlameInstance);
            float2 cell = min(floor(input.uv * input.pixelGrid), input.pixelGrid - 1.0);
            float2 uv = (cell + 0.5) / input.pixelGrid;
            float2 p = float2(uv.x - 0.5, uv.y);
            float frameRate = max(_AnimationFPS, 1.0);
            float t = floor(_Time.y * frameRate) / frameRate * _Speed + instance.w;
            float flow = Noise(float2(p.x * 6.0 + instance.w, p.y * 4.5 - t * 1.4)) - 0.5;
            float detail = Noise(float2(p.x * 15.0 + 7.3, p.y * 11.0 - t * 2.3)) - 0.5;
            p.x += flow * _Turbulence * 0.045 * p.y;
            float mainHeight = 0.80 + sin(t * 1.7) * 0.065;
            float field = max(Tongue(p, 0.0, mainHeight, 0.255, 0.035, t),
                max(Tongue(p, -0.155, 0.56 + sin(t * 2.3 + 1.5) * 0.08, 0.12, -0.045, t + 2.0),
                    Tongue(p, 0.15, 0.60 + sin(t * 1.9 + 4.0) * 0.075, 0.125, 0.06, t + 4.0)));
            field += (flow * 0.055 + detail * 0.025) * _Turbulence * (0.3 + p.y);
            // 轮廓与四色火舌共用硬像素格，不再叠加柔边、连续渐变或模糊光晕。
            float body = step(0.0, field);
            float orange = step(0.025, field);
            float gold = step(0.08, field - p.y * 0.035);
            float coreField = Tongue(p, 0.0, 0.40 + flow * 0.07, 0.105, -0.015, t + 0.7);
            float core = step(0.0, coreField + detail * 0.014) * body;
            half3 color = lerp(_EdgeColor.rgb, _BodyColor.rgb, orange);
            color = lerp(color, _InnerColor.rgb, gold);
            color = lerp(color, _CoreColor.rgb, core);

            float sparks = max(Ember(cell, input.pixelGrid, t, 0.19),
                Ember(cell, input.pixelGrid, t, 0.67)) * step(0.001, _Embers);
            float opacity = saturate(instance.z);
            float alpha = max(body, sparks) * opacity;
            half3 rgb = color * body + _InnerColor.rgb * sparks * (1.0 - body);
            return half4(rgb * opacity * _Brightness * max(instance.z, 1.0), alpha);
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
