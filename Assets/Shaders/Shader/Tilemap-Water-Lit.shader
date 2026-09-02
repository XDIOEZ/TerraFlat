Shader "FlatWorld/2D/Tilemap Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}

        [Header(Ocean Surface)]
        _DeepColor("深海颜色", Color) = (0.015, 0.15, 0.3, 1)
        _ShallowColor("浅海颜色", Color) = (0.04, 0.55, 0.62, 1)
        _SurfaceTint("海水染色强度", Range(0, 1)) = 0.45
        _DepthDarkening("深水压暗强度", Range(0, 1)) = 0.72
        _SwellScale("涌浪尺度", Range(0.05, 4)) = 0.68
        _DetailScale("细浪尺度", Range(0.5, 12)) = 3.8
        _WaveSpeed("海流速度", Range(-3, 3)) = 0.42
        _WaveDistortion("海流扭曲", Range(0, 4)) = 1.4
        _NormalStrength("表面起伏", Range(0, 0.8)) = 0.32
        _PixelDensity("表面采样密度", Range(1, 128)) = 64
        _FlowDirection("流动方向", Vector) = (1, 0.35, 0, 0)
        _RippleColor("浪脊颜色", Color) = (0.38, 0.78, 0.88, 1)
        _RippleStrength("浪脊强度", Range(0, 1)) = 0.22
        _RippleScale("浪纹尺度", Range(0.25, 6)) = 1.8
        _RippleWidth("浪脊宽度", Range(0.04, 0.45)) = 0.1
        _RippleShadowStrength("浪背暗部", Range(0, 0.5)) = 0.08

        [Header(Reflection And Foam)]
        _ReflectionColor("镜面反射颜色", Color) = (0.24, 0.72, 0.9, 1)
        _ReflectionStrength("镜面反射强度", Range(0, 1)) = 0.42
        _ReflectionSmoothness("镜面反射平滑度", Range(0, 1)) = 0.72
        _ReflectionDirection("镜面环境方向", Vector) = (-0.35, 0.18, 0.92, 0)
        _SpecularColor("太阳高光", Color) = (0.9, 0.98, 1, 1)
        _SpecularStrength("太阳高光强度", Range(0, 1)) = 0.3
        _SpecularPower("太阳高光锐度", Range(4, 96)) = 48
        _SunDirection("太阳方向", Vector) = (0.28, 0.42, 0.86, 0)
        _CausticColor("焦散颜色", Color) = (0.28, 0.86, 0.92, 1)
        _CausticStrength("焦散强度", Range(0, 1)) = 0.08
        _FoamColor("泡沫颜色", Color) = (0.76, 0.96, 1, 1)
        _WhitecapStrength("浪峰白沫", Range(0, 1)) = 0.24

        [Header(Moon Reflection)]
        _MoonReflectionColor("月光倒影颜色", Color) = (0.72, 0.86, 1, 1)
        _MoonReflectionStrength("月光倒影强度", Range(0, 8)) = 4.2
        _MoonReflectionPosition("月光倒影屏幕位置", Vector) = (0.68, 0.62, 0, 0)
        _MoonDiscRadius("月面倒影半径", Range(0.01, 0.2)) = 0.055
        _MoonTrailLength("月光带长度", Range(0.01, 0.7)) = 0.34
        _MoonTrailWidth("月光带宽度", Range(0.005, 0.2)) = 0.065

        [Header(Shore)]
        _EdgeColor("岸线暗部", Color) = (0.035, 0.022, 0.015, 1)
        _EdgeWidth("岸线宽度", Range(0.03, 0.45)) = 0.2
        _EdgeStrength("岸线暗部强度", Range(0, 1)) = 0.72
        _CornerStrength("转角叠加强度", Range(0, 1)) = 0.18
        _ShoreColor("岸线亮部", Color) = (0.48, 0.85, 0.92, 1)
        _ShoreStrength("岸线亮部强度", Range(0, 1)) = 0.16
        _ShoreFoamStrength("岸边泡沫强度", Range(0, 1)) = 0.68
        _FoamSpeed("岸边泡沫速度", Range(0, 3)) = 0.72

        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);

        half4 _Color;
        half4 _RendererColor;
        half4 _DeepColor;
        half4 _ShallowColor;
        half4 _ReflectionColor;
        half4 _SpecularColor;
        half4 _CausticColor;
        half4 _FoamColor;
        half4 _RippleColor;
        half4 _MoonReflectionColor;
        half4 _EdgeColor;
        half4 _ShoreColor;
        float4 _FlowDirection;
        float4 _ReflectionDirection;
        float4 _SunDirection;
        float4 _MoonReflectionPosition;
        half _SurfaceTint;
        half _DepthDarkening;
        float _SwellScale;
        float _DetailScale;
        float _WaveSpeed;
        float _PixelDensity;
        half _WaveDistortion;
        half _NormalStrength;
        half _RippleStrength;
        float _RippleScale;
        half _RippleWidth;
        half _RippleShadowStrength;
        half _ReflectionStrength;
        half _ReflectionSmoothness;
        half _SpecularStrength;
        float _SpecularPower;
        half _CausticStrength;
        half _WhitecapStrength;
        half _MoonReflectionStrength;
        float _MoonDiscRadius;
        float _MoonTrailLength;
        float _MoonTrailWidth;
        half _GlobalMoonlightIntensity;
        half _EdgeWidth;
        half _EdgeStrength;
        half _CornerStrength;
        half _ShoreStrength;
        half _ShoreFoamStrength;
        float _FoamSpeed;

        /// <summary>将世界坐标锁定到细像素格，保持像素画风并避免波纹随镜头抖动。</summary>
        float2 QuantizeWaterPosition(float2 positionWS)
        {
            float density = max(_PixelDensity, 1.0);
            return floor(positionWS * density + 0.5) / density;
        }

        /// <summary>生成稳定的二维随机值，避免程序波纹形成规则重复图案。</summary>
        float WaterHash(float2 cell)
        {
            float3 value = frac(float3(cell.xyx) * float3(0.1031, 0.103, 0.0973));
            value += dot(value, value.yzx + 33.33);
            return frac((value.x + value.y) * value.z);
        }

        /// <summary>在世界空间插值随机值，为流向弯曲和波峰断续提供连续噪声。</summary>
        float WaterNoise(float2 position)
        {
            float2 cell = floor(position);
            float2 local = frac(position);
            float2 blend = local * local * (3.0 - 2.0 * local);
            float bottom = lerp(WaterHash(cell), WaterHash(cell + float2(1.0, 0.0)), blend.x);
            float top = lerp(
                WaterHash(cell + float2(0.0, 1.0)),
                WaterHash(cell + float2(1.0, 1.0)),
                blend.x);
            return lerp(bottom, top, blend.y);
        }

        /// <summary>汇总海面各层光学信息，供水色、反光、焦散与白沫统一混合。</summary>
        struct WaterSurfaceData
        {
            half waterDepth;
            half depthBlend;
            half ripple;
            half rippleShadow;
            half reflection;
            half caustic;
            half specular;
            half whitecap;
            half moonReflection;
        };

        /// <summary>从 Tile Color 同时解出四向岸线位与当前格子的真实水深。</summary>
        void DecodeWaterTileData(half4 encodedData, out half4 shoreMask, out half waterDepth)
        {
            const half depthChannelScale = 0.45h;
            const half contactChannelOffset = 0.55h;
            shoreMask = step(0.5h, encodedData);
            half4 depthChannels = (encodedData - shoreMask * contactChannelOffset)
                / depthChannelScale;
            waterDepth = saturate(dot(depthChannels, half4(0.25h, 0.25h, 0.25h, 0.25h)));
        }

        /// <summary>利用屏幕位置和既有波形生成圆形月面及向下延伸的碎光带。</summary>
        half ComputeMoonReflection(
            float2 screenUV,
            float height,
            float macroA,
            float macroB,
            float detailA,
            float detailB,
            float time)
        {
            half moonStrength = saturate(
                _GlobalMoonlightIntensity * max(_MoonReflectionStrength, 0.0h))
                * _MoonReflectionColor.a;
            UNITY_BRANCH
            if (moonStrength <= 0.0001h)
                return 0.0h;

            float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.001);
            float2 moonDelta = screenUV - _MoonReflectionPosition.xy;
            float correctedX = moonDelta.x * aspect;
            float discRadius = max(_MoonDiscRadius, 0.001);
            float discWaveShift = (height * 0.08 + macroA - macroB) * discRadius * 0.16;
            float discDistance = length(float2(correctedX + discWaveShift, moonDelta.y));
            half disc = 1.0h - smoothstep(discRadius * 0.72, discRadius, discDistance);
            half halo = (1.0h - smoothstep(discRadius, discRadius * 1.85, discDistance)) * 0.2h;
            half discBreakup = lerp(
                0.62h,
                1.0h,
                smoothstep(-0.55, 0.65, height + (macroA - macroB) * 0.7));

            float belowMoon = -moonDelta.y;
            float trailLength = max(_MoonTrailLength, 0.001);
            float trailProgress = saturate(belowMoon / trailLength);
            half trailRange = step(0.0, belowMoon)
                * (1.0h - smoothstep(trailLength * 0.72, trailLength, belowMoon));
            float trailWidth = max(_MoonTrailWidth, 0.001)
                * lerp(0.6, 1.55, trailProgress);
            float trailWaveShift = (
                height * 0.28
                + (macroA - macroB) * 0.32
                + sin(screenUV.y * 58.0 - time * 1.7) * 0.16)
                * trailWidth;
            half trailCenter = 1.0h - smoothstep(
                trailWidth * 0.2,
                trailWidth,
                abs(correctedX + trailWaveShift));
            float stripeWave = sin(screenUV.y * 220.0 + time * 2.3 + macroA * 4.0);
            half stripeBreakup = lerp(
                0.18h,
                1.0h,
                smoothstep(-0.42, 0.62, stripeWave + detailA * 0.24 + detailB * 0.14));
            half trail = trailRange
                * trailCenter
                * stripeBreakup
                * lerp(1.0h, 0.42h, trailProgress);

            return saturate(disc * discBreakup + halo + trail) * moonStrength;
        }

        /// <summary>叠加大涌浪和多方向细浪，并汇总海面各层光学信息。</summary>
        WaterSurfaceData CalculateWaterSurface(
            float2 positionWS,
            float2 screenUV,
            half waterDepth)
        {
            WaterSurfaceData surface = (WaterSurfaceData)0;
            float2 direction = _FlowDirection.xy;
            direction *= rsqrt(max(dot(direction, direction), 0.001));
            float2 lateral = float2(-direction.y, direction.x);
            float2 pixelPosition = QuantizeWaterPosition(positionWS);
            float time = _Time.y * _WaveSpeed;

            float2 drift = float2(time * 0.018, -time * 0.012);
            float macroA = WaterNoise(pixelPosition * 0.065 + drift);
            float macroB = WaterNoise(
                pixelPosition * 0.11
                + float2(17.31, 9.17)
                + float2(-drift.y, drift.x));
            float2 warpedPosition = pixelPosition
                + direction * (macroA - 0.5) * _WaveDistortion
                + lateral * (macroB - 0.5) * _WaveDistortion * 1.25;

            float2 swellDirection = direction * 0.42 + lateral * 0.91;
            swellDirection *= rsqrt(max(dot(swellDirection, swellDirection), 0.001));
            float2 detailDirectionA = direction * 0.8 - lateral * 0.6;
            detailDirectionA *= rsqrt(max(dot(detailDirectionA, detailDirectionA), 0.001));
            float2 detailDirectionB = -direction * 0.18 + lateral * 0.98;
            detailDirectionB *= rsqrt(max(dot(detailDirectionB, detailDirectionB), 0.001));

            float swellPhaseA = dot(warpedPosition, direction) * _SwellScale + time * 0.55;
            float swellPhaseB = dot(warpedPosition, swellDirection) * _SwellScale * 1.72
                - time * 0.38
                + macroB * 1.4;
            float detailPhaseA = dot(warpedPosition, detailDirectionA) * _DetailScale
                + time * 1.25
                + macroA * 0.9;
            float detailPhaseB = dot(warpedPosition, detailDirectionB) * _DetailScale * 1.83
                - time * 1.55
                + macroB * 1.1;

            float swellA = sin(swellPhaseA);
            float swellB = sin(swellPhaseB);
            float detailA = sin(detailPhaseA);
            float detailB = sin(detailPhaseB);
            float height = swellA * 0.48 + swellB * 0.27 + detailA * 0.17 + detailB * 0.08;

            float2 gradient = direction * cos(swellPhaseA) * _SwellScale * 0.48;
            gradient += swellDirection * cos(swellPhaseB) * _SwellScale * 1.72 * 0.27;
            gradient += detailDirectionA * cos(detailPhaseA) * _DetailScale * 0.075;
            gradient += detailDirectionB * cos(detailPhaseB) * _DetailScale * 1.83 * 0.035;
            float3 normalWS = normalize(float3(
                -gradient.x * _NormalStrength,
                -gradient.y * _NormalStrength,
                1.0));

            // 权威水深决定整体明暗，噪声只在中间深度保留轻微的自然过渡。
            surface.waterDepth = smoothstep(0.0h, 1.0h, saturate(waterDepth));
            half depthVariation = (
                (macroA - 0.5) * 0.12
                + (macroB - 0.5) * 0.05
                + height * 0.025)
                * (surface.waterDepth * (1.0h - surface.waterDepth) * 4.0h);
            surface.depthBlend = saturate(1.0h - surface.waterDepth + depthVariation);

            // 中尺度浪脊与大涌浪共用扭曲坐标，再用低频噪声切成自然短段。
            float rippleWarp = WaterNoise(
                pixelPosition * 0.19
                + float2(-time * 0.045, time * 0.032)
                + float2(16.8, 7.4)) - 0.5;
            float ripplePhaseA = dot(warpedPosition, direction) * _RippleScale
                + time * 0.82
                + (macroB - 0.5) * 2.6
                + (macroA - 0.5) * 0.9
                + rippleWarp * 3.2;
            float ripplePhaseB = dot(warpedPosition, swellDirection) * _RippleScale * 1.43
                - time * 0.57
                + (macroA - 0.5) * 2.9
                + (macroB - 0.5) * 0.7
                - rippleWarp * 2.15;
            float rippleWaveA = sin(ripplePhaseA);
            float rippleWaveB = sin(ripplePhaseB);
            float crestExponent = 4.0 / max(_RippleWidth, 0.01);
            float crestA = pow(saturate(rippleWaveA * 0.5 + 0.5), crestExponent);
            float crestB = pow(saturate(rippleWaveB * 0.5 + 0.5), crestExponent * 1.12);
            float2 rippleLateralB = float2(-swellDirection.y, swellDirection.x);

            float segmentA = WaterNoise(
                float2(
                    dot(pixelPosition, lateral) * 0.58 + time * 0.055,
                    dot(pixelPosition, direction) * 0.13 - time * 0.018)
                + float2(5.7, 19.3));
            float segmentB = WaterNoise(
                float2(
                    dot(pixelPosition, rippleLateralB) * 0.64 - time * 0.046,
                    dot(pixelPosition, swellDirection) * 0.15 + time * 0.016)
                + float2(27.4, 3.8));
            float segmentDetailA = WaterNoise(
                float2(
                    dot(pixelPosition, lateral) * 1.21 - time * 0.085,
                    dot(pixelPosition, direction) * 0.22 + time * 0.021)
                + float2(34.1, 11.6));
            float segmentDetailB = WaterNoise(
                float2(
                    dot(pixelPosition, rippleLateralB) * 1.34 + time * 0.074,
                    dot(pixelPosition, swellDirection) * 0.25 - time * 0.018)
                + float2(8.9, 36.2));
            float segmentGateA = smoothstep(0.28, 0.5, segmentA * segmentDetailA);
            float segmentGateB = smoothstep(0.31, 0.53, segmentB * segmentDetailB);
            crestA *= segmentGateA;
            crestB *= segmentGateB * 0.28;
            surface.ripple = max(crestA, crestB)
                * saturate(_RippleStrength)
                * _RippleColor.a;

            // 浪脊后方的窄暗带强化起伏，不把整片水面压暗。
            float shadowA = pow(
                saturate(sin(ripplePhaseA - 0.3) * 0.5 + 0.5),
                crestExponent * 0.82)
                * segmentGateA;
            float shadowB = pow(
                saturate(sin(ripplePhaseB - 0.25) * 0.5 + 0.5),
                crestExponent * 0.9)
                * segmentGateB
                * 0.2;
            surface.rippleShadow = max(shadowA, shadowB)
                * (1.0 - max(crestA, crestB))
                * saturate(_RippleShadowStrength);

            // 以俯视相机入射方向反射虚拟环境方向，让镜面亮块真实跟随波面法线移动。
            float3 mirrorDirection = _ReflectionDirection.xyz;
            mirrorDirection *= rsqrt(max(dot(mirrorDirection, mirrorDirection), 0.001));
            float3 reflectedView = reflect(float3(0.0, 0.0, -1.0), normalWS);
            float mirrorAlignment = saturate(dot(reflectedView, mirrorDirection));
            float mirrorExponent = exp2(lerp(1.5, 6.0, saturate(_ReflectionSmoothness)));
            float mirrorLobe = pow(mirrorAlignment, mirrorExponent);
            float mirrorBreakup = saturate(
                0.64
                + (macroA - 0.5) * 0.5
                + (macroB - 0.5) * 0.32
                + height * 0.07);
            float coherentMirror = mirrorLobe
                * lerp(mirrorBreakup, 1.0, saturate(_ReflectionSmoothness));
            float fresnel = saturate((1.0 - normalWS.z) * 3.5);
            surface.reflection = saturate(coherentMirror + fresnel * fresnel * 0.22)
                * saturate(_ReflectionStrength)
                * _ReflectionColor.a;

            float3 sunDirection = _SunDirection.xyz;
            sunDirection *= rsqrt(max(dot(sunDirection, sunDirection), 0.001));
            float3 halfDirection = sunDirection + float3(0.0, 0.0, 1.0);
            halfDirection *= rsqrt(max(dot(halfDirection, halfDirection), 0.001));
            float2 sparkleDrift = float2(time * 0.11, -time * 0.08);
            float sparkleFine = WaterNoise(
                pixelPosition * 1.65
                + sparkleDrift
                + float2(7.23, 14.81));
            float sparkleBreakup = WaterNoise(
                pixelPosition * 3.15
                - sparkleDrift * 1.7
                + float2(21.47, 5.39));
            float sparkleGate = smoothstep(
                0.5,
                0.72,
                sparkleFine * sparkleBreakup + max(detailA, detailB) * 0.025);
            surface.specular = pow(
                saturate(dot(normalWS, halfDirection)),
                max(_SpecularPower, 1.0))
                * sparkleGate
                * saturate(_SpecularStrength)
                * _SpecularColor.a;

            float2 causticPosition = warpedPosition * (_DetailScale * 0.42);
            float2 causticDrift = direction * time * 0.09 + lateral * time * 0.035;
            float causticWarp = WaterNoise(
                causticPosition * 0.37 + causticDrift + float2(13.2, 6.7)) - 0.5;
            float causticFieldA = WaterNoise(
                causticPosition
                + direction * causticWarp * 1.45
                + causticDrift
                + float2(3.4, 18.6));
            float causticFieldB = WaterNoise(
                causticPosition * 1.67
                + lateral * causticWarp * 1.2
                - causticDrift * 1.35
                + float2(24.8, 2.9));
            float causticRidgeA = smoothstep(
                0.92,
                0.985,
                1.0 - abs(causticFieldA * 2.0 - 1.0));
            float causticRidgeB = smoothstep(
                0.94,
                0.992,
                1.0 - abs(causticFieldB * 2.0 - 1.0));
            float causticBreakup = WaterNoise(
                pixelPosition * 0.31
                + float2(-time * 0.018, time * 0.012)
                + float2(9.6, 32.1));
            float causticNetwork = max(causticRidgeA, causticRidgeB * 0.62)
                * smoothstep(0.3, 0.72, causticBreakup);
            surface.caustic = causticNetwork
                * pow(saturate(surface.depthBlend), 1.65)
                * saturate(_CausticStrength)
                * _CausticColor.a;

            float whitecapNoiseA = WaterNoise(
                pixelPosition * 0.47
                + float2(-time * 0.038, time * 0.026)
                + float2(31.7, 4.9));
            float whitecapNoiseB = WaterNoise(
                pixelPosition * 0.93
                + float2(time * 0.052, -time * 0.033)
                + float2(4.6, 26.3));
            float crestHeight = height + max(detailA, detailB) * 0.12;
            float foamBreakup = smoothstep(0.36, 0.66, whitecapNoiseA * whitecapNoiseB);
            float rippleFoam = max(crestA, crestB * 0.65)
                * lerp(0.28, 1.0, foamBreakup);
            float swellFoam = smoothstep(0.7, 0.92, crestHeight)
                * smoothstep(0.44, 0.68, whitecapNoiseA * whitecapNoiseB);
            surface.whitecap = saturate(max(rippleFoam, swellFoam * 0.72))
                * saturate(_WhitecapStrength)
                * _FoamColor.a;
            surface.moonReflection = ComputeMoonReflection(
                screenUV,
                height,
                macroA,
                macroB,
                detailA,
                detailB,
                time);
            return surface;
        }

        /// <summary>按深浅水、焦散、天空反光、太阳高光和白沫的层级合成海面。</summary>
        half3 ApplyWaterSurface(half3 sourceColor, WaterSurfaceData surface)
        {
            half3 waterTint = lerp(_DeepColor.rgb, _ShallowColor.rgb, surface.depthBlend);
            half tintStrength = lerp(
                saturate(_SurfaceTint),
                1.0h,
                surface.waterDepth);
            sourceColor = lerp(sourceColor, waterTint, tintStrength);
            sourceColor *= 1.0h - saturate(surface.waterDepth * _DepthDarkening);
            sourceColor = lerp(sourceColor, _DeepColor.rgb, surface.rippleShadow);
            sourceColor += _CausticColor.rgb * surface.caustic;
            sourceColor = lerp(sourceColor, _ReflectionColor.rgb, surface.reflection);
            sourceColor = lerp(sourceColor, _RippleColor.rgb, surface.ripple);
            sourceColor = lerp(sourceColor, _SpecularColor.rgb, surface.specular);
            sourceColor = lerp(sourceColor, _FoamColor.rgb, surface.whitecap);
            return saturate(sourceColor);
        }

        /// <summary>在场景 2D 光照之后叠加月光，避免夜间亮度被重复相乘。</summary>
        half3 ApplyMoonReflection(half3 sourceColor, half moonReflection)
        {
            half3 reflectedLight = _MoonReflectionColor.rgb * saturate(moonReflection);
            return saturate(sourceColor + reflectedLight * (1.0h - sourceColor));
        }

        /// <summary>RGBA 分别读取左、右、下、上岸线，计算水格内侧的渐变遮罩。</summary>
        half ComputeRecessShadow(float2 localPosition, half4 shoreMask)
        {
            float2 cellUV = frac(localPosition + 0.0001);
            half width = max(_EdgeWidth, 0.001h);
            half left = shoreMask.r * (1.0h - smoothstep(0.0h, width, cellUV.x));
            half right = shoreMask.g * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.x));
            half bottom = shoreMask.b * (1.0h - smoothstep(0.0h, width, cellUV.y));
            half top = shoreMask.a * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.y));
            half strongest = max(max(left, right), max(bottom, top));
            half overlap = saturate(left + right + bottom + top - strongest);
            return saturate(strongest + overlap * _CornerStrength);
        }

        /// <summary>在岸线内侧叠加流动泡沫，再保留贴近陆地的下沉暗边。</summary>
        half3 ApplyShore(half3 sourceColor, half recess, float2 positionWS)
        {
            half shoreBand = saturate(recess * (1.0h - recess) * 4.0h);
            float foamTime = _Time.y * _FoamSpeed;
            float foamNoise = WaterNoise(
                positionWS * 0.58
                + float2(foamTime * 0.04, -foamTime * 0.03)
                + float2(11.3, 27.1));
            half foamPulse = 0.7h + 0.3h * sin(
                dot(positionWS, _FlowDirection.xy) * 1.25
                + foamTime
                + foamNoise * 2.4);
            half foam = saturate(shoreBand * (0.45h + foamNoise * 0.75h) * foamPulse)
                * saturate(_ShoreFoamStrength)
                * _FoamColor.a;
            sourceColor = lerp(
                sourceColor,
                _ShoreColor.rgb,
                shoreBand * saturate(_ShoreStrength) * _ShoreColor.a);
            sourceColor = lerp(sourceColor, _FoamColor.rgb, foam);
            return lerp(
                sourceColor,
                _EdgeColor.rgb,
                saturate(recess * _EdgeStrength * _EdgeColor.a));
        }
    ENDHLSL

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

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #pragma multi_compile _ DEBUG_DISPLAY

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half2 lightingUV : TEXCOORD1;
                float2 localPosition : TEXCOORD2;
                half4 waterTileData : TEXCOORD3;
                float2 positionWS : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

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

            /// <summary>准备 2D 光照、岸线、世界位置与屏幕位置数据。</summary>
            Varyings WaterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.localPosition = input.positionOS.xy;
                output.waterTileData = input.color;
                output.lightingUV = half2(ComputeScreenPos(output.positionCS / output.positionCS.w).xy);
                return output;
            }

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            /// <summary>合成受 2D 灯光影响的水面，并在末尾叠加月光倒影。</summary>
            half4 WaterFragment(Varyings input) : SV_Target
            {
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                half4 shoreMask;
                half waterDepth;
                DecodeWaterTileData(input.waterTileData, shoreMask, waterDepth);
                WaterSurfaceData waterSurface = CalculateWaterSurface(
                    input.positionWS,
                    input.lightingUV,
                    waterDepth);
                main.rgb = ApplyWaterSurface(main.rgb, waterSurface);
                half recess = ComputeRecessShadow(input.localPosition, shoreMask);
                main.rgb = ApplyShore(main.rgb, recess, input.positionWS);

                half4 lightMask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, lightMask, surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                lit.rgb = ApplyMoonReflection(lit.rgb, waterSurface.moonReflection);
                return lit;
            }
            ENDHLSL
        }

        // 非 2D Renderer 下保留同样的水面表现，方便 Scene 视图检查。
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
                half4 waterTileData : TEXCOORD2;
                float2 positionWS : TEXCOORD3;
                float2 screenUV : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            /// <summary>准备非 2D Renderer 下的水面与屏幕位置数据。</summary>
            Varyings UnlitVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.positionWS = TransformObjectToWorld(input.positionOS).xy;
                output.uv = input.uv;
                output.localPosition = input.positionOS.xy;
                output.waterTileData = input.color;
                float4 screenPosition = ComputeScreenPos(output.positionCS);
                output.screenUV = screenPosition.xy / max(screenPosition.w, 0.0001);
                return output;
            }

            /// <summary>合成非 2D Renderer 下的水面及月光倒影。</summary>
            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                main *= _Color * _RendererColor;
                half4 shoreMask;
                half waterDepth;
                DecodeWaterTileData(input.waterTileData, shoreMask, waterDepth);
                WaterSurfaceData waterSurface = CalculateWaterSurface(
                    input.positionWS,
                    input.screenUV,
                    waterDepth);
                main.rgb = ApplyWaterSurface(main.rgb, waterSurface);
                half recess = ComputeRecessShadow(input.localPosition, shoreMask);
                main.rgb = ApplyShore(main.rgb, recess, input.positionWS);
                main.rgb = ApplyMoonReflection(main.rgb, waterSurface.moonReflection);
                return main;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
