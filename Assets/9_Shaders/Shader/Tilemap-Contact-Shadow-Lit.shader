// 地表本体复用统一光照 Pass，高低阴影由 TerrainHeightShadowManager 单独提交。
Shader "Game/2D/Tilemap-Contact-Shadow-Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("Tile Texture", 2D) = "white" {}
        _MaskTex("Light Mask", 2D) = "white" {}
        _FarmlandDryColor("Farmland Dry Soil Color", Color) = (0.67,0.50,0.30,1)
        _FarmlandDryBlend("Farmland Dry Color Strength", Range(0,1)) = 0.55
        _FarmlandWetTint("Farmland Wet Soil Tint", Color) = (0.70,0.76,0.80,1)
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        UsePass "FlatWorld/2D/Chunk Mesh Contact Lit/Universal2D"
        UsePass "FlatWorld/2D/Chunk Mesh Contact Lit/UniversalForward"
    }
}
