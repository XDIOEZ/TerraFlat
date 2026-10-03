Shader "FlatWorld/2D/Chunk Depth Sprite Lit"
{
    Properties
    {
        _MainTex("Sprite", 2D) = "white" {}
        _AlphaTex("External Alpha", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        _DissolveTex("Dissolve Noise", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _RendererColor("Renderer Tint", Color) = (1,1,1,1)
        _EnableExternalAlpha("External Alpha", Float) = 0
        _DepthUnlit("Unlit", Float) = 0
        _DepthEmissive("Emissive", Float) = 0
        _DepthSrcBlend("Source Blend", Float) = 5
        _DepthDstBlend("Destination Blend", Float) = 10
        _DepthSrcBlendAlpha("Source Alpha Blend", Float) = 1
        _DepthDstBlendAlpha("Destination Alpha Blend", Float) = 10
        _DepthBlendOp("Blend Operation", Float) = 0
        _DepthDissolveEnabled("Dissolve Enabled", Float) = 0
        _Dissolve("Dissolve", Float) = 0
        _SnowCoverage("Snow", Float) = 0
        _HitFlash("Hit Flash", Float) = 0
        _HitFlashColor("Hit Color", Color) = (1,.08,.08,1)
        _ActorTint("Status Color", Color) = (1,1,1,1)
        _ActorTintStrength("Status Strength", Float) = 0
        _ActorTintPulseSpeed("Status Speed", Float) = 1.2
        _ActorTintPulseAmplitude("Status Pulse", Float) = .08
        _GrassSwayEnabled("Sway", Float) = 0
        _GrassSwayAmplitude("Sway Amplitude", Float) = 0
        _GrassSwaySpeed("Sway Speed", Float) = 1.2
        _GrassSwayFrequency("Sway Frequency", Float) = 1.5
        _GrassBendPower("Bend Power", Float) = 1.8
        _GrassSecondaryStrength("Secondary Sway", Float) = .35
        _GrassSpriteHeight("Sprite Height", Float) = .5
        _GrassBendStart("Bend Start", Float) = 0
        _GrassTileAnchor("Tile Anchor", Float) = .5
        _GrassUseObjectRoot("Object Root", Float) = 1
        _GrassDirection("Wind Direction", Vector) = (1,0,0,0)
    }
    SubShader
    {
        // 已经手动合并整行；禁止引擎再次预变换顶点，避免破坏随顶点携带的局部仿射基。
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Blend [_DepthSrcBlend] [_DepthDstBlend], [_DepthSrcBlendAlpha] [_DepthDstBlendAlpha]
        BlendOp [_DepthBlendOp]
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_AlphaTex); SAMPLER(sampler_AlphaTex);
        TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_DissolveTex); SAMPLER(sampler_DissolveTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_TexelSize;
            float4 _Color, _RendererColor, _HitFlashColor, _ActorTint, _GrassDirection;
            float _EnableExternalAlpha, _DepthUnlit, _DepthDissolveEnabled, _Dissolve;
            float _DepthEmissive, _DepthSrcBlend, _DepthDstBlend, _DepthSrcBlendAlpha, _DepthDstBlendAlpha, _DepthBlendOp;
            float _SnowCoverage, _HitFlash, _ActorTintStrength, _ActorTintPulseSpeed, _ActorTintPulseAmplitude;
            float _GrassSwayEnabled, _GrassSwayAmplitude, _GrassSwaySpeed, _GrassSwayFrequency;
            float _GrassBendPower, _GrassSecondaryStrength, _GrassSpriteHeight, _GrassBendStart;
            float _GrassTileAnchor, _GrassUseObjectRoot;
        CBUFFER_END
        #define FLATWORLD_VEGETATION_SWAY_MATERIAL_DECLARED
        #include "../../Shader/VegetationSway.hlsl"
        #include "../../Shader/InteractionOutlineCommon.hlsl"
        #include "../../Shader/ConveyorSurface.hlsl"
        float _PlayerOcclusionEnabled, _PlayerOcclusionRadius, _PlayerOcclusionFeather;
        float _PlayerOcclusionAlpha, _PlayerOcclusionVerticalPadding;
        float4 _PlayerOcclusionCenter;

        struct Attributes
        {
            float3 positionOS : POSITION;
            half4 tint : COLOR;
            float2 uv : TEXCOORD0;
            float4 localClip : TEXCOORD1;
            float4 basis : TEXCOORD2;
            float4 origin : TEXCOORD3;
            float4 animation : TEXCOORD4;
            float4 region : TEXCOORD5;
            float4 effects : TEXCOORD6;
            float4 state : TEXCOORD7;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            half4 tint : COLOR;
            float4 uv : TEXCOORD0;
            float2 lightingUV : TEXCOORD1;
            float4 localClip : TEXCOORD2;
            float4 animation : TEXCOORD3;
            float4 region : TEXCOORD4;
            float4 effects : TEXCOORD5;
            float3 worldRoot : TEXCOORD6;
            float4 state : TEXCOORD7;
        };

        // 原始局部顶点和仿射基写在顶点流里，不依赖整行 Mesh 的对象原点计算机械运动。
        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            float2 local = input.localClip.xy;
            float2 tangent = input.basis.xy;
            float phase = input.animation.z + _Time.y * input.animation.y;
            if (input.animation.x > .5 && input.animation.x < 1.5)
            {
                float s, c; sincos(phase, s, c);
                local = float2(local.x * c - local.y * s, local.x * s + local.y * c);
                tangent = input.basis.xy * c + input.basis.zw * s;
            }
            else if (input.animation.x > 2.5 && input.animation.x < 3.5)
                local.y -= (1 - cos(phase)) * .5 * input.animation.w;
            else if (input.animation.x > 3.5 && input.animation.x < 4.5)
                local.y *= 1 - (1 - cos(phase)) * .5 * saturate(input.animation.w);
            float3 positionOS = float3(input.origin.xy + input.basis.xy * local.x + input.basis.zw * local.y, input.origin.z);
            float3 positionWS = TransformObjectToWorld(positionOS);
            if (_GrassSwayEnabled > .5 && _GrassSwayAmplitude > .0001)
            {
                float2 direction = _GrassDirection.xy / max(.001, length(_GrassDirection.xy));
                float wavePhase = sin(dot(positionWS.xy, float2(12.9898,78.233))) * 1.7;
                float t = _Time.y * _GrassSwaySpeed;
                float wave = sin(t + wavePhase + dot(positionWS.xy, direction) * _GrassSwayFrequency);
                wave += sin(t * .63 + wavePhase * 1.71 + positionWS.y * _GrassSwayFrequency * .73) * _GrassSecondaryStrength;
                float sway = wave * _GrassSwayAmplitude * ResolveVegetationVisualWindStrength(saturate(_GlobalWindStrength)) *
                    GrassBendWeight(float3(input.localClip.xy, 0));
                positionOS.xy += (input.basis.xy * direction.x + input.basis.zw * direction.y) * sway;
                positionWS = TransformObjectToWorld(positionOS);
            }
            output.positionCS = TransformWorldToHClip(positionWS);
            output.worldRoot = float3(positionWS.xy, TransformObjectToWorld(float3(0,0,0)).y);
            output.tint = input.tint * _Color * _RendererColor;
            tangent = TransformObjectToWorldDir(float3(tangent, 0)).xy;
            output.uv = float4(input.uv, tangent / max(.00001, length(tangent)));
            output.lightingUV = ComputeScreenPos(output.positionCS / output.positionCS.w).xy;
            output.localClip = input.localClip;
            output.localClip.x = input.basis.x * input.basis.w - input.basis.y * input.basis.z < 0 ? -1 : 1;
            output.animation = input.animation;
            output.region = input.region;
            output.effects = input.effects;
            output.state = input.state;
            return output;
        }

        float2 ResolveUv(Varyings input)
        {
            float2 uv = input.uv.xy;
            if (input.animation.x > 1.5 && input.animation.x < 2.5)
            {
                float width = max(.000001, input.region.z - input.region.x);
                float u = (uv.x - input.region.x) / width;
                float phase = input.animation.z + _Time.y * input.animation.y;
                float inset = min(width * .49, _MainTex_TexelSize.x * .5);
                uv.x = input.region.x + inset + frac(u + phase / 6.28318530718) * (width - inset * 2);
            }
            else if (input.animation.x > 4.5 && input.animation.x < 5.5)
            {
                // 世界火焰：首帧 Sprite 提供几何和 UV 宽度，GPU 只沿横向图集切帧。
                float frameCount = max(1, floor(input.animation.w + .5));
                float frame = fmod(floor(_Time.y * input.animation.y + input.animation.z), frameCount);
                uv.x += frame * max(.000001, input.region.z - input.region.x);
            }
            if (input.animation.x > 5.5 && input.animation.x < 7.5)
                uv = ConveyorSurfaceUv(uv, input.region, input.animation,
                    float4(input.effects.xy, input.localClip.z, input.animation.w), _Time.y);
            return uv;
        }
        half4 Surface(Varyings input, float2 uv)
        {
            // 屏幕导数在裁剪前求出，贴图分辨率和对象缩放不再改变描边宽度。
            float2 pixelScale = InteractionOutlinePixelScale();
            float2 pixelX = ddx(input.uv.xy) * pixelScale.x;
            float2 pixelY = ddy(input.uv.xy) * pixelScale.y;
            if (input.animation.x > 1.5 && input.animation.x < 2.5)
            {
                float width = max(.000001, input.region.z - input.region.x);
                float inset = min(width * .49, _MainTex_TexelSize.x * .5);
                float uvScale = (width - inset * 2) / width;
                // 传送带循环接缝只改变采样位置，不能把 frac 跳变当成一个屏幕像素的宽度。
                pixelX.x *= uvScale;
                pixelY.x *= uvScale;
            }
            if (input.localClip.w > .5) clip(input.localClip.y - input.localClip.z);
            half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            if (_EnableExternalAlpha > .5) main.a = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, uv).r;
            main *= input.tint;
            float bodyV = saturate((input.localClip.y - input.effects.x) / max(.00001, input.effects.y - input.effects.x));
            float snowAmount = input.state.w > .5 ? input.state.y : _SnowCoverage;
            float snow = smoothstep(.8 - snowAmount * .3, .9 - snowAmount * .3, bodyV) * saturate(snowAmount);
            main.rgb = lerp(main.rgb, half3(.88,.94,1), snow);
            float pulse = 1 + sin(_Time.y * max(0, _ActorTintPulseSpeed)) * saturate(_ActorTintPulseAmplitude);
            main.rgb = lerp(main.rgb, _ActorTint.rgb, saturate(_ActorTintStrength * pulse));
            main.rgb = lerp(main.rgb, _HitFlashColor.rgb, saturate(input.state.w > .5 ? input.state.x : _HitFlash));
            if (input.effects.z > .5 && _PlayerOcclusionEnabled > .5)
            {
                float radius = max(.0001, _PlayerOcclusionRadius);
                float feather = clamp(_PlayerOcclusionFeather, .0001, radius);
                float window = 1 - smoothstep(radius - feather, radius, distance(input.worldRoot.xy, _PlayerOcclusionCenter.xy));
                window *= step(input.worldRoot.z + _PlayerOcclusionVerticalPadding, _PlayerOcclusionCenter.z);
                main.a *= lerp(1, saturate(_PlayerOcclusionAlpha), window);
            }
            if (_DepthDissolveEnabled > .5) clip(SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, uv).r - (input.state.w > .5 ? input.state.z : _Dissolve));
            if (input.effects.w > .5)
            {
                main.rgb = lerp(main.rgb, 1, InteractionOutlineMask(uv, input.region, pixelX, pixelY));
            }
            clip(main.a - .0001);
            if (_DepthEmissive > .5) main.rgb *= main.a;
            return main;
        }
        ENDHLSL

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = ResolveUv(input);
                half4 main = Surface(input, uv);
                if (_DepthUnlit > .5 || (input.animation.x > 4.5 && input.animation.x < 5.5)) return main;
                SurfaceData2D surfaceData; InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv), surfaceData);
                InitializeInputData(uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }
        Pass
        {
            Name "NormalsRendering"
            Tags { "LightMode"="NormalsRendering" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragNormals
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"
            half4 FragNormals(Varyings input) : SV_Target
            {
                clip(.5 - _DepthEmissive);
                if (input.animation.x > 4.5 && input.animation.x < 5.5) clip(-1);
                float2 uv = ResolveUv(input);
                half4 main = Surface(input, uv);
                half3 normal = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv));
                half3 tangent = half3(input.uv.zw, 0);
                half3 bitangent = half3(-tangent.y, tangent.x, 0) * input.localClip.x;
                return NormalsRenderingShared(main, normal, tangent, bitangent, half3(0,0,-1));
            }
            ENDHLSL
        }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragForward
            half4 FragForward(Varyings input) : SV_Target { return Surface(input, ResolveUv(input)); }
            ENDHLSL
        }
    }
}
