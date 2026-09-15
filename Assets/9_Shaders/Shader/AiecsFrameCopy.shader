Shader "Hidden/FlatWorld/AIECS Frame Copy"
{
    Properties { _MainTex("源 Sprite", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            // 用源 Sprite 的实际三角形和 UV 解包，包括 Tight Mesh 与图集旋转。
            Output Vertex(Input input)
            {
                Output output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }
            // 保留完整 RGBA，不做预乘，供后续正常透明混合。
            fixed4 Fragment(Output input) : SV_Target { return tex2D(_MainTex, input.uv); }
            ENDCG
        }
    }
}
