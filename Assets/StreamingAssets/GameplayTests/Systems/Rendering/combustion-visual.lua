-- 火焰独立规则，正式熔炉JSON定位与共享资源回归不受其他主题步骤数量影响。
test.system("rendering", "dropped_combustion_material", {}, function(data)
    assert(data.assigned and data.shared and data.sharedMesh, "火焰没有共享正式材质与网格")
    assert(data.shader == "Game/Effects/ShaderFlame" and data.supported and data.passes,
        "火焰缺少可用的 URP 2D / Forward Shader Pass")
    assert(data.pixelsPerUnit == 32 and data.animationFPS == 12,
        "共享火焰没有使用与人物匹配的像素密度和逐帧动画")
    assert(data.vertices == 4 and data.particles == 0, "常驻火焰仍依赖粒子模拟或重复生成复杂网格")
    assert(data.independentPhase, "不同火源使用相同相位，会同步跳动")
    assert(data.width == 2 and data.height == 3 and data.boundsWidth == 2 and data.boundsHeight == 3
        and data.boundsCenterY == 1.5, "Shader 尺寸没有同步 CPU 裁剪边界或火焰底部锚点")
    assert(data.sortingOrder == 37 and data.adapterOrder == 82, "掉落物或设施火焰丢失宿主排序")
    assert(data.startsStopped and data.lights and data.disabled and data.reenabled,
        "火焰初始开关或组件启停错误")
    assert(data.staysExtinguished and data.zeroIntensityHidden and data.finiteBounds,
        "回池后重新点燃、零火势残留，或无效配置污染网格边界")
    assert(data.adapterLights and data.adapterExtinguishes, "燃烧模块没有转发点燃和熄灭状态")
    assert(data.chunkFireMaterialAssigned and data.chunkFireMaterialPath == "Assets/9_Shaders/Material/Chunk-Fire-Unlit.mat"
        and data.chunkFireMaterialShader == "Sprites/Default" and data.chunkFireSpriteAssigned,
        "区块火场没有绑定有效的 Unlit 源材质或燃烧贴图")
    assert(data.chunkFireFrameCount == 8 and data.chunkFireFullRect,
        "区块火场的 8 帧横向图集未使用 FullRect，GPU 切帧可能被首帧裁剪")
    assert(math.abs(data.attachedUpX + math.sin(math.rad(75))) < .0001
        and math.abs(data.attachedUpY - math.cos(math.rad(75))) < .0001,
        "显式跟随锚点的火焰丢失宿主倾斜")
    for _, prefix in ipairs({"uprightUp", "mirroredUp"}) do
        assert(math.abs(data[prefix .. "X"]) < .0001 and math.abs(data[prefix .. "Y"] - 1) < .0001
            and math.abs(data[prefix .. "Z"]) < .0001, "直立火焰继承了手持倾斜或左右翻面")
    end
    local smelter = data.smelter
    assert(smelter.enabled and smelter.followRenderer, "熔炉火焰没有跟随正式主体配置")
    assert(smelter.spriteAddress == "Assets/6_Art/Generated/Building/Smelter/Smelter_Unlit.png[Smelter_Unlit]",
        "熔炉炉口像素断言需要按新贴图重新核对")
    assert(smelter.anchor.x == 0 and smelter.anchor.z == 0, "熔炉火焰没有水平居中或偏离主体平面")
    -- 正式熔炉贴图为16×16、16PPU、中心原点，炉口底沿上表面位于底部第2像素。
    assert(math.abs(smelter.anchor.y * 16 + 8 - 2) < .0001,
        "熔炉火焰底部没有贴齐炉口内底面（底部像素Y=2）")
end, "Shader 火焰共享资源、生命周期与熔炉炉口定位")

test.system("rendering", "world_fire_batches", {}, function(data)
    assert(data.assetsReady and data.fireSpriteName and data.fireMaterialShader == "Sprites/Default"
        and data.depthMeshShaderName == "FlatWorld/2D/Chunk Depth Sprite Lit",
        "100×100区块火场采样没有加载 ChunkView 的正式 Sprite 与 Unlit 材质")
    assert(data.cellCount == 10000 and data.cellsPerChunk == 16 and data.expectedChunkCount == 49,
        "火场采样尺寸没有覆盖一万格与16×16区块边界")
    local initial = data.initial
    assert(initial.partCount == 10000 and initial.drawGroupCount == 49 and initial.rowCount == 49
        and initial.actualEnabledMeshRendererCount == 49 and initial.actualSortingGroupCount == 49,
        "每区块火场没有收敛到一个真实 MeshRenderer 绘制组")
    assert(initial.drawVertexCount == 40000 and initial.drawIndexCount == 60000,
        "真实区块 Mesh 的绘制顶点/索引数量与一万张 FullRect 四边形不符")
    assert(initial.parentAnchorRowCount == 49 and initial.sortingGroupMismatchCount == 0
        and initial.meshRendererMismatchCount == 0 and initial.sourceMaterialMismatchCount == 0
        and initial.rendererShaderMismatchCount == 0 and initial.rendererShaderName == data.depthMeshShaderName,
        "火场区块批没有沿用父排序锚、WorldEffect 层、源材质或深度合批 Shader")
    assert(initial.sortingGroupLayerId == data.expectedSortingLayerId
        and initial.sortingGroupOrder == data.expectedWorldEffectSortingOrder
        and initial.meshRendererLayerId == data.expectedSortingLayerId
        and initial.meshRendererOrder == data.fireDepthSortingOrder,
        "实际 SortingGroup/MeshRenderer 排序键不符合 world-effect + 火格深度顺序")
    assert(initial.indexBufferUploadCount == 49 and initial.indexBufferUploadBytes > 0
        and initial.vertexBufferUploadCount == 49 and initial.vertexBufferUploadBytes > 0
        and initial.topologyRebuildCount == 49,
        "初次真实 Mesh 缺少每区块一次的索引重建和顶点上传")

    local unchanged = data.unchanged
    assert(data.noUploadForUnchangedVisual and data.meshInstancesStableAfterUnchanged
        and data.indicesStableAfterUnchanged and unchanged.indexBufferUploadCount == initial.indexBufferUploadCount
        and unchanged.vertexBufferUploadCount == initial.vertexBufferUploadCount,
        "相同火格重提仍上传了顶点/索引或重建区块网格")
    local incremental = data.incremental
    assert(data.tintWasIncrementalVertexUpload and data.tintChangedUploadedVertexColor
        and data.meshInstancesStableAfterTint and data.indicesStableAfterTint
        and incremental.drawGroupCount == initial.drawGroupCount
        and incremental.topologyRebuildCount == unchanged.topologyRebuildCount
        and incremental.indexBufferUploadCount == unchanged.indexBufferUploadCount
        and incremental.vertexBufferUploadCount == unchanged.vertexBufferUploadCount + 1
        and incremental.vertexBufferUploadBytes > unchanged.vertexBufferUploadBytes,
        "单火色彩变化没有只增量上传顶点，或意外重建了索引/绘制组")
    assert(data.fireAnimationMode == 5 and data.fireFrameCount == 8 and data.fireFramesPerSecond == 12,
        "区块火场 GPU 动画参数没有保持八帧、12 FPS")

    local display = data.displayEntry
    assert(display.movementCellAvailable and display.firePresentAtSameCell and display.initialFireIntensity > 0
        and display.independentFlameSuppressedInSameCell,
        "真实掉落物 Present 没有抑制同格火场下的独立火焰")
    assert(display.flameRestoredAfterMoveAway and display.fireExtinguished and display.remainingFireIntensity == 0
        and display.flameRestoredAfterExtinguish and display.worldModelReleased,
        "真实掉落物离开火格或火场熄灭后没有恢复火焰，或测试世界未释放")
end, "一万火格真实区块Mesh批、上传增量与掉落物火场遮蔽恢复")
