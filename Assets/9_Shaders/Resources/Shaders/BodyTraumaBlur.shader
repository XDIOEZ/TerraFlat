Shader "Hidden/FlatWorld/BodyTraumaBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float _Radius;
            half4 Frag(Varyings input) : SV_Target
            {
                float2 offset = _Radius * _BlitTexture_TexelSize.xy;
                half4 color = 0;
                for (int row = -1; row <= 1; row++)
                    for (int column = -1; column <= 1; column++)
                        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + float2(column, row) * offset);
                return color / 9;
            }
            ENDHLSL
        }
    }
}
