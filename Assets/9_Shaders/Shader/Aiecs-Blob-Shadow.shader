Shader "FlatWorld/2D/AIECS Blob Shadow"
{
    Properties
    {
        [HideInInspector] _ShadowUvTransform("UV Scale / Offset", Vector) = (1,1,0,0)
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        // ECS MeshRenderer 使用 Shadow/0；旧 Item BRG 材质覆写为 Default/Queue 2995。
        Tags { "Queue"="Transparent-9" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "BlobShadow"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 与太阳长投影共享设置滑块；不能放在 Properties 中遮蔽全局参数。
            float _WorldSunShadowBlur;
            half4 _WorldSunShadowColor;
            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowUvTransform;
                half4 _Color;
                half4 _RendererColor;
            CBUFFER_END
            #if defined(UNITY_DOTS_INSTANCING_ENABLED)
            UNITY_DOTS_INSTANCING_START(UserPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(uint, _ContactShadowData)
            UNITY_DOTS_INSTANCING_END(UserPropertyMetadata)
            #endif
            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; half alpha : COLOR; float2 uv : TEXCOORD0; };
            // 昼夜、水深和死亡透明度由批量表现入口提供，不采样光照纹理或阴影图。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(UNITY_DOTS_INSTANCING_ENABLED)
                uint address = UNITY_DOTS_INSTANCED_METADATA_NAME(uint, _ContactShadowData)
                    + GetDOTSInstanceIndex() * 32u;
                float4 footprint = asfloat(unity_DOTSInstanceData.Load4(address));
                float4 appearance = asfloat(unity_DOTSInstanceData.Load4(address + 16u));
                float3 positionWS = float3(footprint.xy + input.positionOS.xy * footprint.zw, 0);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.alpha = input.color.a * appearance.x * _Color.a * _RendererColor.a;
                #else
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv * _ShadowUvTransform.xy + _ShadowUvTransform.zw;
                output.alpha = input.color.a * _Color.a * _RendererColor.a;
                #endif
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float radiusSquared = dot(input.uv, input.uv);
                half softness = max(fwidth(radiusSquared), _WorldSunShadowBlur);
                half alpha = 1.0h - smoothstep(1.0h - softness, 1.0h, radiusSquared);
                return half4(_WorldSunShadowColor.rgb,
                    alpha * input.alpha * _WorldSunShadowColor.a);
            }
            ENDHLSL
        }
    }
}
