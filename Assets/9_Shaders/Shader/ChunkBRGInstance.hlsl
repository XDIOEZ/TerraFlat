// Chunk BatchRendererGroup 共用的 112 字节 AoS 实例布局。
#ifndef FLATWORLD_CHUNK_BRG_INSTANCE_INCLUDED
#define FLATWORLD_CHUNK_BRG_INSTANCE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#if defined(UNITY_DOTS_INSTANCING_ENABLED)
    UNITY_DOTS_INSTANCING_START(UserPropertyMetadata)
        UNITY_DOTS_INSTANCED_PROP(uint, _ChunkInstanceData)
    UNITY_DOTS_INSTANCING_END(UserPropertyMetadata)
#endif

struct ChunkBRGInstanceData
{
    float4 transform0;
    float4 transform1;
    float4 data0;
    float4 data1;
    float4 tint;
    float4 flowX;
    float4 flowY;
};

// 域内原生网格把同一实例的数据写入每个顶点，普通 BRG 继续读取原缓冲。
#if defined(_CHUNK_NATIVE_BATCH)
    #define CHUNK_NATIVE_ATTRIBUTES \
        float4 chunkTransform0 : TEXCOORD1; float4 chunkTransform1 : TEXCOORD2; \
        float4 chunkData0 : TEXCOORD3; float4 chunkData1 : TEXCOORD4; \
        float4 chunkTint : TEXCOORD5; float4 chunkFlowX : TEXCOORD6; float4 chunkFlowY : TEXCOORD7;
    #define LOAD_CHUNK_VERTEX_DATA(input) LoadChunkNativeVertexData(input.chunkTransform0, input.chunkTransform1, \
        input.chunkData0, input.chunkData1, input.chunkTint, input.chunkFlowX, input.chunkFlowY)
    ChunkBRGInstanceData LoadChunkNativeVertexData(float4 transform0, float4 transform1, float4 data0,
        float4 data1, float4 tint, float4 flowX, float4 flowY)
    {
        ChunkBRGInstanceData data;
        data.transform0 = transform0; data.transform1 = transform1;
        data.data0 = data0; data.data1 = data1; data.tint = tint; data.flowX = flowX; data.flowY = flowY;
        return data;
    }
#else
    #define CHUNK_NATIVE_ATTRIBUTES
    #define LOAD_CHUNK_VERTEX_DATA(input) LoadChunkBRGInstanceData()
#endif

ChunkBRGInstanceData LoadChunkBRGInstanceData()
{
    ChunkBRGInstanceData data = (ChunkBRGInstanceData)0;
#if defined(UNITY_DOTS_INSTANCING_ENABLED)
    uint baseAddress = UNITY_DOTS_INSTANCED_METADATA_NAME(uint, _ChunkInstanceData);
    // 调用方必须先执行 UNITY_SETUP_INSTANCE_ID，以建立当前绘制的 DOTS 可见实例映射。
    // unity_InstanceID 只是单次 draw 的局部序号，Unity 拆分大批次后会从零重新计数；
    // 必须使用可见列表映射后的真实实例索引，否则后续地块会重复绘制到批次开头的位置。
    uint instanceIndex = GetDOTSInstanceIndex();
    uint address = baseAddress + instanceIndex * 112u;
    // 使用 URP 统一读取入口，兼容 GLES 常量缓冲与其它后端的原始缓冲。
    data.transform0 = asfloat(DOTSInstanceData_Load4(address));
    data.transform1 = asfloat(DOTSInstanceData_Load4(address + 16u));
    data.data0 = asfloat(DOTSInstanceData_Load4(address + 32u));
    data.data1 = asfloat(DOTSInstanceData_Load4(address + 48u));
    data.tint = asfloat(DOTSInstanceData_Load4(address + 64u));
    data.flowX = asfloat(DOTSInstanceData_Load4(address + 80u));
    data.flowY = asfloat(DOTSInstanceData_Load4(address + 96u));
#else
    data.transform0 = float4(1, 0, 0, 0);
    data.transform1 = float4(0, 1, 0, 0);
    data.tint = 1;
#endif
    return data;
}

float3 TransformChunkBRGVertex(float3 positionOS, ChunkBRGInstanceData data)
{
    float2 positionWS;
    positionWS.x = dot(data.transform0.xy, positionOS.xy) + data.transform0.z;
    positionWS.y = dot(data.transform1.xy, positionOS.xy) + data.transform1.z;
    float3 result = float3(positionWS, data.transform1.w + positionOS.z);
#if defined(_CHUNK_NATIVE_BATCH)
    result = TransformObjectToWorld(result);
#endif
    return result;
}

// 机械动画只变更实例顶点或采样坐标；相位与转速由权威节点低频上传。
float ChunkMechanicalPhase(ChunkBRGInstanceData data)
{
    return data.data0.z + _Time.y * data.data0.y;
}

float3 AnimateChunkMechanicalVertex(float3 positionOS, ChunkBRGInstanceData data)
{
    // 模式 4 仅压缩独立皮革层，木框与喷嘴保留原始顶点。
    if (data.data0.x > 3.5 && data.data0.x < 4.5)
    {
        float compression = (1.0 - cos(ChunkMechanicalPhase(data))) * 0.5 * saturate(data.data0.w);
        positionOS.y *= 1.0 - compression;
        return positionOS;
    }
    if (data.data0.x < 0.5 || data.data0.x > 1.5) return positionOS;
    float angle = ChunkMechanicalPhase(data);
    float sine, cosine;
    sincos(angle, sine, cosine);
    float2 vertexXY = positionOS.xy;
    positionOS.xy = float2(vertexXY.x * cosine - vertexXY.y * sine,
                           vertexXY.x * sine + vertexXY.y * cosine);
    return positionOS;
}

float2 AnimateChunkMechanicalUV(float2 uv, float4 animation, float4 region)
{
    if (animation.x < 1.5 || animation.x > 2.5) return uv;
    float span = max(0.000001, region.y - region.x);
    float localU = (uv.x - region.x) / span;
    float width = max(0.000001, span - region.z * 2.0);
    float phase = animation.z + _Time.y * animation.y;
    uv.x = region.x + region.z + frac(localU + phase / 6.28318530718) * width;
    return uv;
}

#endif
