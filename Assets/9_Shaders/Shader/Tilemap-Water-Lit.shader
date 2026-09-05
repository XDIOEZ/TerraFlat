Shader "FlatWorld/2D/Tilemap Water Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("水面贴图", 2D) = "white" {}
        _MaskTex("灯光遮罩", 2D) = "white" {}
        [PerRendererData] _WaterDepthTexture("水深场", 2D) = "black" {}
        [HideInInspector] _WaterDepthUvScaleOffset("水深纹理坐标", Vector) = (1,1,0,0)

        [Header(Ocean Surface)]
        _DeepColor("深海颜色", Color) = (0.035, 0.13, 0.19, 1)
        _ShallowColor("浅海颜色", Color) = (0.19, 0.42, 0.4, 1)
        _SurfaceTint("海水染色强度", Range(0, 1)) = 1
        _SwellScale("涌浪尺度", Range(0.05, 4)) = 0.74
        _DetailScale("细浪尺度", Range(0.5, 12)) = 4.6
        _WaveSpeed("海流速度", Range(-3, 3)) = 0.42
        _WaveDistortion("海流扭曲", Range(0, 4)) = 0.8
        _NormalStrength("表面起伏", Range(0, 0.8)) = 0.44
        _FlowDirection("流动方向", Vector) = (1, 0.35, 0, 0)
        _TideCyclesPerDay("每日潮汐循环次数", Range(1, 4)) = 2.0
        _RippleColor("浪脊颜色", Color) = (0.3, 0.53, 0.53, 1)
        _RippleStrength("浪脊强度", Range(0, 1)) = 0.12
        _RippleScale("浪纹尺度", Range(0.25, 6)) = 2.8
        _RippleWidth("浪脊宽度", Range(0.04, 0.45)) = 0.18
        _RippleShadowStrength("浪背暗部", Range(0, 0.5)) = 0.06

        [Header(Reflection And Foam)]
        _ReflectionColor("镜面反射颜色", Color) = (0.52, 0.65, 0.72, 1)
        _ReflectionStrength("镜面反射强度", Range(0, 1)) = 0.65
        _ReflectionSmoothness("镜面反射平滑度", Range(0, 1)) = 0.68
        _ReflectionDirection("镜面环境方向", Vector) = (-0.35, 0.18, 0.92, 0)
        _SpecularColor("太阳高光", Color) = (1, 0.97, 0.88, 1)
        _SpecularStrength("太阳高光强度", Range(0, 1)) = 0.34
        _SpecularPower("太阳高光锐度", Range(4, 96)) = 72
        _SunDirection("太阳方向", Vector) = (0.28, 0.42, 0.86, 0)
        _CausticColor("焦散颜色", Color) = (0.42, 0.66, 0.58, 1)
        _CausticStrength("焦散强度", Range(0, 1)) = 0.025
        _FoamColor("泡沫颜色", Color) = (0.81, 0.87, 0.85, 1)
        _WhitecapStrength("浪峰白沫", Range(0, 1)) = 0.12

        [Header(Moon Reflection)]
        _MoonReflectionColor("月光倒影颜色", Color) = (0.72, 0.86, 1, 1)
        _MoonReflectionStrength("月光倒影强度", Range(0, 8)) = 4.2
        _MoonReflectionPosition("月光倒影屏幕位置", Vector) = (0.68, 0.62, 0, 0)
        _MoonDiscRadius("月面倒影半径", Range(0.01, 0.2)) = 0.055
        _MoonTrailLength("月光带长度", Range(0.01, 0.7)) = 0.34
        _MoonTrailWidth("月光带宽度", Range(0.005, 0.2)) = 0.065

        [Header(Shore)]
        _EdgeWidth("岸线宽度", Range(0.03, 0.45)) = 0.22
        _CornerStrength("转角叠加强度", Range(0, 1)) = 0.18
        _ShoreColor("岸线亮部", Color) = (0.39, 0.57, 0.53, 1)
        _ShoreStrength("岸线亮部强度", Range(0, 1)) = 0.035
        _ShoreFoamStrength("岸边泡沫强度", Range(0, 1)) = 0.3
        _FoamSpeed("岸边泡沫速度", Range(0, 3)) = 0.52

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
        TEXTURE2D(_WaterDepthTexture);
        SAMPLER(sampler_WaterDepthTexture);

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
        half4 _ShoreColor;
        float4 _FlowDirection;
        float _TideCyclesPerDay;
        float _GlobalGameDay;
        float4 _ReflectionDirection;
        float4 _SunDirection;
        float4 _MoonReflectionPosition;
        float4 _WaterDepthUvScaleOffset;
        half _SurfaceTint;
        float _SwellScale;
        float _DetailScale;
        float _WaveSpeed;
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
        half _GlobalMoonAppearance;
        half _EdgeWidth;
        half _CornerStrength;
        half _ShoreStrength;
        half _ShoreFoamStrength;
        float _FoamSpeed;

        /// <summary>读取材质定义的潮流轴；潮汐只沿该轴往返，不再让整片水面持续绕圈。</summary>
        float2 ResolveWaterFlowAxis()
        {
            float2 baseDirection = _FlowDirection.xy;
            float baseLengthSq = dot(baseDirection, baseDirection);
            return baseLengthSq > 0.0001
                ? baseDirection * rsqrt(baseLengthSq)
                : float2(1.0, 0.0);
        }

        /// <summary>用半日潮的积分相位驱动往返流动；相位到达峰谷时流速自然降为零后反向。</summary>
        float ResolveTideFlowPhase(float flowSpeed)
        {
            const float fullTurn = 6.28318530718;
            const float tideTravel = 96.0;
            float cyclesPerDay = max(_TideCyclesPerDay, 0.01);
            float tideAngle = _GlobalGameDay * cyclesPerDay * fullTurn;
            return sin(tideAngle) * flowSpeed * tideTravel;
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

        /// <summary>累加波高及其解析斜率，短波按像素覆盖范围衰减，避免缩远后的闪烁。</summary>
        void AccumulateWaterWave(
            float2 position, float2 direction, float frequency, float steepness,
            float time, float phaseOffset, inout float height, inout float2 slope)
        {
            float phase = dot(position, direction) * frequency
                - time * sqrt(9.81 * frequency) + phaseOffset;
            float footprint = fwidth(dot(position, direction)) * frequency;
            float visibility = 1.0 - smoothstep(0.8, 2.8, footprint);
            float waveSin;
            float waveCos;
            sincos(phase, waveSin, waveCos);
            height += waveSin * (steepness / frequency) * visibility;
            slope += direction * waveCos * steepness * visibility;
        }

        /// <summary>用连续噪声的解析梯度补充细碎波面，避免独立随机亮点脱离水面运动。</summary>
        float2 WaterNoiseSlope(float2 position)
        {
            float2 cell = floor(position);
            float2 local = frac(position);
            float2 blend = local * local * (3.0 - 2.0 * local);
            float2 derivative = 6.0 * local * (1.0 - local);
            float a = WaterHash(cell);
            float b = WaterHash(cell + float2(1.0, 0.0));
            float c = WaterHash(cell + float2(0.0, 1.0));
            float d = WaterHash(cell + float2(1.0, 1.0));
            return derivative * float2(
                lerp(b - a, d - c, blend.y),
                lerp(c - a, d - b, blend.x));
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

        /// <summary>Tile Color RGBA 只表示左、右、下、上四向岸线。</summary>
        half4 DecodeWaterShoreMask(half4 encodedData)
        {
            return step(0.5h, encodedData);
        }

        /// <summary>通过显式世界坐标映射采样 Chunk 水深，避免 Tilemap 合批改变局部坐标。</summary>
        half SampleWaterDepth(float2 positionWS)
        {
            float2 depthUV = positionWS * _WaterDepthUvScaleOffset.xy
                + _WaterDepthUvScaleOffset.zw;
            return SAMPLE_TEXTURE2D(_WaterDepthTexture, sampler_WaterDepthTexture, depthUV).r;
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
            half moonAppearance = saturate(_GlobalMoonAppearance);
            half appearanceEase = moonAppearance * moonAppearance * (3.0h - 2.0h * moonAppearance);
            half brightnessEase = appearanceEase * appearanceEase;
            half moonStrength = saturate(
                _GlobalMoonlightIntensity * max(_MoonReflectionStrength, 0.0h))
                * _MoonReflectionColor.a
                * brightnessEase;
            UNITY_BRANCH
            if (moonStrength <= 0.0001h)
                return 0.0h;

            float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.001);
            float2 moonDelta = screenUV - _MoonReflectionPosition.xy;
            float correctedX = moonDelta.x * aspect;
            float sizeEase = lerp(0.2, 1.0, appearanceEase);
            float discRadius = max(_MoonDiscRadius * sizeEase, 0.001);
            float discWaveShift = (height * 0.08 + macroA - macroB) * discRadius * 0.16;
            float discDistance = length(float2(correctedX + discWaveShift, moonDelta.y));
            half disc = 1.0h - smoothstep(discRadius * 0.72, discRadius, discDistance);
            half halo = (1.0h - smoothstep(discRadius, discRadius * 1.85, discDistance)) * 0.2h;
            half discBreakup = lerp(
                0.62h,
                1.0h,
                smoothstep(-0.55, 0.65, height + (macroA - macroB) * 0.7));

            float belowMoon = -moonDelta.y;
            float trailLength = max(_MoonTrailLength * lerp(0.12, 1.0, appearanceEase), 0.001);
            float trailProgress = saturate(belowMoon / trailLength);
            half trailRange = step(0.0, belowMoon)
                * (1.0h - smoothstep(trailLength * 0.72, trailLength, belowMoon));
            float trailWidth = max(_MoonTrailWidth * lerp(0.3, 1.0, appearanceEase), 0.001)
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

        /// <summary>六组色散波与细波法线共同驱动反射，潮流平移和风浪传播分别跟随游戏时间。</summary>
        WaterSurfaceData CalculateWaterSurface(
            float2 positionWS,
            float2 screenUV,
            half waterDepth)
        {
            WaterSurfaceData surface = (WaterSurfaceData)0;
            float2 direction = ResolveWaterFlowAxis();
            float2 lateral = float2(-direction.y, direction.x);
            float time = _GlobalGameDay * 240.0 * _WaveSpeed;
            float tide = ResolveTideFlowPhase(_WaveSpeed);
            float2 waterPosition = positionWS - direction * tide * 0.045;
            float2 drift = direction * time * 0.08;
            float macroA = WaterNoise(waterPosition * 0.075 - drift * 0.1);
            float macroB = WaterNoise(waterPosition * 0.13 + drift * 0.07 + float2(17.31, 9.17));
            float2 warpedPosition = waterPosition
                + (direction * (macroA - 0.5) + lateral * (macroB - 0.5)) * _WaveDistortion;

            // 波长不同，传播速度也不同；固定方向叠加，不让整片水面旋转或在潮汐换向时停住。
            float height = 0.0;
            float2 slope = float2(0.0, 0.0);
            float swellScale = max(_SwellScale, 0.05);
            float detailScale = max(_DetailScale, 0.5);
            AccumulateWaterWave(warpedPosition, direction,
                swellScale, 0.34, time, 0.4, height, slope);
            AccumulateWaterWave(warpedPosition, direction * 0.8 + lateral * 0.6,
                swellScale * 1.71, 0.22, time, 2.1, height, slope);
            AccumulateWaterWave(warpedPosition, direction * 0.6 - lateral * 0.8,
                swellScale * 2.37, 0.17, time, 4.6, height, slope);
            AccumulateWaterWave(warpedPosition, direction * 0.96 + lateral * 0.28,
                detailScale, 0.19, time, 1.3, height, slope);
            AccumulateWaterWave(warpedPosition, direction * 0.28 - lateral * 0.96,
                detailScale * 1.73, 0.13, time, 3.8, height, slope);
            AccumulateWaterWave(warpedPosition, direction * 0.8 - lateral * 0.6,
                detailScale * 2.61, 0.09, time, 5.2, height, slope);

            float2 detailPosition = waterPosition * max(_RippleScale, 0.25) - drift;
            float detailVisibility = 1.0 - smoothstep(0.3, 1.2, length(fwidth(detailPosition)));
            float2 fineSlope = WaterNoiseSlope(detailPosition + float2(8.3, 21.7));
            fineSlope += WaterNoiseSlope(detailPosition * 1.91 + drift * 0.6) * 0.45;
            slope += fineSlope * 0.2 * detailVisibility;
            float3 normalWS = normalize(float3(-slope * _NormalStrength, 1.0));

            // 水体吸收随深度平滑增长；深水不再显露原贴图里的装饰性亮块。
            surface.waterDepth = saturate(waterDepth);
            float transmittance = exp2(-surface.waterDepth * 3.0);
            surface.depthBlend = saturate((transmittance - 0.125) / 0.875);

            float3 sunDirection = normalize(_SunDirection.xyz);
            float3 viewDirection = float3(0.0, 0.0, 1.0);
            float3 halfDirection = normalize(sunDirection + viewDirection);
            float ndv = saturate(normalWS.z);
            float ndl = saturate(dot(normalWS, sunDirection));
            float ndh = saturate(dot(normalWS, halfDirection));
            float vdh = saturate(dot(viewDirection, halfDirection));
            float fresnel = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);

            // 天空只提供柔和反射；太阳使用 GGX 微表面高光，波光由法线自然切碎。
            float3 reflectedView = reflect(-viewDirection, normalWS);
            float mirrorAlignment = saturate(dot(reflectedView, normalize(_ReflectionDirection.xyz)));
            float skyLobe = pow(mirrorAlignment, lerp(4.0, 18.0, _ReflectionSmoothness));
            surface.reflection = saturate(_ReflectionStrength) * (fresnel + skyLobe * 0.075)
                * _ReflectionColor.a;

            float roughness = lerp(0.38, 0.16, saturate(_ReflectionSmoothness))
                * pow(48.0 / max(_SpecularPower, 4.0), 0.25);
            float3 normalDx = ddx(normalWS);
            float3 normalDy = ddy(normalWS);
            float normalVariance = dot(normalDx, normalDx) + dot(normalDy, normalDy);
            float alphaSquared = max(pow(roughness, 4.0) + normalVariance * 0.25, 0.0025);
            float denominator = ndh * ndh * (alphaSquared - 1.0) + 1.0;
            float distribution = alphaSquared / max(3.14159265 * denominator * denominator, 0.00001);
            float geometryK = (roughness + 1.0) * (roughness + 1.0) * 0.125;
            float geometry = ndl / max(ndl * (1.0 - geometryK) + geometryK, 0.001);
            float sunFresnel = 0.02 + 0.98 * pow(1.0 - vdh, 5.0);
            float sunRadiance = distribution * geometry * sunFresnel / max(ndv, 0.001);
            // 平滑压缩强高光，避免硬截断把连续波光变成等亮色块。
            surface.specular = sunRadiance / (1.0 + sunRadiance)
                * saturate(_SpecularStrength) * _SpecularColor.a;

            // 波峰只保留少量透光和波背阴影，不再画独立的白色正弦轮廓线。
            float crest = smoothstep(0.08, 0.8 - saturate(_RippleWidth) * 0.6, height);
            float backLight = saturate(dot(-normalWS.xy, sunDirection.xy) + 0.18);
            surface.ripple = crest * backLight * saturate(_RippleStrength) * _RippleColor.a;
            surface.rippleShadow = (1.0 - ndl) * saturate(_RippleShadowStrength) * 0.35;

            float detailA = WaterNoise(detailPosition * 0.73 + normalWS.xy * 0.8);
            float detailB = WaterNoise(detailPosition * 1.17 - drift * 0.3 + float2(23.1, 7.6));
            float causticFocus = pow(saturate(1.0 - abs(detailA - detailB) * 3.0), 12.0);
            surface.caustic = causticFocus * pow(surface.depthBlend, 4.0)
                * saturate(_CausticStrength) * _CausticColor.a;

            // 白沫只在足够陡的波峰上零星出现，避免每道细浪都变成白线。
            float steepCrest = crest * smoothstep(0.12, 0.46, dot(slope, slope));
            float foamBreakup = smoothstep(0.5, 0.78, detailA * 0.6 + detailB * 0.4);
            surface.whitecap = steepCrest * foamBreakup
                * saturate(_WhitecapStrength) * _FoamColor.a;
            surface.moonReflection = ComputeMoonReflection(
                screenUV, height, macroA, macroB, detailA * 2.0 - 1.0, detailB * 2.0 - 1.0, time);
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
            sourceColor = lerp(sourceColor, _DeepColor.rgb, surface.rippleShadow);
            sourceColor += _CausticColor.rgb * surface.caustic;
            sourceColor = lerp(sourceColor, _ReflectionColor.rgb, surface.reflection);
            sourceColor = lerp(sourceColor, _RippleColor.rgb, surface.ripple);
            sourceColor += _SpecularColor.rgb * surface.specular;
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
        half ComputeShoreRecess(float2 positionWS, half4 shoreMask)
        {
            float2 cellUV = frac(positionWS + 0.0001);
            half width = max(_EdgeWidth, 0.001h);
            half left = shoreMask.r * (1.0h - smoothstep(0.0h, width, cellUV.x));
            half right = shoreMask.g * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.x));
            half bottom = shoreMask.b * (1.0h - smoothstep(0.0h, width, cellUV.y));
            half top = shoreMask.a * (1.0h - smoothstep(0.0h, width, 1.0h - cellUV.y));
            half strongest = max(max(left, right), max(bottom, top));
            half overlap = saturate(left + right + bottom + top - strongest);
            return saturate(strongest + overlap * _CornerStrength);
        }

        /// <summary>岸边只留断续的薄泡沫，沿游戏时间起落，避免描出连续的方格亮边。</summary>
        half3 ApplyShore(half3 sourceColor, half recess, float2 positionWS)
        {
            float2 direction = ResolveWaterFlowAxis();
            float tide = ResolveTideFlowPhase(_FoamSpeed);
            float time = _GlobalGameDay * 180.0 * _FoamSpeed;
            float2 foamPosition = positionWS - direction * tide * 0.04;
            float foamNoise = WaterNoise(foamPosition * 1.1 + float2(11.3, 27.1));
            float foamDetail = WaterNoise(foamPosition * 3.7 - direction * time * 0.12);
            float wash = 0.5 + 0.5 * sin(time + foamNoise * 4.0);
            float shoreBand = pow(saturate(recess * (1.0 - recess) * 4.0), 1.5);
            float foamCoverage = smoothstep(0.48, 0.82,
                foamNoise * 0.5 + foamDetail * 0.35 + wash * 0.25);
            float foam = shoreBand * foamCoverage * saturate(_ShoreFoamStrength) * _FoamColor.a;
            sourceColor = lerp(sourceColor, _ShoreColor.rgb,
                shoreBand * foamNoise * saturate(_ShoreStrength) * _ShoreColor.a);
            return lerp(sourceColor, _FoamColor.rgb, foam);
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
                half4 waterTileData : TEXCOORD2;
                float2 positionWS : TEXCOORD3;
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
                half4 shoreMask = DecodeWaterShoreMask(input.waterTileData);
                half waterDepth = SampleWaterDepth(input.positionWS);
                WaterSurfaceData waterSurface = CalculateWaterSurface(
                    input.positionWS,
                    input.lightingUV,
                    waterDepth);
                main.rgb = ApplyWaterSurface(main.rgb, waterSurface);
                half recess = ComputeShoreRecess(input.positionWS, shoreMask);
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
                half4 waterTileData : TEXCOORD1;
                float2 positionWS : TEXCOORD2;
                float2 screenUV : TEXCOORD3;
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
                half4 shoreMask = DecodeWaterShoreMask(input.waterTileData);
                half waterDepth = SampleWaterDepth(input.positionWS);
                WaterSurfaceData waterSurface = CalculateWaterSurface(
                    input.positionWS,
                    input.screenUV,
                    waterDepth);
                main.rgb = ApplyWaterSurface(main.rgb, waterSurface);
                half recess = ComputeShoreRecess(input.positionWS, shoreMask);
                main.rgb = ApplyShore(main.rgb, recess, input.positionWS);
                main.rgb = ApplyMoonReflection(main.rgb, waterSurface.moonReflection);
                return main;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
