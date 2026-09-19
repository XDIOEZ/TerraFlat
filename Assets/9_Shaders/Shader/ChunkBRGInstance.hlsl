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

ChunkBRGInstanceData LoadChunkBRGInstanceData()
{
    ChunkBRGInstanceData data = (ChunkBRGInstanceData)0;
#if defined(UNITY_DOTS_INSTANCING_ENABLED)
    uint baseAddress = UNITY_DOTS_INSTANCED_METADATA_NAME(uint, _ChunkInstanceData);
    uint address = baseAddress + unity_InstanceID * 112u;
    data.transform0 = asfloat(unity_DOTSInstanceData.Load4(address));
    data.transform1 = asfloat(unity_DOTSInstanceData.Load4(address + 16u));
    data.data0 = asfloat(unity_DOTSInstanceData.Load4(address + 32u));
    data.data1 = asfloat(unity_DOTSInstanceData.Load4(address + 48u));
    data.tint = asfloat(unity_DOTSInstanceData.Load4(address + 64u));
    data.flowX = asfloat(unity_DOTSInstanceData.Load4(address + 80u));
    data.flowY = asfloat(unity_DOTSInstanceData.Load4(address + 96u));
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
    return float3(positionWS, data.transform1.w + positionOS.z);
}

#endif
