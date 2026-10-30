-- 通过隔离1080p相机实际绘制一万格火场；GPU计时由运行设备决定，可用时采样，不以不可用判失败。
test.runtime("rendering", "world_fire_render_benchmark", {warmupFrames = 12, sampleFrames = 60}, function(data)
    assert(data.assetsReady and data.fireSpriteName and data.fireMaterialShader == "Sprites/Default"
        and data.depthMeshShaderName == "FlatWorld/2D/Chunk Depth Sprite Lit"
        and data.benchmarkScope == "isolated-scene-single-camera-render-texture",
        "火场基准没有使用ChunkView正式资源和隔离相机")
    assert(data.cellCount == 10000 and data.cellsPerChunk == 16 and data.expectedChunkCount == 49,
        "火场现场规模或16×16分组错误")
    assert(data.renderTextureCreated and data.renderTextureWidth == 1920 and data.renderTextureHeight == 1080,
        "火场基准没有创建1920×1080 RenderTexture")
    assert(data.cameraOrthographic and data.cameraContains100By100Grid and data.cameraContainsAllFireMeshBounds,
        "实际相机视锥没有完整覆盖100×100火格和Sprite网格边界")
    assert(data.drawGroupCount == 49 and data.actualEnabledMeshRendererCount == 49
        and data.actualSortingGroupCount == 49 and data.drawVertexCount == 40000
        and data.drawIndexCount == 60000,
        "隔离相机没有渲染49个真实ChunkDepthMesh绘制组")
    assert(data.actualSampleCameraRenderFrames >= data.requestedSampleFrames,
        "正式性能采样帧数被预热帧补足，或隔离火场相机没有完成所需渲染")
    assert(type(data.previewImagePath) == "string" and #data.previewImagePath > 0
        and data.previewImageBytes > 0,
        "1920×1080火场RT没有在采样结束后读回PNG视觉证据")
    assert(data.initialUploads.indexBufferUploadCount == 49 and data.initialUploads.vertexBufferUploadCount == 49
        and data.initialUploads.indexBufferUploadBytes > 0 and data.initialUploads.vertexBufferUploadBytes > 0
        and data.initialUploads.topologyRebuildCount == 49 and data.initialUploads.partCount == 10000
        and data.initialUploads.parentAnchorRowCount == 49
        and data.initialUploads.sourceMaterialMismatchCount == 0
        and data.initialUploads.rendererShaderMismatchCount == 0
        and data.initialUploads.rendererShaderName == data.depthMeshShaderName
        and data.initialUploads.sortingGroupMismatchCount == 0
        and data.initialUploads.meshRendererMismatchCount == 0
        and data.initialUploads.sortingGroupLayerId == data.expectedSortingLayerId
        and data.initialUploads.sortingGroupOrder == data.expectedWorldEffectSortingOrder
        and data.initialUploads.meshRendererLayerId == data.expectedSortingLayerId
        and data.initialUploads.meshRendererOrder == data.expectedFireDepthSortingOrder
        and data.noMeshUploadDuringRender
        and data.renderDeltaUploads.indexBufferUploadCount == 0
        and data.renderDeltaUploads.indexBufferUploadBytes == 0
        and data.renderDeltaUploads.vertexBufferUploadCount == 0
        and data.renderDeltaUploads.vertexBufferUploadBytes == 0
        and data.renderDeltaUploads.topologyRebuildCount == 0,
        "实际网格上传计数不符合初次每区块一次、稳定渲染零重传")

    local function checkMetric(metric, label)
        assert(metric.status == "available" or metric.status == "unavailable", label .. "状态缺失")
        if metric.status == "available" then
            assert(metric.sampleCount > 0, label .. "没有采样帧")
        end
    end
    checkMetric(data.cpuFrameMilliseconds, "CPU整帧计时")
    checkMetric(data.cpuMainThreadMilliseconds, "CPU主线程计时")
    checkMetric(data.cpuRenderThreadMilliseconds, "CPU渲染线程计时")
    checkMetric(data.gpuFrameMilliseconds, "GPU整帧计时")
    checkMetric(data.drawCalls, "实际Draw Calls计数")
    checkMetric(data.gcAllocatedBytes, "GC分配字节计数")
    if data.gpuFrameMilliseconds.status == "available" then
        assert(data.gpuFrameMilliseconds.average > 0, "GPU计时样本必须为正数")
    end
    if data.drawCalls.status == "available" then
        assert(data.drawCalls.maximum > 0, "Profiler报告的Draw Calls为零，隔离相机没有实际绘制")
    end
end, "100×100火场1080p隔离相机实帧、Profiler与网格上传采样")
