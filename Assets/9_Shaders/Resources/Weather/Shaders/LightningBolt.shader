Shader "Game/Weather/LightningBolt"
{
    Properties
    {
        _Progress("Lifetime Progress", Range(0, 1)) = 0
        _Strength("Intensity", Range(0, 1)) = 1
        _Seed("Strike Seed", Float) = 1
        _BoltSize("Width Height Core Glow", Vector) = (5.5, 24, 0.035, 0.16)
        _Mode("Bolt Or Ground Impact", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "DisableBatching" = "True"
        }
        Blend One One
        Cull Off
        ZWrite Off
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
            UNITY_VERTEX_OUTPUT_STEREO
        };

        CBUFFER_START(UnityPerMaterial)
            float4 _BoltSize;
            float _Progress;
            float _Strength;
            float _Seed;
            float _Mode;
        CBUFFER_END

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = input.uv;
            return output;
        }

        float Hash(float value)
        {
            return frac(sin(value * 127.1 + _Seed * 17.73) * 43758.5453);
        }

        // 采样坐标避开 HLSL 保留关键字，防止图形后端解析失败。
        float SegmentDistance(float2 samplePosition, float2 start, float2 end)
        {
            float2 edge = end - start;
            float projection = saturate(dot(samplePosition - start, edge) / max(dot(edge, edge), 0.00001));
            return length(samplePosition - start - edge * projection);
        }

        float MainX(float height)
        {
            const float segments = 16.0;
            float scaledHeight = saturate(height) * segments;
            float index = min(floor(scaledHeight), segments - 1.0);
            float lower = (Hash(index + 3.0) * 2.0 - 1.0) * pow(index / segments, 0.6);
            float upper = (Hash(index + 4.0) * 2.0 - 1.0) * pow((index + 1.0) / segments, 0.6);
            return lerp(lower, upper, scaledHeight - index) * _BoltSize.x * 0.18;
        }

        float MainDistance(float2 samplePosition, float height)
        {
            float index = min(floor(saturate(height) * 16.0), 15.0);
            float lower = index / 16.0;
            float upper = (index + 1.0) / 16.0;
            return SegmentDistance(samplePosition, float2(MainX(lower), lower * _BoltSize.y),
                float2(MainX(upper), upper * _BoltSize.y));
        }

        float BranchX(float progress, float rootHeight, float direction, float key)
        {
            float index = min(floor(saturate(progress) * 5.0), 4.0);
            float lower = index / 5.0;
            float upper = (index + 1.0) / 5.0;
            float lowerNoise = (Hash(index + key) * 2.0 - 1.0) * sin(lower * PI);
            float upperNoise = (Hash(index + key + 1.0) * 2.0 - 1.0) * sin(upper * PI);
            float noise = lerp(lowerNoise, upperNoise, saturate(progress) * 5.0 - index);
            return MainX(rootHeight) + direction * _BoltSize.x * progress * 0.36 + noise * _BoltSize.x * 0.07;
        }

        float BranchDistance(float2 samplePosition, float height, float rootHeight, float endHeight, float direction, float key)
        {
            if (height > rootHeight || height < endHeight) return 1000.0;
            float progress = (rootHeight - height) / max(rootHeight - endHeight, 0.001);
            float index = min(floor(progress * 5.0), 4.0);
            float lower = index / 5.0;
            float upper = (index + 1.0) / 5.0;
            float2 start = float2(BranchX(lower, rootHeight, direction, key),
                lerp(rootHeight, endHeight, lower) * _BoltSize.y);
            float2 end = float2(BranchX(upper, rootHeight, direction, key),
                lerp(rootHeight, endHeight, upper) * _BoltSize.y);
            return SegmentDistance(samplePosition, start, end);
        }

        float3 BoltColor(float2 uv)
        {
            float2 samplePosition = float2((uv.x - 0.5) * _BoltSize.x, uv.y * _BoltSize.y);
            float mainDistance = MainDistance(samplePosition, uv.y);
            float branchDistance = min(
                BranchDistance(samplePosition, uv.y, 0.82, 0.43, -1.0, 30.0),
                min(BranchDistance(samplePosition, uv.y, 0.63, 0.24, 1.0, 50.0),
                    BranchDistance(samplePosition, uv.y, 0.4, 0.12, -1.0, 70.0)));
            float antiAlias = max(fwidth(mainDistance), 0.003);
            float core = 1.0 - smoothstep(_BoltSize.z, _BoltSize.z + antiAlias, mainDistance);
            float branchCore = 1.0 - smoothstep(_BoltSize.z * 0.55,
                _BoltSize.z * 0.55 + antiAlias, branchDistance);
            float glow = exp(-mainDistance / max(_BoltSize.w, 0.001));
            float branchGlow = exp(-branchDistance / max(_BoltSize.w * 0.7, 0.001));
            // 闪电从上缘快速落到真实落点，白芯和蓝紫边光由同一条随机折线计算。
            float reveal = smoothstep(1.0 - saturate(_Progress / 0.12) - 0.025,
                1.0 - saturate(_Progress / 0.12), uv.y);
            float boundary = smoothstep(0.0, 0.01, uv.x) * (1.0 - smoothstep(0.99, 1.0, uv.x));
            float pulse = 0.55 + 0.45 * pow(abs(sin(_Progress * 22.0 + 0.8)), 6.0);
            float envelope = pow(saturate(1.0 - _Progress), 1.5) * pulse;
            float3 color = float3(1.0, 0.97, 1.0) * (core * 4.0 + branchCore * 2.2)
                + float3(0.27, 0.38, 1.0) * (glow * 1.45 + branchGlow * 0.9);
            return color * reveal * boundary * envelope;
        }

        float3 ImpactColor(float2 uv)
        {
            float2 samplePosition = (uv - 0.5) * 2.0;
            float radius = length(samplePosition);
            float angle = atan2(samplePosition.y, samplePosition.x + 0.00001);
            float wave = sin(radius * 31.0 + _Seed) * 0.13 + sin(radius * 13.0 + _Seed * 0.7) * 0.1;
            float arcDistance = abs(sin((angle - wave - _Seed) * 3.0)) * radius;
            float arcs = 1.0 - smoothstep(0.009, 0.009 + max(fwidth(arcDistance), 0.003), arcDistance);
            arcs *= smoothstep(0.08, 0.2, radius) * (1.0 - smoothstep(0.55, 0.92, radius));
            float spot = exp(-radius * radius * 30.0);
            float halo = exp(-radius * radius * 6.0);
            float ring = exp(-abs(radius - (0.16 + _Progress * 0.55)) * 55.0);
            float envelope = pow(saturate(1.0 - _Progress), 2.0);
            float arrive = smoothstep(0.015, 0.08, _Progress);
            float boundary = 1.0 - smoothstep(0.83, 1.0, radius);
            return (float3(0.94, 0.97, 1.0) * (spot * 3.0 + arcs * 1.8)
                + float3(0.34, 0.26, 1.0) * (halo * 0.95 + ring * 0.65)) * envelope * arrive * boundary;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 color;
            [branch] if (_Mode < 0.5) color = BoltColor(input.uv);
            else color = ImpactColor(input.uv);
            return half4(color * saturate(_Strength), 0.0);
        }
        ENDHLSL

        Pass
        {
            Name "Lightning2D"
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "LightningUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    Fallback Off
}
