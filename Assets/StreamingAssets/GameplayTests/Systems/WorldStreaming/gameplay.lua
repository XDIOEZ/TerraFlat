-- 只读正式 ChunkMgr 的窗口和脚下区块，不刷新窗口或额外申请租约。
test.runtime("world_streaming", "live_window", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.runtimeActive and data.world ~= nil and data.epoch > 0, "正式纯世界运行时未建立")
    assert(data.windowReady and data.retainedChunks > 0, "可见窗口没有完成表现绑定")
    assert(data.footFound and data.footData == "Ready" and data.footPresentation == "Bound" and data.footView, "脚下权威数据或真实表现缺失")
    assert(data.footSimulationLeases > 0 and data.footPresentationLeases > 0, "当前窗口区块没有正式使用租约")
    assert(data.footViewVisualOnly and data.footIndependentPhysics,
        "真实ChunkView仍挂有物理组件，或脚下碰撞没有独立交给所属世界物理服务")
end, "真实流送窗口、脚下数据和表现租约")

test.runtime("world_streaming", "incremental_window_roundtrip", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.awayReady and data.awayActive,
        "正式窗口平移后没有完成新区块数据、表现或模拟租约")
    assert(data.forwardVisitedCells > 0 and data.forwardVisitedCells <= 52 and
        data.immediateReturnVisitedCells > 0 and data.immediateReturnVisitedCells <= 52,
        "单格平移或折返仍全量枚举窗口面积")
    assert(data.retargetReturnViewsReady and data.foldedViewReused and data.returnedViewReused and data.returnedDataReused,
        "折返没有复用重叠范围内的真实ChunkView或权威数据")
    assert(data.homeActive and data.homePresentationLease and data.pendingBindings == 0 and
        data.pendingPresentations == 0 and data.idleReconciliationAttempts == 0,
        "回到原窗口后租约未恢复、待办未清空或稳定区块仍被逐帧检查")
    assert(data.worldPreserved and data.playerPreserved and data.restoredWindowReady,
        "窗口回归改变了世界/玩家，或未恢复玩家原配置窗口")
end, "真实窗口边界增量、快速折返、数据表现复用与稳定零待办")

test.runtime("world_streaming", "incremental_world_scope", {}, function(data)
    local scopes = data.executionScopes
    assert(scopes.outerWorld and scopes.innerWorld and scopes.innerMachineWorld and scopes.nestedRestored
        and scopes.exceptionRestored and scopes.repeatedDisposePreservedContext and scopes.contextRestored
        and scopes.machineContextRestored and scopes.scenePreserved,
        "跨世界作用域的嵌套、异常或重复释放没有正确恢复上下文")
    assert(scopes.stable and scopes.iterations == 1024 and scopes.allocatedBytes == 0,
        "真实世界作用域预热后仍产生GC或执行上下文泄漏")
    assert(data.sourceWorld ~= data.targetWorld and data.targetWindowReady and data.targetViewBound
        and data.targetViewWorld == data.targetWorld,
        "地表仍活动时，洞穴真实窗口或 ChunkView 没有在所属世界完成绑定")
    assert(data.contextRestored and data.scenePreserved and data.localPlayerPreserved,
        "跨帧队列泄漏了世界上下文，或后台洞穴准备更换了活动场景/主角")
    for _, entry in ipairs({{name = "completed", count = 2}, {name = "cancelled", count = 1}}) do
        local sample = data[entry.name]
        assert(sample.stepsBeforeYield == 0 and #sample.steps == entry.count and sample.error == "",
            "分帧首让帧、续步次数或正式资源注册失败：" .. entry.name)
        for _, step in ipairs(sample.steps) do
            assert(step.world == data.targetWorld and step.scene == data.targetWorld and step.managerMatches
                and step.ecsSeparate and step.ecsDimension == "cave",
                "自然实体续步没有恢复洞穴的场景、管理器和独立 ECS 世界：" .. entry.name)
        end
        if #sample.steps == 2 then
            assert(sample.steps[2].frame > sample.steps[1].frame, "正式续步没有真正跨帧")
        end
        assert(sample.disposeCount == 1 and sample.disposeWorld == data.targetWorld,
            "正常完成或无效队列取消没有在洞穴上下文唯一释放枚举器：" .. entry.name)
    end
    for _, rejected in ipairs({data.completed.missingOwner, data.completed.replacedOwner}) do
        assert(not rejected.frameAdvanced and rejected.disposeRejected and rejected.routineRetained
            and rejected.stepsAfter == 0 and rejected.worldCountBefore == rejected.worldCountAfter
            and rejected.contextPreserved,
            "已注销或被替换的管理器仍推进/解绑，或重新创建世界/泄漏上下文")
    end
    assert(data.resourcesRemoved, "分帧归属回归没有移除全部自有自然实体")
end, "地表活动时洞穴跨帧绑定、取消释放与失效管理器拒绝")
