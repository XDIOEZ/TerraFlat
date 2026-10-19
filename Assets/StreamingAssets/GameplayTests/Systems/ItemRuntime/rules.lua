-- 物质条件表的读取顺序、固液分支与临界状态保持不变。
test.system("item_runtime", "simulation_region_shortcut", {}, function(data)
    assert(data.identical and data.samples > 10000,
        "物品模拟区域快路径改变原有距离分档或未覆盖足够半径样本")
    assert(data.nearSkipped > 0 and data.stoppedSkipped > 0 and data.preciseFallbacks > 0,
        "明确近/远区域没有跳过开平方，或边界区域没有保留原精算")
    assert(data.infiniteStopped and data.invalidFallsBack,
        "无同场景玩家或无效距离的判档规则改变")
end, "物品区域判档：近远快速区与临界距离原算法保持一致")

test.system("item_runtime", "initial_animator_activation", {}, function(data)
    assert(data.queuedWhileInactive and data.stillQueued and data.disabledNotPlayed,
        "停用候选或禁用 Animator 的初始播放未延迟，可能重复输出 Unity 动画错误")
    assert(data.cancelClears and data.poolClears,
        "初始动画的过期待播状态没有在取消或对象池复用时清理")
end, "物品初始化动画延迟到正式激活，取消与对象池复用安全")

test.system("item_runtime", "matter_transition_scan", {}, function(data)
    assert(data.firstMatch and data.secondMatch,
        "物质转换条件没有按既有配置优先级选择首个匹配项")
    assert(data.dryBoundary and data.dryRejected and data.uninitializedRejected,
        "物质转换条件没有在湿度阈值与未初始化时实时更新")
    assert(data.noLiquidMatch and data.emptyRejected and data.nullRejected,
        "物质固液类型或空定义的拒绝规则改变")
end, "材料只读条件表索引扫描保持优先级、温湿度边界及空值语义")

-- 检查模块结构版本和正式物品工厂的可变状态隔离。
test.system("item_runtime", "cleanup_lookup", {}, function(data)
    assert(data.stable, "场景销毁期间无搜索单例读取不稳定")
    assert(data.allocatedBytes == 0, "场景销毁期间无搜索单例读取仍产生 GC 分配")
end, "物品批量销毁时的单例只读查询")

test.system("item_runtime", "world_matter_structure", {}, function(data)
    assert(data.initialCurrent and not data.initialBlocked and data.stableAllowed,
        "普通散落物资格缓存没有保留正常物品")
    assert(data.stableAllocatedBytes == 0, "散落物稳态资格检查仍分配层级组件数组")
    assert(data.addInvalidated and data.receiverBlocked and data.receiverMatchesReference,
        "新增地块接收能力后仍重复推进世界物质")
    assert(data.removeInvalidated and data.afterRemoveAllowed,
        "移除地块接收能力后物品没有恢复世界物质资格")
    assert(data.aiBlocked and data.aiMatchesReference, "缓存遗漏自定义 AI 能力")
    assert(data.rebindInvalidated and data.generationInvalidated and data.generationAllowed,
        "换载荷或重新加载后仍使用旧代际资格")
    assert(data.combustionCached and data.inactiveCombustionExcluded and
        data.combustionRemovalInvalidated and data.combustionVisualRemoved,
        "散落物燃烧模块缓存未绑定正式模块身份或未随结构变更失效")
end, "世界散落物资格缓存、能力变更与零分配热路径")

for _, row in ipairs({
    { water = false, temperature = 1100, rate = 75, seconds = 2, expected = 170 },
    { water = true, temperature = 5, rate = 75, seconds = 2, expected = 5 },
    { water = false, temperature = 1100, rate = 75, seconds = 0, expected = 20 }
}) do
    test.system("item_runtime", "world_matter_contact", row, function(data)
        assert(math.abs(data.temperature - row.expected) < .0001, "复用地形液体定义后接触传热规则变化")
        assert(data.waterContact == (row.water and row.seconds > 0), "水与岩浆的真实接触分类变化")
        assert(data.amount == 7, "环境推进修改了整堆物品数量")
    end, "世界散落物复用液体定义：" .. tostring(row.water) .. "/" .. row.seconds)
end

test.system("item_runtime", "module_structure", { stock = 2 }, function(data)
    assert(data.added == data.before + 1, "新增模块没有发布结构变化")
    assert(data.same == data.added, "原引用赋值误增结构版本")
    assert(data.disabled == data.same + 1, "禁用模块没有失效调度")
    assert(data.payload == data.disabled and data.stock == 2, "库存载荷变化被当成模块结构变化")
    assert(data.removed and data.remaining == 0 and data.afterRemove == data.payload + 1, "移除模块没有更新结构")
    assert(data.afterDetachedEnable == data.afterRemove, "被移除模块仍回写宿主结构")
end, "模块增删启停与载荷通知边界")

for _, row in ipairs({ { amount = 3, weight = 2, volume = 4, stock = 7 }, { amount = 0, weight = 0.5, volume = 1.5, stock = 0 } }) do
    test.system("item_runtime", "clone_runtime", row, function(data)
        assert(data.guid == data.sourceGuid, "复制运行态改变了持久身份")
        assert(data.independentStack and data.independentModules, "复制后共享可变库存或模块")
        assert(data.initialAmount == row.amount and data.sourceAmount == row.amount, "修改副本污染原数量")
        assert(data.initialStock == row.stock and data.sourceStock == row.stock, "修改副本污染原模块库存")
        assert(not data.sourceHasAddedTag, "副本标签污染原配置")
        assert(data.sourceWeight == row.amount * row.weight and data.sourceVolume == row.amount * row.volume, "重量体积计算错误")
    end, "物品独立运行态：数量 " .. row.amount)
end

-- 单图动画只摆动独立视觉层，物品根、刚体与原贴图局部姿态都必须保持原值。
local function checkAnimationBoundary(data)
    assert(#data.samples > 0, "单图动画没有返回真实姿态采样")
    for _, sample in ipairs(data.samples) do
        assert(sample.rootPositionError < .00001 and sample.bodyPositionError < .00001,
            "单图动作修改了物品根或刚体位置")
        assert(sample.rootRotationError < .001 and sample.rootScaleError < .00001,
            "单图动作修改了物品根旋转或缩放")
        assert(sample.localPositionError < .00001 and sample.localRotationError < .001 and sample.localScaleError < .00001,
            "单图表现覆盖了原贴图节点的局部姿态")
        if not sample.parentRestored then
            assert(sample.hasBaseMatrix, "活动单图层没有提供基础几何矩阵")
        end
        if sample.hasBaseMatrix then
            assert(sample.basePositionError < .0002 and sample.baseMatrixError < .0002,
                "单图基础几何矩阵仍含程序姿态，或丢失原贴图与外部父层变换")
        end
        for _, field in ipairs({"x", "y", "z", "angle", "scaleX", "scaleY"}) do
            assert(type(sample[field]) == "number" and sample[field] == sample[field] and math.abs(sample[field]) < math.huge,
                "单图姿态含无效数值：" .. field)
        end
    end
end

local function checkAnimationRest(sample)
    assert(math.abs(sample.x) < .0002 and math.abs(sample.y) < .0002 and math.abs(sample.z) < .0002,
        "单图动作结束后没有回到原位置")
    assert(math.abs(sample.angle) < .005 and math.abs(sample.scaleX - 1) < .0002 and math.abs(sample.scaleY - 1) < .0002,
        "单图动作结束后没有恢复原角度和缩放")
    assert(not sample.attacking, "单图攻击结束后仍保留攻击状态")
end

for _, network in ipairs({false, true}) do
    local steps = {}
    for index = 1, 20 do steps[index] = {moving = true, deltaTime = .08} end
    for index = 21, 36 do steps[index] = {moving = false, deltaTime = .08} end
    test.system("item_runtime", "single_sprite_animation", {network = network, steps = steps}, function(data)
        checkAnimationBoundary(data)
        local left, right, lifted = false, false, false
        for index = 1, 20 do
            left = left or data.samples[index].angle < -.1
            right = right or data.samples[index].angle > .1
            lifted = lifted or data.samples[index].y > .005
        end
        assert(left and right and lifted, "主动走路没有表现左右摇摆与起伏")
        checkAnimationRest(data.samples[#data.samples])
    end, network and "单图远端移动与停止复位" or "单图本地主动移动与停止复位")
end

test.system("item_runtime", "single_sprite_animation", {steps = {
    {moving = false, velocityX = 4, deltaTime = .1}, {deltaTime = .1}, {deltaTime = .1}
}}, function(data)
    checkAnimationBoundary(data)
    for _, sample in ipairs(data.samples) do
        assert(sample.bodySpeed > 3, "被动移动夹具没有提供真实刚体速度")
        checkAnimationRest(sample)
    end
end, "单图被动漂移不误播迈步")

for _, direction in ipairs({
    {name = "右", x = 1, y = 0}, {name = "左", x = -1, y = 0},
    {name = "上", x = 0, y = 1}, {name = "下", x = 0, y = -1},
    {name = "镜像父级向右", x = 1, y = 0, mirrored = true}
}) do
    local steps = {{attack = true, x = direction.x, y = direction.y, deltaTime = .06}}
    for index = 2, 18 do steps[index] = {deltaTime = .08} end
    test.system("item_runtime", "single_sprite_animation", {mirrored = direction.mirrored == true, steps = steps}, function(data)
        checkAnimationBoundary(data)
        local first = data.samples[1]
        assert(first.attacking and first.x * direction.x + first.y * direction.y > .04,
            "单图攻击没有朝实际世界攻击方向前探")
        checkAnimationRest(data.samples[#data.samples])
    end, "单图攻击前探与归位：" .. direction.name)
end

test.system("item_runtime", "single_sprite_animation", {steps = {
    {jump = true, deltaTime = .105}, {deltaTime = .105}, {deltaTime = .105}, {deltaTime = .105},
    {deltaTime = .1}, {deltaTime = .1}, {deltaTime = .1}, {deltaTime = .1}
}}, function(data)
    checkAnimationBoundary(data)
    assert(data.samples[1].y > .01 and data.samples[2].y > data.samples[1].y,
        "单图跳跃没有向上起跳")
    assert(data.samples[2].y > .1 and data.samples[3].y < data.samples[2].y,
        "单图跳跃没有从最高点落下")
    checkAnimationRest(data.samples[#data.samples])
end, "单图跳跃起落与原地复位")

test.system("item_runtime", "single_sprite_animation", {steps = {
    {attack = true, jump = true, deltaTime = .08},
    {mode = "Animator", deltaTime = .08},
    {mode = "SingleSprite", moving = true, deltaTime = .08},
    {lifecycle = "disable"},
    {lifecycle = "enable", moving = false, deltaTime = 0},
    {attack = true, jump = true, deltaTime = .08},
    {lifecycle = "unload"}, {lifecycle = "unload"},
    {lifecycle = "load", deltaTime = 0}, {lifecycle = "load", deltaTime = 0},
    {attack = true, jump = true, deltaTime = .08}, {reset = true, deltaTime = 0},
    {mode = "Animator", deltaTime = 0}
}}, function(data)
    checkAnimationBoundary(data)
    assert(data.samples[1].attacking and data.samples[1].y > .01, "中断动作夹具没有先启动攻击和跳跃")
    for _, index in ipairs({2, 4, 5, 7, 8, 9, 10, 12, 13}) do checkAnimationRest(data.samples[index]) end
    for _, index in ipairs({2, 4, 7, 8, 13}) do
        assert(data.samples[index].parentRestored, "切模式或卸载后没有恢复原父级和同级顺序")
    end
    assert(not data.samples[7].loaded and not data.samples[8].loaded and data.samples[9].loaded and data.samples[10].loaded,
        "单图模块重复加载或卸载的生命周期不正确")
end, "单图中断切换、重复启停、层级恢复与清除残留动作")

test.system("item_runtime", "single_sprite_animation", {steps = {
    {moving = true, deltaTime = .08}, {externalY = 180, deltaTime = .08},
    {moving = false, mode = "Animator", deltaTime = 0},
    {mode = "SingleSprite", attack = true, deltaTime = .04}, {lifecycle = "unload"}
}}, function(data)
    checkAnimationBoundary(data)
    for index = 2, #data.samples do
        assert(data.samples[index].localFacingX < -.9, "切模式或释放单图层覆盖了外部最新翻面")
    end
    assert(data.samples[3].parentRestored and data.samples[5].parentRestored,
        "外部翻面后没有恢复原视觉层级")
end, "单图层释放保留外部最新Y轴翻面")

test.system("item_runtime", "single_sprite_animation", {withAnimator = true, steps = {
    {attack = true, jump = true, deltaTime = .08},
    {componentEnabled = false, attack = true, jump = true, hit = true},
    {attack = true, jump = true, hit = true},
    {componentEnabled = true, deltaTime = .08},
    {attack = true, deltaTime = .06},
    {componentEnabled = false, attack = true, jump = true, hit = true}
}}, function(data)
    checkAnimationBoundary(data)
    assert(data.samples[1].attacking and not data.samples[1].bodyAnimatorEnabled,
        "停用边界夹具没有先进入单图动作并暂停身体Animator")
    for _, index in ipairs({2, 3, 6}) do
        local sample = data.samples[index]
        checkAnimationRest(sample)
        assert(not sample.componentEnabled and sample.parentRestored and sample.bodyAnimatorEnabled,
            "停用动画组件后动作入口重新建立了单图层或卡住身体Animator")
    end
    checkAnimationRest(data.samples[4])
    assert(data.samples[5].componentEnabled and data.samples[5].attacking and data.samples[5].x > .04,
        "重新启用动画组件后无法正常播放新攻击")
end, "单图组件停用拒绝攻击跳跃受击且恢复身体Animator")

for _, lifecycle in ipairs({"despawn", "destroy_component", "destroy_owner"}) do
    test.system("item_runtime", "single_sprite_animation", {
        nestedVisual = true, withAdditionalVisual = true, finalLifecycle = lifecycle,
        steps = {{attack = true, jump = true, externalY = 180, deltaTime = .06}}
    }, function(data)
        checkAnimationBoundary(data)
        local final = data.lifecycle
        assert(#final.errors == 0, "单图生命周期产生 Unity 错误：" .. table.concat(final.errors, "；"))
        assert(final.poseReleased and final.additionalPoseReleased,
            "动画收尾没有释放身体或附加视觉的临时父层")
        if lifecycle == "destroy_owner" then
            assert(final.ownerDestructionStarted and final.ownerDestroyed and final.visualDestroyed and final.additionalDestroyed,
                "宿主销毁没有同步销毁单图视觉，或提前把视觉移出了销毁层级")
            assert(final.poseDestroyed and final.additionalPoseDestroyed,
                "宿主销毁后仍留下独立单图表现层")
        else
            assert(not final.ownerDestructionStarted and not final.ownerDestroyed and not final.visualDestroyed and not final.additionalDestroyed,
                "普通回收或单独删除动画组件误判为宿主销毁")
            assert(final.parentRestored and final.additionalParentRestored and final.additionalSiblingRestored and final.localFacingX < -.9,
                "存活宿主的身体或附加视觉没有恢复真实父级和同级顺序，或最新翻面被覆盖")
            assert(final.localPositionError < .00001 and final.localScaleError < .00001,
                "存活视觉恢复层级时丢失了原局部位置或缩放")
        end
    end, "单图宿主销毁、模块删除与回收层级边界：" .. lifecycle)
end

local motionSteps = {{moving = true, source = {y = .3, roll = 12},
    legacyState = "Walk", legacyMove = true, deltaTime = .08}}
for index = 2, 21 do motionSteps[index] = {moving = false, source = {moving = true}, deltaTime = .08} end
for index = 22, 37 do motionSteps[index] = {velocityX = 4, source = {moving = false}, deltaTime = .08} end
motionSteps[38] = {source = {available = false}, deltaTime = .08}
motionSteps[39] = {source = {moving = true, y = .3, roll = 12}, sourceEnabled = false, deltaTime = .08}
motionSteps[40] = {sourceEnabled = true, deltaTime = .08}
test.system("item_runtime", "single_sprite_animation", {withMotionSource = true, withAnimator = true, steps = motionSteps}, function(data)
    checkAnimationBoundary(data)
    local first = data.samples[1]
    assert(math.abs(first.y - .3) < .0001 and math.abs(first.angle - 12) < .005,
        "特殊移动信号没有优先于Mover，或已有高度倾角没有交给共用表现层")
    assert(first.hasBaseMatrix and first.basePositionError < .0002 and first.baseMatrixError < .0002,
        "特殊视觉偏移改变了提供给物理系统的基础几何位置")
    assert(first.legacyStateAccepted and first.legacyMoveAccepted and not first.bodyAnimatorEnabled,
        "单图通用旧状态入口没有被接管或重新启用了身体Animator")
    local left, right = false, false
    for index = 2, 21 do
        left = left or data.samples[index].angle < -.1
        right = right or data.samples[index].angle > .1
    end
    assert(left and right, "飞行或游动主动信号没有驱动共用左右摆动")
    for _, index in ipairs({37, 38, 39}) do
        assert(data.samples[index].bodySpeed > 3, "被动风速夹具没有保留真实刚体速度")
        checkAnimationRest(data.samples[index])
    end
    assert(data.samples[40].y > .3, "重新启用主动表现信号后没有恢复共用动画")
end, "特殊主动信号、旧状态接管、被动风速拒绝与来源启停")

test.system("item_runtime", "single_sprite_animation", {network = true, withMotionSource = true, steps = {
    {moving = false, source = {moving = true, y = .3, roll = 12}, deltaTime = .08},
    {moving = false, deltaTime = .08}
}}, function(data)
    checkAnimationBoundary(data)
    for _, sample in ipairs(data.samples) do checkAnimationRest(sample) end
end, "单图远端显式状态优先于本地特殊来源")

test.system("item_runtime", "single_sprite_animation", {withSpriteStates = true, withAnimator = true, steps = {
    {legacyState = "Flying"},
    {legacyState = "Base Layer.Flying", forceRestart = false, moving = true},
    {legacyState = "Missing"},
    {legacyState = "Broken"},
    {legacyState = "Landing"},
    {legacyState = "Ground"},
    {sprite = "alternate"},
    {sprite = "null"},
    {legacyState = "Flying"},
    {componentEnabled = false, sprite = "alternate", legacyState = "Flying"},
    {componentEnabled = true, legacyState = "Flying"},
    {lifecycle = "unload"},
    {lifecycle = "load", legacyState = "Flying"},
    {legacyState = "Flying"},
    {mode = "Animator"},
    {mode = "SingleSprite", legacyState = "Flying"},
    {reset = true}
}}, function(data)
    checkAnimationBoundary(data)
    for _, index in ipairs({1, 2, 9, 11, 14, 16}) do
        assert(data.samples[index].legacyStateAccepted and data.samples[index].sprite == "flying",
            "单图飞行状态未换图，或重复状态、重新启用及重新加载后不能换图：" .. index)
        assert(not data.samples[index].bodyAnimatorEnabled, "切换状态贴图重新启用了身体Animator")
    end
    for _, index in ipairs({3, 4}) do
        assert(not data.samples[index].legacyStateAccepted and data.samples[index].sprite == "flying",
            "未知状态或空贴图没有拒绝，或拒绝时改坏当前贴图")
    end
    assert(data.samples[5].sprite == "landing" and data.samples[6].sprite == "original",
        "降落与落地没有切换到各自贴图")
    assert(data.samples[7].spriteAccepted and data.samples[7].sprite == "landing" and
        not data.samples[8].spriteAccepted and data.samples[8].sprite == "landing",
        "直接更换Sprite失败，或空Sprite破坏当前图片")
    assert(not data.samples[10].spriteAccepted and not data.samples[10].legacyStateAccepted,
        "停用动画组件后仍接受换图")
    for _, index in ipairs({10, 12, 13, 15, 17}) do
        assert(data.samples[index].sprite == "original", "停用、卸载、切模式或复位后残留状态贴图：" .. index)
    end
end, "单图状态换图、直接换图、拒绝边界与生命周期还原")

-- 死亡沿正式生命事件和共用推进入口采样，Lua只判断动作与底边，不重算动画曲线。
local function checkDeadRest(sample, sprite)
    assert(sample.dead and not sample.deathPlaying and not sample.attacking, "死亡落地后没有保持终态")
    assert(math.abs(math.abs(sample.angle) - 180) < .02, "死亡落地没有保持翻肚皮")
    assert(math.abs(sample.bottomOffset) < .0003, "翻转后的图片底边没有落回中性地面")
    assert(sample.sprite == sprite, "死亡终态没有保持死亡瞬间的图片")
end

for _, geometry in ipairs({
    {name = "底角枢轴"},
    {name = "镜像父级和偏心枢轴", mirrored = true, rootScaleX = 1.7, rootScaleY = .65, pivotX = .2, pivotY = .7},
    {name = "贴图双翻面和非等比缩放", flipX = true, flipY = true, visualScaleX = .65, visualScaleY = 1.8, pivotX = .8, pivotY = .3}
}) do
    geometry.withHealth, geometry.withSpriteStates, geometry.nestedVisual = true, true, true
    geometry.steps = {
        {legacyState = "Flying", deltaTime = 0},
        {kill = true, deltaTime = 0},
        {deltaTime = .1}, {deltaTime = .28}, {deltaTime = .2}, {deltaTime = .19}, {deltaTime = 2}
    }
    test.system("item_runtime", "single_sprite_animation", geometry, function(data)
        checkAnimationBoundary(data)
        local samples = data.samples
        assert(samples[2].hp == 0 and samples[2].deathStartedCount == 1 and samples[2].dead and samples[2].deathPlaying,
            "正式生命DeathStarted没有启动单图死亡")
        assert(samples[3].bottomOffset > .02 and samples[4].bottomOffset > samples[3].bottomOffset,
            "死亡没有先向上抛起")
        assert(math.abs(samples[4].angle) > 10 and math.abs(samples[4].angle) < 179,
            "死亡抛起过程中没有旋转")
        assert(samples[5].bottomOffset < samples[4].bottomOffset, "死亡没有从最高点回落")
        for index = 2, #samples do
            assert(samples[index].bottomOffset >= -.0003 and samples[index].sprite == "flying",
                "死亡途中穿入地面或换掉了死亡瞬间图片")
        end
        checkDeadRest(samples[6], "flying")
        checkDeadRest(samples[7], "flying")
    end, "单图死亡上抛旋转、底边落地与终态保持：" .. geometry.name)
end

test.system("item_runtime", "single_sprite_animation", {withHealth = true, withSpriteStates = true, withAnimator = true, steps = {
    {legacyState = "Flying", attack = true, jump = true, hit = true, deltaTime = .06},
    {kill = true, death = true, deltaTime = 0},
    {attack = true, jump = true, hit = true, sprite = "alternate", legacyState = "Landing", moving = true, deltaTime = .1},
    {kill = true, legacyState = "Death", forceRestart = true, deltaTime = .15},
    {legacyState = "Base Layer.Dead", forceRestart = true, deltaTime = .15},
    {death = true, deltaTime = .37},
    {attack = true, jump = true, hit = true, sprite = "alternate", legacyState = "Ground", deltaTime = .5},
    {death = true, legacyState = "Death", forceRestart = true, deltaTime = .1}
}}, function(data)
    checkAnimationBoundary(data)
    assert(data.samples[1].attacking and data.samples[1].y > .01, "死亡优先级夹具没有先播放存活动作")
    assert(data.samples[2].deathAccepted and data.samples[2].deathPlaying, "公开死亡入口未接管进行中的存活动作")
    for index = 2, #data.samples do
        local sample = data.samples[index]
        assert(sample.dead and not sample.attacking and sample.sprite == "flying" and not sample.bodyAnimatorEnabled,
            "攻击、跳跃、受击、移动或换图打断了死亡展示")
        assert(sample.deathStartedCount == 1, "重复致死重新发布了同一轮生命死亡事件")
    end
    for _, index in ipairs({3, 7}) do
        assert(not data.samples[index].spriteAccepted and not data.samples[index].legacyStateAccepted,
            "死亡中仍接受了换图或普通状态")
    end
    for _, index in ipairs({4, 5, 8}) do assert(data.samples[index].legacyStateAccepted, "旧Death/Dead状态入口没有接入死亡") end
    for _, index in ipairs({6, 7, 8}) do checkDeadRest(data.samples[index], "flying") end
    assert(data.samples[6].deathAccepted and data.samples[8].deathAccepted,
        "死亡重复调用不能幂等接收，或完成后的重复调用重新起跳")
end, "单图死亡抢占动作、固定图片、旧状态接入和重复死亡不重播")

test.system("item_runtime", "single_sprite_animation", {withHealth = true, withSpriteStates = true, withMotionSource = true, steps = {
    {legacyState = "Flying", source = {y = .6, roll = 12}, deltaTime = 0},
    {kill = true, deltaTime = 0},
    {source = {available = false}, legacyState = "Ground", deltaTime = .1},
    {source = {y = 8, roll = 70, moving = true}, deltaTime = .67},
    {sourceEnabled = false, deltaTime = 1}
}}, function(data)
    checkAnimationBoundary(data)
    assert(math.abs(data.samples[1].y - .6) < .0002, "飞行死亡夹具没有先建立真实表现高度")
    assert(math.abs(data.samples[2].y - data.samples[1].y) < .0003,
        "飞行死亡起点瞬间丢掉了已有高度")
    assert(data.samples[3].bottomOffset >= -.0003 and data.samples[3].sprite == "flying",
        "飞行来源回地面时提前改图或穿地")
    checkDeadRest(data.samples[4], "flying")
    checkDeadRest(data.samples[5], "flying")
end, "单图飞行死亡保留起始高度，来源回地面后仍落到中性底边")

test.system("item_runtime", "single_sprite_animation", {withHealth = true, withSpriteStates = true, steps = {
    {zeroHp = true, deltaTime = .38}, {deltaTime = .39},
    {revive = true, deltaTime = 0}, {attack = true, deltaTime = .06},
    {kill = true, deltaTime = .77}, {lifecycle = "pool_reuse", moving = false, deltaTime = 0},
    {attack = true, deltaTime = .06}, {kill = true, deltaTime = .77},
    {lifecycle = "unload", deltaTime = 0}, {revive = true, lifecycle = "load", deltaTime = 0}
}}, function(data)
    checkAnimationBoundary(data)
    assert(data.samples[1].dead and data.samples[1].deathPlaying and data.samples[1].deathStartedCount == 0,
        "未收到死亡事件的零血量兜底没有启动死亡展示")
    for _, index in ipairs({2, 5, 8}) do checkDeadRest(data.samples[index], "original") end
    for _, index in ipairs({3, 6, 9, 10}) do
        checkAnimationRest(data.samples[index])
        assert(not data.samples[index].dead and not data.samples[index].deathPlaying,
            "复活、正式取池复位或卸载后残留死亡状态")
    end
    assert(data.samples[3].hp > 0 and data.samples[6].hp > 0 and data.samples[10].hp > 0,
        "复活和同外壳新载荷没有恢复生命")
    assert(data.samples[4].attacking and data.samples[7].attacking,
        "复活或同外壳复用后仍拒绝正常攻击")
    assert(data.samples[5].deathStartedCount == 1 and data.samples[8].deathStartedCount == 2,
        "复用后新生命不能再次触发唯一死亡事件")
    assert(not data.samples[9].loaded and data.samples[9].parentRestored and data.samples[10].loaded,
        "死亡卸载没有恢复层级，或重载未成功")
end, "单图零血量兜底、显式复活、正式回收取池复位与重载")

test.system("item_runtime", "single_sprite_animation", {withHealth = true, withSpriteStates = true, steps = {
    {death = true, deltaTime = .77}, {deltaTime = 1}, {reset = true, deltaTime = 0}
}}, function(data)
    checkAnimationBoundary(data)
    assert(data.samples[1].deathAccepted and data.samples[1].hp > 0, "公开死亡展示入口错误要求先扣血")
    checkDeadRest(data.samples[1], "original")
    checkDeadRest(data.samples[2], "original")
    checkAnimationRest(data.samples[3])
    assert(not data.samples[3].dead, "显式展示复位没有清除手动死亡终态")
end, "单图手动死亡演示保持至显式复位")

-- 真实伤害 Prefab 保留碰撞体引用，死亡关窗与新生命复位都走正式生命周期。
for _, initiallyOpen in ipairs({false, true}) do
    test.system("item_runtime", "single_sprite_animation", {
        withHealth = true, withDamage = true, withSpriteStates = true, damageInitiallyOpen = initiallyOpen,
        steps = {
            {deltaTime = 0}, {damageAttack = true, deltaTime = 0},
            {kill = true, deltaTime = 0}, {damageAttack = true, deltaTime = 0},
            {revive = true, damageReload = true, deltaTime = 0}, {damageAttack = true, deltaTime = 0},
            {kill = true, deltaTime = 0}, {lifecycle = "pool_reuse", deltaTime = 0},
            {damageAttack = true, deltaTime = 0}, {kill = true, deltaTime = 0},
            {damageReload = true, deltaTime = 0}, {lifecycle = "pool_reuse", deltaTime = 0}
        }
    }, function(data)
        checkAnimationBoundary(data)
        for _, index in ipairs({1, 5, 8, 12}) do
            assert(data.samples[index].damageEnabled == initiallyOpen,
                "新生命重载或同外壳复用没有恢复伤害窗首次初态，或误开了原本关闭的窗口")
        end
        for _, index in ipairs({2, 6, 9}) do assert(data.samples[index].damageEnabled, "存活时正常攻击未开启真实伤害窗") end
        for _, index in ipairs({3, 4, 7, 10, 11}) do
            assert(not data.samples[index].damageEnabledBeforeAdvance and not data.samples[index].damageEnabled,
                "致死事件没有立即关窗，或死后攻击与重载重新开启了真实伤害窗")
        end
    end, initiallyOpen and "死亡立即关闭常开接触伤害，新生命恢复常开初态" or "死亡关闭攻击窗，复用不误开首次关闭窗口")
end

for _, species in ipairs({"Bird", "Seagull"}) do
    test.system("item_runtime", "single_sprite_shell_assembly", {actorId = species}, function(data)
        local states = {}
        for _, entry in ipairs(data.stateSprites) do
            assert(not states[entry.state], "鸟类外壳配置了重复的单图状态")
            states[entry.state] = entry.sprite
        end
        for _, state in ipairs({"Ground", "Walk", "TakingOff", "Flying", "Landing"}) do
            assert(states[state] == state .. "_0", "鸟类单图状态没有引用正式首帧：" .. state)
        end
        for _, entry in ipairs(data.birdStates) do
            local expected = entry.phase
            if entry.phase == "Ground" then expected = entry.moving and "Walk" or "Ground" end
            if entry.phase == "RunUp" then expected = "Walk" end
            assert(entry.state == expected and states[entry.state], "鸟类飞行阶段未选中已配置的单图状态")
        end
    end, "鸟类飞行阶段与正式单图贴图配置：" .. species)
end

test.system("item_runtime", "single_sprite_shell_assembly", {actorId = "Wolf"}, function(data)
    assert(data.actorId == "Wolf" and data.templateStableName == "animator" and data.templateIsAnimator,
        "真实狼外壳没有用 animator 稳定槽位参与正式模块编译")
    assert(data.registeredSameInstance and data.resolvedSameInstance and data.stableIdentityRequired,
        "狼动画没有精确复用内嵌组件，或缺少身份时仍被模糊匹配")
    assert(data.unboundRequiresBinding and data.restoredRuntimeIdentity and data.boundAfterRestore and data.reusedIdentityMatches,
        "狼动画首次能力 ID 为空时没有按默认能力绑定，或已绑定组件不能继续复用")
    assert(data.conflictingModuleIdRejected and data.conflictingStableNameRejected and data.conflictingPrefabRejected,
        "狼动画绑定错误地接受了冲突的能力 ID、稳定槽位或具体 Prefab 身份")
    assert(data.bodyAnimatorMatches and data.bodyRendererMatches and data.controllerName == "Wolf",
        "狼动画接收模块没有绑定真实狼贴图和控制器")
    assert(data.animatorCount == 1 and data.rendererCount == 1 and
        data.finalAnimatorCount == data.animatorCount and data.finalRendererCount == data.rendererCount,
        "狼装配重复生成了默认动画机或贴图节点")
    assert(data.singleSprite and data.enabled and data.inactive,
        "狼正式单图配置没有应用到停用外壳中的精确动画组件")
end, "真实狼外壳稳定槽位编译、内嵌动画复用与重复视觉拒绝")

test.system("item_runtime", "module_assembly_identity", {}, function(data)
    assert(#data.awakeBindings == 2, "缺少真实AI或转身模块的Awake身份回归")
    for _, row in ipairs(data.awakeBindings) do
        assert(row.sourceUnchanged and row.restored and row.boundPreserved,
            row.slot .. "在Awake和定义装配之间改写身份或无法生成")
    end
    assert(data.sourceIdentityRestored and data.canonicalIdentityRestored,
        "编译来源的默认身份或Awake补全的正式能力ID无法首次绑定")
    assert(data.sourceStableConflictRejected and data.sourceIdConflictRejected,
        "装配覆盖了既不属于来源模板也不属于当前定义的冲突身份")
    assert(data.nestedExcludedFromCompile and data.nestedRestoreRejected and data.nestedUnchanged and data.nestedPrefabRejected,
        "父物品编译或恢复跨过了子物品边界，或把完整物品当作独立模块")
    assert(data.unrelatedOwnerRejected and data.exactPrefabRestored and data.namedAncestorRejected,
        "独立模块没有恢复精确Prefab组件位置，或仅凭同名祖先接受了错误实现")
    assert(data.shellPrototypeExact and data.prefabPrototypeExact,
        "资源预检没有复用编译选定的内嵌或独立Prefab原型")
    assert(data.inactiveStableNamesDistinct, "停用宿主中同能力模块推导出重复稳定名")
end, "模块来源身份、嵌套物品所有权、精确Prefab位置与停用宿主稳定槽位")
