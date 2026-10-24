-- 动作在外圈生效，移动只在更小的内圈停止；禁止再次把到达和进食/攻击距离合并。
for _, case in ipairs({
    {range = .9, preferred = .3, stop = .3},
    {range = .55, preferred = .5, stop = .275},
    {range = .45, preferred = .2, stop = .2},
    {range = .1, preferred = 5, stop = .05},
    {range = .005, preferred = .5, stop = .0025}
}) do
    test.system("ai", "approach_ranges", {actionRange = case.range, stopDistance = case.preferred}, function(data)
        assert(math.abs(data.stopDistance - case.stop) < .00001 and data.stopDistance < data.actionRange,
            "追近停止距离没有独立收紧到动作范围内")
        assert(math.abs(data.movingTimer - .3) < .00001 and data.outsideTimer == 0,
            "接近中清零动作计时，或目标脱离范围后仍远程进食")
        assert(math.abs(data.clampedStep - (case.range - case.stop)) < .00001 and data.innerStep == 0,
            "低帧率步长越过停止内圈，或已进入内圈仍向前移动")
        assert(data.invalidStop < data.actionRange and not data.invalidAction,
            "非法配置/距离绕过了动作范围判定")
    end, "追近与动作距离解耦：" .. case.range)
end

-- 狼群使用最终定义的真实能力身份，重复候选不加人数，并保留独狼避让和双狼残血撤退。
test.system("ai", "wolf_formation_topology", {}, function(data)
    assert(data.cases == 7 and data.wrappedDeltas and data.infiniteDeltas,
        "狼群站位共用拓扑后改变了环绕接缝、负方向或半周期最短位移")
    assert(data.normalized and data.infiniteUnchanged,
        "阵型位置没有正确归一化或误改 Z/无限世界坐标")
    assert(data.laneSeamRight and data.laneSeamLeft and data.unwrappedLaneLeft and
        data.laneTieParity and data.laneNearTie,
        "站位左右翼选择在环绕接缝、直线距离或近重合时改变")
end, "狼群站位批次冻结拓扑，环绕位移、归一化和左右翼分配等价")

for _, case in ipairs({
    {name = "四狼互认并响应支援接口", args = {count = 4, callOnly = true}, members = 4, state = "Chase"},
    {name = "三狼主动追击", args = {count = 3}, members = 3, state = "Chase"},
    {name = "双狼主动追击", args = {count = 2}, members = 2, state = "Chase"},
    {name = "独狼保持避让", args = {count = 1}, members = 1, state = "Avoid"},
    {name = "双狼残血撤退", args = {count = 2, hpRate = .1}, members = 2, state = "Flee"},
    {name = "三狼残血仍成群作战", args = {count = 3, hpRate = .1}, members = 3, state = "Chase"},
    {name = "攻击站位内选择攻击", args = {count = 4, playerX = 1.05}, members = 4, state = "Attack", firstOnly = true}
}) do
    test.system("ai", "wolf_pack", case.args, function(data)
        assert(data.declaredId == "AI" and data.prefabId == "Module_AI_Wolf" and data.identityBound,
            "正式狼将具体Prefab身份误当AI能力，真实注册后同伴查询失败")
        assert(data.wrongIdRejected, "错误能力ID被默认值掩盖，身份反例无效")
        for index, members in ipairs(data.packCounts) do
            assert(members == case.members and data.leaders[index] == 101,
                "狼群漏认、重复累计成员，或没有选出同一名稳定头狼")
            assert(data.validatedAllyCounts[index] == members - 1,
                "本轮有效同伴快照与群体人数不一致")
            assert(data.threats[index] == 200, "狼没有从检测或同伴呼叫获得同一个玩家目标")
            if not case.firstOnly or index == 1 then
                assert(data.states[index] == case.state, "狼群人数与正式追击、攻击或避让选择不符")
            end
        end
    end, "狼群协作：" .. case.name)
end
for _, exclusion in ipairs({"dead", "disabled", "unready", "foreign"}) do
    test.system("ai", "wolf_pack", {count = 4, exclude = exclusion, callOnly = true}, function(data)
        for index = 1, 3 do
            assert(data.packCounts[index] == 3 and data.leaders[index] == 101,
                "死亡、禁用、未就绪或敌对阵营的狼被算作有效同伴")
            assert(data.validatedAllyCounts[index] == 2,
                "无效同伴仍进入本轮复用快照")
            assert(data.states[index] == "Chase" and data.threats[index] == 200,
                "拒绝无效成员时破坏了剩余狼群的共同追击")
        end
        assert(data.threats[4] == 0, "无效成员仍收到同伴支援目标")
    end, "狼群排除无效同伴：" .. exclusion)
end

test.system("ai", "wolf_target_memory", {}, function(data)
    assert(data.visibleState == "Chase" and data.initialMemoryX == 15,
        "看到玩家后没有追击或未记录真实位置")
    assert(data.hiddenState == "Search" and data.memoryFrozen and data.travellingMemoryRetained,
        "丢失视野后立即停追，或旧检测候选把隐藏目标位置泄露给狼")
    assert(data.hiddenNearbyState == "Search" and data.hiddenNearbyMemoryFrozen,
        "隐藏目标靠近后狼隔墙攻击或偷偷更新记忆")
    assert(data.arrivalWaitStarted and data.waitRetained and data.waitExpired and data.afterWaitState == "Idle",
        "狼到达最后位置后没有限时搜索，或搜索结束没有清理目标")
    assert(data.reacquiredState == "Chase" and data.reacquiredDuringTravelState == "Chase" and
        data.reacquiredMemoryUpdated and data.reacquiredDuringWaitState == "Chase" and data.reacquiredWaitCleared,
        "赶往记忆点或到点等待时重新看见玩家没有恢复追击")
    assert(data.travelExpired and data.reusedIdentityCleared and data.commandCancelCleared and data.runtimeResetCleared,
        "搜索超时、实例身份变化、取消指令或运行态重置后仍残留旧目标")
    assert(data.reportedState == "Search" and data.reportedMemoryFrozen and data.unseenCallerIgnored,
        "同伴情报没有冻结已知坐标，或失去视野的同伴不断传播未知目标")
    assert(data.triggerBeforeDecisionState == "Search" and data.triggerBeforeDecisionMemoryFrozen,
        "集火确认到首次决策之间丢失视野，狼仍然立即忘记目标")
    assert(data.callBeforeDecisionState == "Search" and data.callBeforeDecisionMemoryFrozen,
        "收到可见目标的支援呼叫后首次决策前丢视野，狼仍然立即忘记目标")
end, "狼丢失视野后追到最后位置、搜索、重新发现和安全清理")

-- 所有正式物种都要在最终继承配置中启用单图，新增控制器只提供能力而不额外画身体。
test.system("ai", "single_sprite_catalog", {}, function(data)
    assert(#data.actors >= 12, "正式 Actor Manifest 缺少已有物种")
    local ghost, fish, bird, bee = false, false, false, false
    local snowLeopard, zombie = false, false
    for _, actor in ipairs(data.actors) do
        assert(actor.hasAnimatorModule and actor.hasControllerPrototype and actor.enabled and actor.visualMode == 1,
            "正式物种没有启用可装配的单图动画：" .. actor.id)
        assert(actor.initialSpriteAddress ~= "" and actor.hasInitialSprite and actor.initialSpriteName ~= "",
            "摇摆动画缺少不依赖 Animator 的静态首帧：" .. actor.id .. "：" .. actor.initialSpriteError)
        local compiled = actor.runtimeSprite
        assert(compiled.isActor and compiled.sameSprite and compiled.spriteName == actor.initialSpriteName,
            "Actor 运行时定义丢弃或替换了已加载的首帧图片：" .. actor.id)
        assert(compiled.missingRejected and compiled.nullRejected and compiled.absentAddressRejected
            and compiled.missingError ~= "" and compiled.inactive,
            "Actor 编译接纳了缺失、空引用或未配置的首帧，或规则意外加载了世界：" .. actor.id)
        if actor.id == "SnowLeopard" then
            assert(actor.initialSpriteName == "MiniWolf_Idle_1", "雪豹没有绑定自己的静态首帧")
            snowLeopard = true
        elseif actor.id == "Zombie" then
            assert(actor.initialSpriteName == "Character_Zombie_Idle_Right", "僵尸错误复用了狼外壳的图片")
            zombie = true
        end
        assert(actor.stableName == "animator" and actor.moduleId == data.requiredAnimationId and actor.prefabId == actor.declaredPrefabId,
            "动画槽位、能力ID或具体Prefab身份不符合正式依赖契约：" .. actor.id)
        assert(actor.requiredCount == 1 and actor.registeredIdCount == 1 and actor.requiredSameInstance and not actor.requiredRejected,
            "动画能力真实注册后无法被RequireSingle唯一取到：" .. actor.id)
        assert(actor.slotSameInstance and actor.dataBoundToItem, "动画数据或稳定槽位没有绑定真实临时Item：" .. actor.id)
        assert(math.abs(actor.deathDuration - .76) < .0001 and math.abs(actor.deathJumpHeight - .35) < .0001,
            "正式物种缺少统一翻肚皮动作参数：" .. actor.id)
        assert(actor.deathHealth.present and actor.deathHealth.hasPrototype and actor.deathHealth.enabled,
            "正式物种缺少可触发死亡动画的生命模块：" .. actor.id)
        assert(actor.deathHealth.destroyDelay > actor.deathDuration + .2,
            "正式物种在翻转落地和短暂停留前就销毁：" .. actor.id)
        local wrong = actor.wrongIdRegistration
        assert(wrong.moduleId == "test:wrong_animation" and wrong.stableName == "animator" and wrong.prefabId == actor.declaredPrefabId,
            "显式错误ID被Canonical自动纠正，身份反例失去作用：" .. actor.id)
        assert(wrong.requiredCount == 0 and wrong.registeredIdCount == 1 and wrong.requiredRejected and not wrong.requiredSameInstance,
            "错误动画能力ID仍被RequireSingle接纳：" .. actor.id)
        assert(wrong.slotSameInstance and wrong.dataBoundToItem and wrong.requiredError ~= "",
            "错误ID反例未完成真实绑定注册或缺少精确查询拒绝：" .. actor.id)
        if actor.id == "Bird" or actor.id == "Bee" then
            local flight = actor.flightDependencies
            assert(flight.moduleCount == 7 and flight.allBound and flight.allEnabled,
                "真实飞行外壳没有按最终继承配置注册七个依赖模块：" .. actor.id)
            assert(flight.bound and flight.singleSprite and flight.animationCount == 1,
                "鸟或蜜蜂正式BindModuleDependencies无法绑定唯一动画能力：" .. actor.id)
            assert(flight.noneLoaded and flight.inactive,
                "飞行依赖规则不应激活外壳或执行模块OnLoad：" .. actor.id)
            assert(flight.wrongIdRejected and flight.wrongIdCount == 1 and flight.requiredCountAfterWrongId == 0
                and string.find(flight.wrongIdError, data.requiredAnimationId, 1, true),
                "飞行模块接纳了错误动画ID，或未在真实依赖查询处明确拒绝：" .. actor.id)
            bird = bird or actor.id == "Bird"
            bee = bee or actor.id == "Bee"
        end
        if actor.id == "Ghost" or actor.id == "SmallFish" then
            assert(actor.bodyRendererCount == 1 and actor.moduleRendererCount == 0,
                "幽灵或鱼的新增动画模块重复创建了身体 Renderer：" .. actor.id)
            assert(actor.moduleColliderCount == 0 and actor.moduleBodyCount == 0,
                "纯动画控制器带入了额外碰撞体或刚体：" .. actor.id)
            ghost = ghost or actor.id == "Ghost"
            fish = fish or actor.id == "SmallFish"
        end
    end
    assert(ghost and fish and bird and bee, "单图动画目录缺少幽灵、小鱼、鸟或蜜蜂")
    assert(snowLeopard and zombie, "单图首帧回归缺少雪豹或共享狼外壳的僵尸")
end, "正式全物种单图模式与幽灵小鱼纯控制器")

test.system("ai", "single_sprite_aquatic", {}, function(data)
    for _, sample in ipairs({data.swimming, data.stranded, data.carried}) do
        assert(sample.singleSprite and sample.positionError < .00001 and sample.rotationError < .001,
            "鱼单图接管后旧游动或搁浅表现仍直接叠加原节点姿态")
        assert(sample.rootPositionError < .00001, "鱼外观表现改动了权威根位置")
    end
    assert(data.swimming.swimming and data.swimming.height == 0 and data.swimming.roll == 0,
        "真实水生游动没有提供主动动画信号")
    assert(not data.stranded.swimming and math.abs(data.stranded.height - .24) < .00001 and data.stranded.roll == 18,
        "搁浅的已有高度倾角没有交给共用动画")
    assert(not data.carried.swimming and math.abs(data.carried.height - .4) < .00001 and data.carried.roll == 0,
        "被携带的鱼误播游动或丢失已有外观高度")
    assert(not data.animatorSwimming.singleSprite and data.animatorSwimming.swimming and data.animatorSwimming.rotationError > .1,
        "鱼切回原模式后独立游动表现没有恢复")
end, "鱼单图接管不双叠旧摆动且保留搁浅携带信号")

test.system("ai", "passable_building_sight", {}, function(data)
    assert(data.catalogCount >= 46 and data.extensionCount == 23 and
        #data.samples == data.catalogCount + data.extensionCount, "没有采样完整设备目录及各层通行扩展")
    local sampled = {}
    for _, sample in ipairs(data.samples) do
        assert(not sampled[sample.id], "设备视线样本身份重复：" .. sample.id)
        sampled[sample.id] = sample
        local expectedBlocking = sample.blocksMovement and sample.layer ~= 2 and sample.layer ~= 6
        assert(sample.blocksTraversal == expectedBlocking and sample.blocksSight == expectedBlocking and sample.visible ~= expectedBlocking,
            "可通行设施挡住视线，或实体设备失去遮挡：" .. sample.id)
        assert(not sample.wallVisible and sample.afterWallRemovedVisible == sample.visible,
            "同格覆盖层抹掉地形墙，或拆墙后未恢复设备自身规则：" .. sample.id)
    end
    for _, id in ipairs({"Conveyor_Wood", "Conveyor_Cloth", "Shaft_Wood", "Wire_Copper", "Wire_Hidden",
        "FluidPipe", "FluidValve", "FluidOutlet", "GasSelector", "PressureProbe_Mechanical",
        "PressureProbe_Electronic", "ElectricLightBulb"}) do
        local sample = sampled[id]
        assert(sample and not sample.blocksMovement and not sample.blocksSight and sample.visible,
            "正式可通行物品漏配或被当成视线障碍：" .. id)
    end
    for layer = 0, 6 do
        local sample = sampled["test:passable_layer_" .. layer]
        assert(sample and sample.visible and not sample.blocksSight,
            "不同名称或覆盖层的同类扩展没有继承通用透视规则：" .. layer)
    end
    for layer = 0, 15 do
        local sample = sampled["test:solid_layer_" .. layer]
        local expected = layer ~= 2 and layer ~= 6
        assert(sample and sample.blocksSight == expected and sample.visible ~= expected,
            "不同占地层的实体或地板没有遵守统一阻挡规则：" .. layer)
    end
    assert(data.removedVisible and not data.terrainWallVisible,
        "动态设施移除没有恢复视线，或真实地形墙被错误放行")
end, "全目录可通行设施不挡视线，各层扩展不靠名称特判，实体和叠加墙保留遮挡")

test.system("ai", "unified_building_obstruction", {}, function(data)
    assert(#data.samples == 32, "未覆盖全部16层的可通行与实体声明")
    for _, sample in ipairs(data.samples) do
        local expected = sample.declared and sample.layer ~= 2 and sample.layer ~= 6
        assert(sample.gameObjectBlocking == expected and sample.compiledBlocking == expected and
            sample.indexedBlocking == expected and sample.sightBlocking == expected,
            "普通建筑、数据机器、导航和视线发生分歧：" .. sample.layer)
        assert(sample.compiledLayer == sample.layer and sample.highestLayer == sample.layer and sample.placementRetained,
            "数据化丢失占地层，或可通行建筑失去放置占格")
    end
    assert(data.stackedBlocked and data.overlayRemovedBlocked,
        "可通行覆盖层或移除覆盖层抹掉了同格实体阻挡")
    assert(data.solidRemovedClear and data.emptyClear and data.emptyHighestLayer == -1,
        "实体移除后残留阻挡，或误删同格可通行设施")
end, "普通建筑与数据机器统一通行遮挡，覆盖全部层及同格增删")

test.system("ai", "line_of_sight_batch", {}, function(data)
    assert(not data.unknownSameCell and data.loadedSameCell and data.blockedEndpointsIgnored,
        "视线起终点加载资格或同格/端点阻挡豁免改变")
    assert(data.crossChunkClear and data.secondRayClear,
        "已加载的跨区块空地没有保持可见")
    assert(data.verticalPositiveClear and data.verticalNegativeClear and
        not data.verticalIntermediateBlocked and data.verticalEndpointIgnored,
        "纵向视线在正负方向、跨区块、遮挡和终点豁免上与原规则不一致")
    assert(not data.verticalBlockedBeforeUnknown and data.verticalSkippedUnknownLookup and
        not data.verticalUnknownEndpoint,
        "纵向视线被遮挡后仍查询未知终点，或让未知区块可见")
    assert(data.horizontalNegativeClear and data.wrappedVerticalSeamClear,
        "负坐标横向视线或环绕纵向接缝漏查真实加载状态")
    assert(data.firstLookupCount == 2 and data.reusedLookupCount == 2 and data.cachedChunkCount == 2,
        "同轮不同射线没有复用两个已解析区块")
    assert(data.allocatedBytes == 0, "预热后的同轮 LOS 仍分配托管内存")
    assert(not data.dynamicBlocked and data.dynamicRestored,
        "同轮建筑占地变化仍读取旧的遮挡结果")
    assert(not data.terrainBlocked and data.terrainRestored and data.terrainRevisionAdvanced,
        "同轮地形阻挡版本变化没有立即生效")
    assert(data.diagonalClear and not data.diagonalHorizontalBlocked
        and not data.diagonalVerticalBlocked and not data.diagonalCenterBlocked,
        "对角视线漏过侧格拐角或中心阻挡")
    assert(not data.unknownEndpoint and not data.unknownIntermediate and data.reboundChunkRestored,
        "未加载端点/途中格没有阻挡，或同地址区块重绑仍读旧数据")
    assert(not data.blockedBeforeUnknownEndpoint and data.blockedBeforeUnknownSkippedEndpointLookup
        and data.unknownEndpointChecked,
        "被墙截断的射线仍多查终点，或无遮挡时漏查未知终点")
    assert(not data.generatingChunkBlocked and data.replacedTerrainRestored,
        "生成中的区块仍参与视线，或新地形提交没有恢复")
    assert(not data.oldObserverBlocked and data.newObserverClear,
        "新一轮没有使用观察者当前真实位置")
    assert(not data.otherDimensionBlocked and data.originalDimensionClear,
        "不同维度复用了旧区块寻址")
    assert(not data.staleEpochBlocked and data.newEpochClear,
        "世界代际变化后旧 LOS 上下文仍有效")
    assert(data.wrappedShortestClear and data.wrappedEquivalentClear
        and not data.wrappedSeamBlocked and data.wrappedSeamRestored,
        "环绕最短路径、等价坐标或接缝实时阻挡改变")
    assert(data.clearedChunkCount == 0 and not data.disposedQueryBlocked and not data.disposedWorldBlocked,
        "本轮结束或世界释放后仍保留旧视线资格")
    assert(data.customNegativeChunksClear, "自定义矩形区块或负坐标寻址错误")
end, "同轮 LOS 寻址复用、实时遮挡、边界、环绕、代际与清理")

test.system("ai", "hive_resident_schedule", {}, function(data)
    assert(data.preparedCount == 3 and #data.intended == 3, "分帧前没有保存完整的三名初始成员意图")
    for index, guid in ipairs({101, 102, 103}) do
        assert(data.intended[index].guid == guid and data.intended[index].pending, "成员意图缺少唯一身份或待初始化状态")
    end
    assert(data.generatedCount == 5 and data.originalHoney == 6, "重复维护重新分配了初始成员或重抽蜂蜜")
    local expected = {true, false, true, true, false, true}
    for index, accepted in ipairs(expected) do
        assert(data.admissions[index] == accepted, "普通出巢没有按帧限额或护巢被限额阻止")
    end
    assert(data.pendingAfterReload and data.sleepingAfterReload and data.firstGuidAfterReload == 101 and data.resumedCount == 3,
        "创建未完成时保存重载丢失成员意图、睡眠状态或身份")
    assert(data.failedAttemptAdmitted and not data.sameFrameRetry and data.nextFrameRetry,
        "失败尝试在同帧重复执行或不能在下帧重试")
    assert(not data.completedPending and not data.completedSleeping and data.completedSatiety == 321 and data.secondStillPending,
        "成功提交没有清待生成/睡眠状态或误改其它待出巢成员")
    assert(data.remainingCount == 2 and data.survivors[1] == 102 and data.survivors[2] == 103,
        "已登记初始成员死亡后被免费补足或改变剩余成员身份")
end, "蜂巢分帧出巢的持久意图、配额与即时护巢")

test.system("ai", "bee_target_search", {}, function(data)
    assert(data.defenseBecameSearch and data.travelsToMemory and data.remainingAfterTravel == 8,
        "护巢追击丢失目标后没有保留目标记忆并赶往最后位置")
    assert(data.firstTravelDistance >= .75 and data.firstSearchRadius <= data.lockRadius + .001,
        "到达最后位置后停在原地，或搜索点越出记忆点附近")
    assert(data.waypointRetained and data.nextTravelDistance >= .75,
        "搜索点每帧乱跳，或到点后没有继续巡飞搜索")
    assert(data.memoryFrozen and data.stillSearching and data.negativeTimeIgnored and data.nightKeepsSearch,
        "搜索泄露隐藏目标位置、被夜间归巢抢占，或倒计时被反向推进")
    assert(string.find(data.debugState, "警戒搜索", 1, true), "搜索时头顶没有显示真实警戒状态")
    assert(data.reacquiredClearsSearch, "重新锁定目标后残留旧搜索目的地或倒计时")
    assert(data.timeoutClearsSearch and data.resetClearsSearch, "搜索到期或回池后残留旧目标及巡飞状态")
end, "蜜蜂护巢丢失目标后持续警戒巡飞、重锁及清理")

-- 正式水生线路保持原小段采样，连续相同液体格只查询一次。
for _, case in ipairs({
    { name = "同格移动", args = { targetX = .9 }, accepted = true, cells = {{x=0,y=0}} },
    { name = "跨三格连续水域", args = {}, accepted = true, cells = {{x=0,y=0},{x=1,y=0},{x=2,y=0}} },
    { name = "不能跳过岸边或未知格", args = { blockedCells = {{x=1,y=0}} }, accepted = false, cells = {{x=0,y=0},{x=1,y=0}} },
    { name = "起点未知立即拒绝", args = { blockedCells = {{x=0,y=0}} }, accepted = false, cells = {{x=0,y=0}} },
    { name = "负坐标向下取格", args = { originX = -.9, targetX = -.1 }, accepted = true, cells = {{x=-1,y=0}} },
    { name = "负坐标跨零格", args = { originX = -.1, targetX = .1 }, accepted = true, cells = {{x=-1,y=0},{x=0,y=0}} },
    { name = "零距离仍读起点", args = { targetX = .1 }, accepted = true, cells = {{x=0,y=0}} },
    { name = "环绕接缝取最短水路", args = { originX = 3.8, targetX = -3.8, wrapped = true }, accepted = true, cells = {{x=3,y=0},{x=-4,y=0}} },
    { name = "环绕接缝不能穿未知水格", args = { originX = 3.8, targetX = -3.8, wrapped = true, blockedCells = {{x=-4,y=0}} }, accepted = false, cells = {{x=3,y=0},{x=-4,y=0}} },
    { name = "镜像起点归一化", args = { originX = 8.1, targetX = 8.9, wrapped = true }, accepted = true, cells = {{x=0,y=0}} }
}) do
    test.system("ai", "aquatic_traversal", case.args, function(data)
        assert(data.accepted == case.accepted, "水生线路可达结果错误：" .. case.name)
        assert(data.queries == #case.cells and #data.visits == #case.cells, "水生线路重复查询同格或漏查跨格：" .. case.name)
        for index, cell in ipairs(case.cells) do
            assert(data.visits[index].x == cell.x and data.visits[index].y == cell.y, "水生线路采样规范格错误：" .. case.name)
        end
        assert(not data.acceptedAfterChange and data.queriesAfterChange > 0, "水域变化后仍复用上一次线路缓存：" .. case.name)
    end, "水生线路查询去重：" .. case.name)
end

-- 同一条同步前视弧线保留原采样点，仅跨分段合并连续相同规范格。
for _, case in ipairs({
    {name = "同格连续分段", chunks = {{x=0,y=0}}, segments = {{x=.4,y=.1},{x=.7,y=.1},{x=.9,y=.1}}, results = {true,true,true}, queries = 1},
    {name = "跨格不能漏查", chunks = {{x=0,y=0},{x=2,y=0}}, segments = {{x=.9,y=.1},{x=1.3,y=.1},{x=2.3,y=.1}}, results = {true,true,true}, queries = 3},
    {name = "未知块拒绝后仍可返回", chunks = {{x=0,y=0}}, segments = {{x=.9,y=.1},{x=2.1,y=.1},{x=.1,y=.1}}, results = {true,false,true}, queries = 4},
    {name = "负坐标矩形区块", originX = -2.9, chunkWidth = 3, chunkHeight = 2, chunks = {{x=-3,y=0},{x=0,y=0}}, segments = {{x=-2.1,y=.1},{x=-.1,y=.1},{x=.1,y=.1}}, results = {true,true,true}, queries = 4},
    {name = "环绕接缝", originX = 3.7, wrapped = true, chunks = {{x=2,y=0},{x=-4,y=0}}, segments = {{x=3.9,y=.1},{x=-3.9,y=.1},{x=-3.6,y=.1}}, results = {true,true,true}, queries = 2}
}) do
    test.system("ai", "flight_traversal_batch", case, function(data)
        assert(#data.results == #case.results and data.batchQueries == case.queries,
            "鸟前视弧线重复查询连续同格或漏查跨格：" .. case.name)
        for index, accepted in ipairs(case.results) do
            assert(data.results[index] == accepted, "鸟前视弧线改变了原分段通行：" .. case.name)
        end
        assert(data.freshBeforeChange and not data.freshAfterEviction and data.freshAfterReload,
            "鸟实际位移复核沿用了前视缓存或看不到区块卸载重载：" .. case.name)
        assert(not data.oldEpochAccepted and not data.disposedAccepted,
            "鸟实际位移复核接受了旧世界代际或已释放世界：" .. case.name)
    end, "鸟前视弧线查询复用：" .. case.name)
end

test.system("ai", "flight_hitbox_geometry_cache", {}, function(data)
    assert(data.same and data.restored, "飞行受击盒相对几何不变时没有复用已有形状")
    assert(data.differentSprite and data.moved and data.flippedX and data.flippedY,
        "换图、相对位置或镜像变化没有强制刷新受击盒")
end, "飞行受击盒只在真实视觉几何变化时重算，持续同步物理姿态")

-- 自然刷新读取正式目录，独立概率落空也要消耗排队机会。
test.system("ai", "ecology_config", {}, function(data)
    assert(data.fishSafeMin == -26.5 and data.fishSafeMax == 63.5, "鱼的冷热安全范围没有按确认值扩大")
    assert(data.fishWaterOnly, "小鱼自然生成不再限制水域")
    local biomes = {}
    for _, name in ipairs(data.fishAllowedBiomes) do biomes[name] = true end
    assert(#data.fishAllowedBiomes == 2 and biomes["海洋"] and biomes["河流"],
        "小鱼自然生成必须同时覆盖海洋和河流，且不能扩张到所有液体")
    assert(data.chickenChance == .5 and data.chickenWeight == 2.5, "小鸡减半概率改变了原物种权重")
    assert(data.boarChance == 1 and math.abs(data.boarWeight - .2631579) < .000001,
        "小鸡减半影响了野猪默认概率或原权重")
    assert(data.defaultChance == 1, "未配置独立概率的物种没有保持全通过")
end, "自然刷新：小鸡独立机会减半，野猪与权重保持原样")
for _, weight in ipairs({0, 1, 70, 10000}) do
    test.system("ai", "fish_current", {weight = weight}, function(data)
        assert(not data.receivesCurrent, "鱼没有声明免受表层水流推动的能力")
        assert(data.fishSpeed == 0 and data.passiveVelocityX == 0 and data.passiveVelocityY == 0,
            "鱼的水流推动仍随体重产生位移")
        assert(math.abs(data.chickenSpeed - .54) < .000001 and math.abs(data.wolfSpeed - .54) < .000001 and data.defaultSpeed > 0,
            "鱼的水流免疫误改了其他生物的通用推动规则")
    end, "鱼水流免疫：体重 " .. weight)
end
for _, case in ipairs({
    {chance = .5, sample = 0, accepted = true}, {chance = .5, sample = .4999, accepted = true},
    {chance = .5, sample = .5, accepted = false}, {chance = .5, sample = 1, accepted = false},
    {chance = 0, sample = 0, accepted = false}, {chance = 0, sample = 1, accepted = false},
    {chance = 1, sample = 0, accepted = true}, {chance = 1, sample = 1, accepted = true}
}) do
    test.system("ai", "spawn_chance", {chance = case.chance, sample = case.sample}, function(data)
        assert(data.accepted == case.accepted, "正式独立概率判定边界错误")
    end, "自然刷新概率边界：" .. case.chance .. "/" .. case.sample)
end
for _, case in ipairs({
    {replacement = false, spawned = false, pending = 1, replacements = 2, lifetime = 4},
    {replacement = true, spawned = false, pending = 0, replacements = 1, lifetime = 4},
    {replacement = false, spawned = true, pending = 1, replacements = 2, lifetime = 5},
    {replacement = true, spawned = true, pending = 0, replacements = 1, lifetime = 4}
}) do
    test.system("ai", "spawn_attempt", {replacement = case.replacement, spawned = case.spawned}, function(data)
        assert(data.pending == case.pending and data.replacement == case.replacements,
            "生成机会结束后没有消耗对应普通队列或补员队列")
        assert(data.lifetime == case.lifetime and data.budget == 7 and data.lastDay == 12,
            "概率落空计入出生数、误扣预算或未记录处理日期")
    end, "自然刷新队列：补员=" .. tostring(case.replacement) .. "，出生=" .. tostring(case.spawned))
end

test.system("ai", "tree_habitat_batch", {}, function(data)
    assert(data.seamNear and not data.seamTooFar and not data.flatTooFar,
        "树木栖息地在环绕接缝或普通世界错误计算了与玩家的距离")
    assert(not data.exactBoundary and data.withinBoundary and not data.emptyPlayers,
        "树木栖息地改变了严格半径边界或无玩家时的候选资格")
    assert(data.matchesOriginal and data.wrappedBiome and data.matched == 3 and data.missing == 3,
        "树木批量群系查询与原地块采样不一致，或未拒绝未知地块")
end, "树木栖息地批量寻址保持群系、平台、接缝及距离规则")

-- 调用真实动物产蛋日历，不把离线时间变成积压产蛋。
local cases = {
    {name = "最短两天", args = {day = 10, minimum = 0, delay = 0, checkDay = 11.99}, deadline = 12, due = false, next = 13.99},
    {name = "截止当天", args = {day = 10, delay = 4, sample = .5, checkDay = 14}, deadline = 14, due = true, next = 18},
    {name = "随机值下界", args = {day = 10, delay = 4, sample = -1, checkDay = 12}, deadline = 12, due = true, next = 14},
    {name = "随机值上界", args = {day = 10, delay = 4, sample = 2, checkDay = 16}, deadline = 16, due = true, next = 22},
    {name = "提高冷却立即生效", args = {day = 10, delay = 0, checkDay = 12, updatedMinimum = 5}, deadline = 12, due = false, next = 14},
    {name = "离线不补发", args = {day = 10, delay = 0, checkDay = 100}, deadline = 12, due = true, next = 102},
    {name = "负延迟归零", args = {day = 10, delay = -4, checkDay = 12}, deadline = 12, due = true, next = 14}
}
for _, case in ipairs(cases) do
    test.system("ai", "egg_schedule", case.args, function(data)
        assert(not data.initiallyDue and data.initialized and not data.rejected, "日历初始化失败")
        assert(math.abs(data.deadline - case.deadline) < .000001, "产蛋截止时间错误")
        assert(data.due == case.due, "冷却资格错误")
        assert(math.abs(data.nextDeadline - case.next) < .000001, "下轮应从实际成功日重新计时")
    end, "产蛋日历：" .. case.name)
end
for _, field in ipairs({"day", "minimum", "delay", "sample"}) do
    test.system("ai", "egg_schedule", {invalid = field}, function(data)
        assert(data.rejected and not data.initiallyDue, "非法非有限时间没有被拒绝")
    end, "产蛋日历拒绝非有限参数：" .. field)
end

for _, blocked in ipairs({false, true}) do
    test.system("ai", "state_machine", {blocked = blocked}, function(data)
        local expected = blocked
            and {"idle.enter", "idle.tick", "idle.tick", "idle.exit", "idle.enter", "idle.tick"}
            or {"idle.enter", "idle.tick", "idle.exit", "transition", "chase.enter", "chase.tick", "chase.exit", "idle.enter", "idle.tick"}
        assert(#data.events == #expected, "状态切换额外执行了节点回调")
        for index, value in ipairs(expected) do assert(data.events[index] == value, "旧节点退出、宿主切换、新节点进入顺序错误") end
        local state = blocked and "idle" or "chase"
        assert(data.afterEvaluation == state and data.afterRejected == state, "禁止或未知状态切换破坏当前节点")
        assert(data.repeatedTransitions == 0 and data.duplicateRejected and data.missingRejected and data.uninitializedRejected, "状态机身份或生命周期保护失效")
        assert(not data.resetInitialized and data.reinitialized and data.chaseAnimation == "Moving", "回收重入或动画语义错误")
    end, blocked and "AI状态机：被拦截时保持旧节点" or "AI状态机：正式评估切换与回收重入")
end
for _, case in ipairs({
    {name = "目标暂时缺失", args = {}, firstMoves = 1, firstArrivals = 0},
    {name = "非有限目标失效", args = {invalid = "nonfinite"}, firstMoves = 1, firstArrivals = 0},
    {name = "到达阈值最少五厘米", args = {targetX = .04, arrivalDistance = -1}, firstMoves = 0, firstArrivals = 1}
}) do
    test.system("ai", "advance_target", case.args, function(data)
        assert(data.firstMovements == case.firstMoves and data.firstArrivals == case.firstArrivals, "有效目标推进或到达阈值错误")
        assert(data.invalidMovements == case.firstMoves and data.invalidStops >= 1, "失效目标继续提交移动")
        assert(data.repeatedArrivalCount == case.firstArrivals + 1, "停在目标处重复通知到达")
        assert(data.returnArrivalCount == case.firstArrivals + 2 and data.arrivals == case.firstArrivals + 3, "离开再到达或节点重入不重新通知")
        assert(data.enters == 2 and data.exits == 2 and data.animation == "Moving", "推进节点生命周期错误")
    end, "AI目标推进：" .. case.name)
end
for _, case in ipairs({
    {name = "前摇与攻击窗口独立", args = {}, immediate = false, restored = 1},
    {name = "零前摇立即进入窗口", args = {windup = 0}, immediate = true, restored = 1},
    {name = "负配置钳制", args = {windup = -1, restore = -1}, immediate = true, restored = 0}
}) do
    test.system("ai", "attack_window", case.args, function(data)
        assert(data.startLocked and data.startActive == case.immediate and data.firstActive == case.immediate, "攻击提前生效或前摇没有锁定")
        assert(data.afterWindupActive, "前摇结束没有开启真实伤害窗口")
        assert(not data.closedActive and data.recoveryActive and data.cooldownAfterClose == 1, "伤害窗口关闭后没有进入后摇冷却")
        assert(not data.recoveryRestartActive, "后摇中重复起手重新开启窗口")
        assert(not data.afterRecoveryLocked and not data.afterRecoveryDone and data.cooldownDone, "后摇和冷却时钟被混为一项")
        assert(data.restored == case.restored and not data.stoppedLocked, "冷却恢复或取消窗口残留临时锁")
    end, "AI攻击时序：" .. case.name)
end
for _, case in ipairs({
    {name = "受击记忆跨状态保留", args = {}, ordinary = 2, previous = false, remembered = true},
    {name = "无来源只登记本状态受击", args = {hasOrigin = false}, ordinary = 2, previous = false, remembered = false},
    {name = "零记忆不造威胁", args = {memory = 0}, ordinary = 2, previous = false, remembered = false},
    {name = "负时间不推进", args = {step = -1}, ordinary = 4, previous = true, remembered = true}
}) do
    test.system("ai", "behavior_memory", case.args, function(data)
        assert(data.ordinaryAfter == case.ordinary and data.worldAfter == 10 and data.previousThreat == case.previous, "模块计时重复推进世界计时或负时间")
        assert(data.damaged and data.remembered == case.remembered, "受击事实或来源记忆错误")
        assert(not data.newStateDamaged and data.newStateThreat == case.remembered and data.stateElapsed == 0, "切换状态未清瞬时事实或丢失持续记忆")
        if case.remembered then assert(data.originX == 7 and data.originY == 8, "实际受击来源位置没有冻结") end
        assert(data.worldAfterWorldTick == 5 and data.originalAfterExportMutation == case.ordinary, "世界计时或导出快照污染原数据")
        assert(data.ordinaryElapsed and not data.finalThreat, "到期计时或威胁记忆不清退")
    end, "AI行为图记忆：" .. case.name)
end
for _, case in ipairs({
    {name = "目标体型扩大感知", args = {}, effective = 8},
    {name = "负半径", args = {radius = -1}, effective = 0},
    {name = "负目标倍率", args = {multiplier = -1}, effective = 0},
    {name = "非法半径", args = {invalidRadius = true}, effective = 0},
    {name = "非法目标倍率回到一", args = {invalidMultiplier = true}, effective = 4},
    {name = "零目标倍率", args = {multiplier = 0}, effective = 0}
}) do
    test.system("ai", "perception_radius", case.args, function(data)
        assert(data.effective == case.effective, "正式目标体型感知规则错误")
    end, "AI感知范围：" .. case.name)
end

test.system("ai", "perception_shape_batch", {}, function(data)
    assert(data.shapes and data.added == 2, "形状转换没有保持旋转、负缩放及已有形状偏移")
    assert(data.bounds and data.withoutOutput, "合并计算的粗筛包围盒与独立计算不一致")
    assert(data.rejectedEmpty, "空形状仍登记目标或修改了已有 Burst 几何列表")
end, "Actor 感知形状一次变换生成包围盒和 Burst 数据")

test.system("ai", "closest_target_batch", {}, function(data)
    assert(data.unwrappedNearest and data.wrappedTieFirst,
        "目标筛选的无限世界距离、环绕接缝或同距离优先级改变")
    assert(data.playerExcluded and data.playerIncluded and data.playerWithNoTags,
        "玩家威胁识别或目标标签过滤规则改变")
    assert(data.emptyRejected and data.caseSensitive and data.runtimeUnmatched and
        data.movedTargetUpdated and data.runtimeEntryConsistent,
        "目标筛选的空标签、区分大小写、实时位置或公开调用入口改变")
end, "AI 最近目标批量距离计算保持接缝、过滤及动态候选语义")

test.system("ai", "detector_apply_batch", {}, function(data)
    assert(data.initialUnique and data.initialTags and data.staleIgnored,
        "感知结果应用没有去重或更新标签，或接受了过期版本")
    assert(data.reordered and data.reusedTags and data.changedTags,
        "感知批次改变了候选顺序、原标签列表引用或实时标签更新")
    assert(data.debugUpdated and data.debugOffReused and data.cleared,
        "打开再关闭感知 Debug 后标签和目标状态失效，或清空批次仍残留对象")
end, "普通感知跳过仅供 Debug 的进出差集，标签和切换语义保持一致")

test.system("ai", "stale_perception_batch", {}, function(data)
    assert(data.matched and data.superseded and data.missingPreserved,
        "感知请求版本相同、过期或检测器已销毁时筛选资格错误")
    assert(data.staleCount == 3 and data.nextCount == 2 and
        data.nextFirst == 32 and data.nextSecond == 54 and data.emptyCount == 0,
        "丢弃过期感知候选后未正确消费 NativeStream 或破坏下一组结果")
end, "过期感知批次跳过无效视线与碰撞计算，完整消费结果流")


test.system("ai", "spawner_update_work", {}, function(data)
    assert(data.initialization and data.due and data.overdue,
        "生物调度初始化、到期边界或过期调度被空转守卫跳过")
    assert(not data.idle and data.birthPending and not data.birthCompleted,
        "空转帧仍创建世界上下文，或分帧出生未完成时停止推进")
    assert(not data.disabled and not data.missingSettings,
        "调度器停用或缺少配置时仍进入已初始化世界")
    assert(data.nanNow and data.nanNext and data.infiniteNow and
        not data.infiniteNext and not data.negativeInfinityNow,
        "非有限时钟与原先小于下一Tick的比较语义不一致")
end, "生物调度空转跳过世界上下文，初始化与分帧出生保持")

test.system("ai", "network_snapshot_buffer", {}, function(data)
    assert(data.incomplete and data.duplicatePart and data.assembled,
        "生态快照没有等待完整分片，或重复/乱序分片破坏了正式结果")
    assert(data.mixedRealm and data.mixedWorld and data.oldRejected and data.resetAcceptsNewRealm,
        "旧世界、旧代际或过期序号污染了新世界生态呈现")
    assert(data.newerReplacesIncomplete and data.emptyClears and data.duplicateGuid and data.invalidRejected,
        "缺片后的新快照不能恢复，或空快照/身份去重/非法坐标边界失效")
end, "联机生态完整快照：乱序、缺片恢复、代际与身份边界")
